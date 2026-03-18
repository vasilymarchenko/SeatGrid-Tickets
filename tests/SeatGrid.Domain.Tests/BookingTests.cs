using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Events;
using SeatGrid.API.Domain.Exceptions;
using SeatGrid.API.Domain.ValueObjects;

namespace SeatGrid.Domain.Tests;

public class BookingTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static Booking CreateBookingWithSeat()
    {
        var booking = Booking.Create(Guid.NewGuid(), 1, "user-1");
        booking.AddSeat(new SeatLocation(1, 1));
        return booking;
    }

    // ── Booking.Confirm ────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_WhenPending_TransitionsToConfirmed()
    {
        var booking = CreateBookingWithSeat();

        booking.Confirm();

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void Confirm_WhenPending_RaisesBookingConfirmedEvent()
    {
        var booking = CreateBookingWithSeat();

        booking.Confirm();

        var evt = Assert.Single(booking.DomainEvents.OfType<BookingConfirmed>());
        Assert.Equal(booking.OrderId, evt.OrderId);
        Assert.Equal(booking.EventId, evt.EventId);
        Assert.Equal(booking.UserId,  evt.UserId);
        Assert.Single(evt.Seats);
        Assert.Equal(new SeatLocation(1, 1), evt.Seats[0]);
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_ThrowsDomainException()
    {
        var booking = CreateBookingWithSeat();
        booking.Confirm();

        Assert.Throws<DomainException>(() => booking.Confirm());
    }

    [Fact]
    public void Confirm_WhenCancelled_ThrowsDomainException()
    {
        var booking = CreateBookingWithSeat();
        booking.Cancel();

        Assert.Throws<DomainException>(() => booking.Confirm());
    }

    // ── Booking.Cancel ─────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_WhenPending_TransitionsToCancelled()
    {
        var booking = CreateBookingWithSeat();

        booking.Cancel();

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void Cancel_WhenPending_RaisesBookingCancelledEvent()
    {
        var booking = CreateBookingWithSeat();

        booking.Cancel();

        var evt = Assert.Single(booking.DomainEvents.OfType<BookingCancelled>());
        Assert.Equal(booking.OrderId, evt.OrderId);
        Assert.Equal(booking.EventId, evt.EventId);
        Assert.Single(evt.Seats);
        Assert.Equal(new SeatLocation(1, 1), evt.Seats[0]);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ThrowsDomainException()
    {
        var booking = CreateBookingWithSeat();
        booking.Cancel();

        Assert.Throws<DomainException>(() => booking.Cancel());
    }

    // ── Booking.AddSeat ────────────────────────────────────────────────────────

    [Fact]
    public void AddSeat_DuplicateLocation_ThrowsDomainException()
    {
        var booking = Booking.Create(Guid.NewGuid(), 1, "user-1");
        booking.AddSeat(new SeatLocation(3, 5));

        Assert.Throws<DomainException>(() => booking.AddSeat(new SeatLocation(3, 5)));
    }

    [Fact]
    public void AddSeat_MultipleDistinctSeats_AllPresent()
    {
        var booking = Booking.Create(Guid.NewGuid(), 1, "user-1");
        booking.AddSeat(new SeatLocation(1, 1));
        booking.AddSeat(new SeatLocation(1, 2));
        booking.AddSeat(new SeatLocation(2, 1));

        Assert.Equal(3, booking.Seats.Count);
    }

    // ── Booking.Create ─────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithEmptyUserId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Booking.Create(Guid.NewGuid(), 1, ""));
    }

    [Fact]
    public void Create_WithWhitespaceUserId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Booking.Create(Guid.NewGuid(), 1, "   "));
    }

    [Fact]
    public void Create_ValidArgs_StartsInPendingStatus()
    {
        var booking = Booking.Create(Guid.NewGuid(), 1, "user-1");

        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public void Create_ValidArgs_NoDomainEventsRaised()
    {
        var booking = Booking.Create(Guid.NewGuid(), 1, "user-1");

        Assert.Empty(booking.DomainEvents);
    }
}
