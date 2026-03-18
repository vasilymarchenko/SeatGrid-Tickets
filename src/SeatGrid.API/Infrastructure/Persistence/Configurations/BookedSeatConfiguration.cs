using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Infrastructure.Persistence.Configurations;

public sealed class BookedSeatConfiguration : IEntityTypeConfiguration<BookedSeat>
{
    public void Configure(EntityTypeBuilder<BookedSeat> builder)
    {
        builder.ToTable("BookedSeats");

        builder.HasKey(s => s.Id);

        // DDD — Nested owned Value Object inside a child entity
        // SeatLocation is a Value Object owned by BookedSeat. Like BookingId and
        // BookingStatus on Booking, it gets flattened into the same table row.
        // The columns are named "Row" and "Col" — matching the ubiquitous language
        // used in the domain model.
        //
        // Why configure this in BookedSeatConfiguration, not BookingConfiguration?
        // EF requires that owned-type mappings are defined on the entity that directly
        // owns them. BookedSeat owns SeatLocation; Booking owns BookedSeat.
        // Placing the OwnsOne for SeatLocation anywhere other than here would either
        // fail or produce unexpected column names.
        builder.OwnsOne(s => s.Location, loc =>
        {
            loc.Property(x => x.Row)
               .HasColumnName("Row")
               .IsRequired();

            loc.Property(x => x.Col)
               .HasColumnName("Col")
               .IsRequired();
        });
    }
}
