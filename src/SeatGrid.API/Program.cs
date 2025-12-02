using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Infrastructure.Persistence;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Application.Services;
using SeatGrid.API.Domain.Interfaces;
using SeatGrid.API.Infrastructure.Repositories;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<SeatGridDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IEventRepository, EventRepository>();

// Register all booking service implementations
builder.Services.AddScoped<BookingNaiveService>();
builder.Services.AddScoped<BookingPessimisticService>();
builder.Services.AddScoped<BookingOptimisticService>();

// Strategy pattern: Use factory to resolve the correct implementation based on configuration
builder.Services.AddScoped<IBookingService>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var strategy = configuration.GetValue<string>("Booking:Strategy")?.ToLowerInvariant() ?? "pessimistic";
    
    // Dictionary-based strategy resolution for clean, extensible service selection
    var strategyMap = new Dictionary<string, Func<IServiceProvider, IBookingService>>(StringComparer.OrdinalIgnoreCase)
    {
        ["naive"] = sp => sp.GetRequiredService<BookingNaiveService>(),
        ["pessimistic"] = sp => sp.GetRequiredService<BookingPessimisticService>(),
        ["optimistic"] = sp => sp.GetRequiredService<BookingOptimisticService>()
    };
    
    if (strategyMap.TryGetValue(strategy, out var factory))
    {
        return factory(serviceProvider);
    }
    
    // Default to pessimistic if unknown strategy
    return serviceProvider.GetRequiredService<BookingPessimisticService>();
});

builder.Services.AddScoped<IEventService, EventService>();

// Add Redis connection multiplexer for direct Redis operations
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(redisConnection);
});

// Add Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SeatGrid:";
});

// Add cache services
builder.Services.AddScoped<IAvailabilityCache, AvailabilityCache>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? "",
        name: "postgres",
        timeout: TimeSpan.FromSeconds(3),
        tags: new[] { "ready" })
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis") ?? "",
        name: "redis",
        timeout: TimeSpan.FromSeconds(3),
        tags: new[] { "ready" });

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// --- OpenTelemetry Configuration ---
const string serviceName = "SeatGrid.API";

builder.Logging.AddOpenTelemetry(options =>
{
    options
        .SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService(serviceName))
        .AddOtlpExporter();
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            // WARNING: This should be disabled in production to avoid leaking sensitive data in SQL queries.
            options.SetDbStatementForText = true;
        })
        .AddRedisInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
// --- End OpenTelemetry Configuration ---

var app = builder.Build();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SeatGridDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Only redirect to HTTPS when not running in Docker (ASPNETCORE_URLS will be http only)
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")))
{
    // Skip HTTPS redirection in container
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

// Map health check endpoints
// Liveness: Fast check - just verifies the app is running (no dependency checks)
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Don't run any health checks, just return 200 if app responds
});

// Readiness: Full check - verifies app can handle requests (checks all dependencies)
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
