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
using SeatGrid.API.Application.Decorators;

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
// At startup: Fail fast if Redis unavailable (better to crash than start degraded)
// At runtime: Cache operations have try-catch for resilience
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
    
    var options = ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = true; // Fail fast at startup
    options.ConnectTimeout = 5000;
    options.SyncTimeout = 5000;
    
    logger.LogInformation("Connecting to Redis at {Connection}...", redisConnection);
    
    var multiplexer = ConnectionMultiplexer.Connect(options);
    
    if (!multiplexer.IsConnected)
    {
        throw new InvalidOperationException(
            $"Failed to connect to Redis at {redisConnection}. Application cannot start without cache.");
    }
    
    logger.LogInformation("Successfully connected to Redis at {Connection}", redisConnection);
    return multiplexer;
});

// Add Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SeatGrid:";
});

// Add cache services with decorators for observability
// Core implementations
builder.Services.AddScoped<AvailabilityCache>();
builder.Services.AddScoped<BookedSeatsCache>();

// Decorated versions (adds metrics)
builder.Services.AddScoped<IAvailabilityCache>(sp =>
{
    var inner = sp.GetRequiredService<AvailabilityCache>();
    var logger = sp.GetRequiredService<ILogger<InstrumentedAvailabilityCache>>();
    return new InstrumentedAvailabilityCache(inner, logger);
});

builder.Services.AddScoped<IBookedSeatsCache>(sp =>
{
    var inner = sp.GetRequiredService<BookedSeatsCache>();
    var logger = sp.GetRequiredService<ILogger<InstrumentedBookedSeatsCache>>();
    return new InstrumentedBookedSeatsCache(inner, logger);
});

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

// Validate OTEL Collector is reachable at startup (smoke test)
var otlpEndpoint = builder.Configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT") 
                   ?? "http://localhost:4318";
try
{
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var response = await httpClient.GetAsync($"{otlpEndpoint}/v1/metrics");
    Console.WriteLine($"✓ OTEL Collector reachable at {otlpEndpoint}");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠ WARNING: OTEL Collector not reachable at {otlpEndpoint}");
    Console.WriteLine($"⚠ Reason: {ex.Message}");
    Console.WriteLine($"⚠ Telemetry will be lost! Check OTEL Collector deployment.");
    // Don't crash - but operator is explicitly warned
}

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
        .AddMeter("SeatGrid.API") // Add custom metrics from BookingMetrics
        .AddOtlpExporter());

Console.WriteLine("✓ OpenTelemetry SDK configured");
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

// Observability health endpoint - separate from functional health
// Use this to monitor if telemetry pipeline is working
app.MapGet("/health/observability", () =>
{
    // This endpoint itself generates metrics/traces
    // If you can query this in Prometheus/Tempo, OTEL pipeline works
    return Results.Ok(new
    {
        status = "healthy",
        message = "If you see this metric in Prometheus, OTEL pipeline is working",
        timestamp = DateTime.UtcNow,
        serviceName = "SeatGrid.API"
    });
});

app.Run();
