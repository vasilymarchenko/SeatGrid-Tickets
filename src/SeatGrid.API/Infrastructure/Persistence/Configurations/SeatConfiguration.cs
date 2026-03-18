using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Infrastructure.Persistence.Configurations;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Row).IsRequired().HasMaxLength(10);
        builder.Property(s => s.Col).IsRequired().HasMaxLength(10);

        // Composite index for faster lookups and uniqueness per event.
        builder.HasIndex(s => new { s.EventId, s.Row, s.Col }).IsUnique();

        builder.HasOne<Event>()
               .WithMany()
               .HasForeignKey(s => s.EventId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
