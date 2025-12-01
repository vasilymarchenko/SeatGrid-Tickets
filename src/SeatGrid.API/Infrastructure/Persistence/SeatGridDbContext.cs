using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Infrastructure.Persistence;

public class SeatGridDbContext : DbContext
{
    public SeatGridDbContext(DbContextOptions<SeatGridDbContext> options) : base(options)
    {
    }

    public DbSet<Event> Events { get; set; }
    public DbSet<Seat> Seats { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Seat>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Row).IsRequired().HasMaxLength(10);
            entity.Property(s => s.Col).IsRequired().HasMaxLength(10);
            
            // Composite index for faster lookups and uniqueness per event
            entity.HasIndex(s => new { s.EventId, s.Row, s.Col }).IsUnique();
            
            // Configure foreign key without navigation properties
            entity.HasOne<Event>()
                  .WithMany()
                  .HasForeignKey(s => s.EventId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
