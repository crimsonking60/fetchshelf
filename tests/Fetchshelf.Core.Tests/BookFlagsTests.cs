using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class BookFlagsTests
{
    // Real tracker bit positions. Bit 0 is unused.
    [Theory]
    [InlineData(BookFlags.CrudeLanguage, 1 << 1)]
    [InlineData(BookFlags.Violence, 1 << 2)]
    [InlineData(BookFlags.SomeExplicit, 1 << 3)]
    [InlineData(BookFlags.Explicit, 1 << 4)]
    [InlineData(BookFlags.Abridged, 1 << 5)]
    [InlineData(BookFlags.Lgbt, 1 << 6)]
    public void NumericValue_MatchesRealTrackerBit(BookFlags flag, int expectedBit)
    {
        Assert.Equal(expectedBit, (int)flag);
    }

    [Fact]
    public void RawBitfield_RoundTripsToCorrectFlags()
    {
        // violence (bit 2) + abridged (bit 5), as the tracker would report in `browseflags`.
        const int rawBitfield = (1 << 2) | (1 << 5);

        var flags = (BookFlags)rawBitfield;

        Assert.True(flags.HasFlag(BookFlags.Violence));
        Assert.True(flags.HasFlag(BookFlags.Abridged));
        Assert.False(flags.HasFlag(BookFlags.CrudeLanguage));
        Assert.False(flags.HasFlag(BookFlags.SomeExplicit));
        Assert.False(flags.HasFlag(BookFlags.Explicit));
        Assert.False(flags.HasFlag(BookFlags.Lgbt));
    }
}
