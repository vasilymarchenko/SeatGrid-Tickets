using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SeatGrid.API.Data;

/// <summary>
/// Design-time factory for creating SeatGridDbContext instances.
/// Used by EF Core CLI tools (dotnet ef migrations add, etc.).
/// </summary>
public class SeatGridDbContextFactory : IDesignTimeDbContextFactory<SeatGridDbContext>
{
    public SeatGridDbContext CreateDbContext(string[] args)
    {
        // Build configuration to read from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<SeatGridDbContext>();
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        optionsBuilder.UseNpgsql(connectionString);

        return new SeatGridDbContext(optionsBuilder.Options);
    }
}
