using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public sealed class BookIdentifiersTests
{
    [Theory]
    [InlineData("9780547928227", new[] { "9780547928227", "054792822X" })]
    [InlineData("978-0-547-92822-7", new[] { "9780547928227", "054792822X" })]
    [InlineData(" 978 0547928227 ", new[] { "9780547928227", "054792822X" })]
    [InlineData("054792822X", new[] { "054792822X", "9780547928227" })]
    [InlineData("054792822x", new[] { "054792822X", "9780547928227" })]
    [InlineData("0-306-40615-2", new[] { "0306406152", "9780306406157" })]
    [InlineData("9791034304783", new[] { "9791034304783" })]
    [InlineData("B0FRHR1QD3", new[] { "B0FRHR1QD3" })]
    [InlineData(" b0frk1r2c7", new[] { "B0FRK1R2C7" })]
    [InlineData("ASIN:B0072JE06M", new[] { "B0072JE06M" })]
    [InlineData("ASIN1538722941", new[] { "1538722941", "9781538722947" })]
    public void Keys_ReadsTheWaysUploadsAndEditionsWriteThem(string value, string[] expected)
    {
        Assert.Equal(expected, BookIdentifiers.Keys(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9780547928228")] // wrong check digit
    [InlineData("0547928221")] // wrong check digit
    [InlineData("978-075066725")] // twelve digits
    [InlineData("525511830")] // nine digits
    [InlineData("6065")]
    [InlineData("1234567890123")] // not 978 or 979
    [InlineData("A0FRHR1QD3")] // not an ASIN
    public void Keys_ValueThatIsNoValidIsbnOrAsin_GivesNone(string? value)
    {
        Assert.Empty(BookIdentifiers.Keys(value));
    }

    [Fact]
    public void From_AnIsbn10AndTheSameBooksIsbn13_ShareTheirKeys()
    {
        var fromHardcover = BookIdentifiers.From(["9780306406157", null, "B0FRHR1QD3"]);
        var fromMam = BookIdentifiers.From(["0-306-40615-2"]);

        Assert.True(fromHardcover.Overlaps(fromMam));
        Assert.Equal(["0306406152", "9780306406157", "B0FRHR1QD3"], fromHardcover.Order(StringComparer.Ordinal));
    }
}
