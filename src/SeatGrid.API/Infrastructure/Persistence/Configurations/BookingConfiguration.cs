using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.ValueObjects;

namespace SeatGrid.API.Infrastructure.Persistence.Configurations;

// DDD — EF Core configuration as a separate class (IEntityTypeConfiguration<T>)
// Keeping mapping rules out of OnModelCreating avoids the DbContext becoming a
// dumping ground for infrastructure concerns. Each aggregate root gets its own
// configuration class. This follows the Single Responsibility Principle and
// mirrors the DDD separation: domain model in Domain/, persistence mapping in
// Infrastructure/.
public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");

        // DDD — Mapping a strongly-typed ID used as the primary key
        // When a Value Object IS the primary key, OwnsOne is the wrong tool.
        // OwnsOne creates an owned navigation — EF cannot use that as a PK column.
        // Instead, a value converter is used: it teaches EF how to translate
        // between BookingId (domain type) and Guid (database type) when reading
        // and writing the column. The domain always works with BookingId; EF
        // handles the Guid conversion transparently.
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
               .HasColumnName("Id")
               .HasConversion(
                   id    => id.Value,           // domain → database (Guid)
                   value => new BookingId(value) // database → domain (BookingId)
               );

        builder.Property(b => b.EventId).IsRequired();
        builder.Property(b => b.OrderId).IsRequired();
        builder.HasIndex(b => b.OrderId).IsUnique();
        builder.Property(b => b.UserId).IsRequired().HasMaxLength(256);

        // DDD — Mapping a closed-set enumeration Value Object with OwnsOne
        // BookingStatus is not the primary key, so OwnsOne is correct here.
        // OwnsOne flattens it to a single "Status" VARCHAR column on the same row.
        // EF reconstructs the BookingStatus object from the string on every read,
        // so the domain always sees a strongly-typed BookingStatus — never a raw string.
        //
        // Gotcha: using HasOne instead of OwnsOne would make EF create a separate
        // FK table — wrong for a value object with no independent identity.
        builder.OwnsOne(b => b.Status, s =>
        {
            s.Property(x => x.Value)
             .HasColumnName("Status")
             .IsRequired()
             .HasMaxLength(50);
        });

        // DDD — Mapping a private backing field (aggregate-internal collection)
        // The _seats field is private — the public Seats property is read-only
        // (IReadOnlyList<BookedSeat>). Without UsePropertyAccessMode(Field), EF
        // would try to use the public property, which has no setter and would
        // fail to populate the collection when loading from the database.
        //
        // EF Core already discovers the _seats backing field by convention (it
        // follows the _propertyName naming pattern). We configure the navigation
        // via the property name "Seats" and set Field access mode so EF bypasses
        // the read-only property and writes directly to the backing field.
        //
        // HasForeignKey("BookingId") creates a shadow FK property: a column that
        // exists in the database but not as a declared C# property on BookedSeat.
        // The domain entity stays clean; the FK is purely a persistence concern.
        builder.HasMany<BookedSeat>("Seats")
               .WithOne()
               .HasForeignKey("BookingId")
               .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation("Seats")
               .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
