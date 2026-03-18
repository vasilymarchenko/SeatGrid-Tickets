using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace SeatGrid.API.Infrastructure.Persistence;

public class SeatGridDbContextFactory : IDesignTimeDbContextFactory<SeatGridDbContext>
{
    public SeatGridDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<SeatGridDbContext>();
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        optionsBuilder.UseNpgsql(connectionString);

        // This factory is used only by EF design-time tooling (migrations, scripts).
        // No domain events are dispatched during migrations, so a no-op mediator is correct here.
        return new SeatGridDbContext(optionsBuilder.Options, new NoOpMediator());
    }

    // Satisfies the IMediator contract with empty no-op implementations.
    // Used exclusively by the design-time factory above.
    private sealed class NoOpMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
            => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => EmptyAsyncEnumerable<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => EmptyAsyncEnumerable<object?>();

#pragma warning disable CS1998
        private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>() { yield break; }
#pragma warning restore CS1998
        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
            => Task.CompletedTask;
    }
}
