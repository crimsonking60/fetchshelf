using System.Text;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class FileNameRulesTests
{
    [Theory]
    [InlineData("AC/DC", "ACDC")]
    [InlineData("a\0b", "ab")]
    [InlineData("  Dune: Part One  ", "Dune: Part One")]
    [InlineData("What? <Really> \"so\" | a*b\\c", "What? <Really> \"so\" | a*b\\c")]
    [InlineData("Trailing dots...", "Trailing dots...")]
    [InlineData("CON", "CON")]
    public void Clean_Posix_RemovesOnlySlashAndNul(string name, string expected) =>
        Assert.Equal(expected, FileNameCleaner.Clean(name, FileNameRules.Posix));

    [Theory]
    [InlineData("Dune: Part One", "Dune - Part One")]
    [InlineData("Dune : Part One", "Dune - Part One")]
    [InlineData("What? <Really> \"so\" | a*b\\c/d", "What Really so  abcd")]
    [InlineData("tab\there\u0001", "tabhere")]
    [InlineData("Trailing dots... ", "Trailing dots")]
    [InlineData("Mr. Smith Goes. . .", "Mr. Smith Goes")]
    public void Clean_Portable_ReplacesWhatWindowsRefuses(string name, string expected) =>
        Assert.Equal(expected, FileNameCleaner.Clean(name, FileNameRules.Portable));

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("con", "con_")]
    [InlineData("NUL.txt", "NUL_.txt")]
    [InlineData("COM1", "COM1_")]
    [InlineData("LPT9.tar.gz", "LPT9_.tar.gz")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("COM10", "COM10")]
    [InlineData("A CON", "A CON")]
    public void Clean_Portable_AvoidsReservedNames(string name, string expected) =>
        Assert.Equal(expected, FileNameCleaner.Clean(name, FileNameRules.Portable));

    [Theory]
    [InlineData("/", FileNameRules.Posix)]
    [InlineData("   ", FileNameRules.Posix)]
    [InlineData("???", FileNameRules.Portable)]
    [InlineData("...", FileNameRules.Portable)]
    public void Clean_NothingLeft_IsAnUnderscore(string name, FileNameRules rules) =>
        Assert.Equal("_", FileNameCleaner.Clean(name, rules));

    [Theory]
    [InlineData(FileNameRules.Posix)]
    [InlineData(FileNameRules.Portable)]
    public void Clean_TooLong_IsCutTo255Bytes(FileNameRules rules)
    {
        var cleaned = FileNameCleaner.Clean(new string('a', 300), rules);

        Assert.Equal(new string('a', FileNameCleaner.MaxComponentBytes), cleaned);
    }

    [Fact]
    public void Clean_TooLongWithMultiByteCharacters_NeverSplitsOne()
    {
        // 'ø' is 2 bytes and '😀' 4: 127 of each pass 255 bytes at a byte no character ends on.
        var cleaned = FileNameCleaner.Clean("a" + string.Concat(Enumerable.Repeat("ø😀", 50)), FileNameRules.Posix);

        var bytes = Encoding.UTF8.GetByteCount(cleaned);
        Assert.InRange(bytes, FileNameCleaner.MaxComponentBytes - 3, FileNameCleaner.MaxComponentBytes);
        Assert.DoesNotContain('�', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(cleaned)));
        Assert.False(char.IsHighSurrogate(cleaned[^1]));
    }

    [Fact]
    public void Clean_CutEndingInASpace_IsTrimmedAgain()
    {
        var cleaned = FileNameCleaner.Clean(new string('a', 254) + " bcd", FileNameRules.Posix);

        Assert.Equal(new string('a', 254), cleaned);
    }

    [Fact]
    public void CutToBytes_FitsAlready_ReturnsItUnchanged() =>
        Assert.Equal("Dune", FileNameCleaner.CutToBytes("Dune", 10));
}
