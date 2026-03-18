using SeatGrid.API.Domain.Exceptions;
using SeatGrid.API.Domain.ValueObjects;

namespace SeatGrid.Domain.Tests;

public class SeatLocationTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void Constructor_RowOrColLessThanOne_ThrowsDomainException(int row, int col)
    {
        Assert.Throws<DomainException>(() => new SeatLocation(row, col));
    }

    [Fact]
    public void Constructor_ValidRowAndCol_CreatesInstance()
    {
        var location = new SeatLocation(3, 7);

        Assert.Equal(3, location.Row);
        Assert.Equal(7, location.Col);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new SeatLocation(2, 4);
        var b = new SeatLocation(2, 4);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new SeatLocation(2, 4);
        var b = new SeatLocation(2, 5);

        Assert.NotEqual(a, b);
    }
}
