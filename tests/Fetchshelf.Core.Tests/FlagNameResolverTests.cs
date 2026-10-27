using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class FlagNameResolverTests
{
    [Theory]
    [InlineData("crude_language", BookFlags.CrudeLanguage)]
    [InlineData("crude", BookFlags.CrudeLanguage)]
    [InlineData("language", BookFlags.CrudeLanguage)]
    [InlineData("violence", BookFlags.Violence)]
    [InlineData("some_explicit", BookFlags.SomeExplicit)]
    [InlineData("explicit", BookFlags.Explicit)]
    [InlineData("abridged", BookFlags.Abridged)]
    [InlineData("lgbt", BookFlags.Lgbt)]
    public void ResolvesDocumentedAliases(string name, BookFlags expected)
    {
        Assert.True(FlagNameResolver.TryResolve(name, out var flag));
        Assert.Equal(expected, flag);
    }

    [Fact]
    public void UnrecognizedName_ReturnsFalse()
    {
        Assert.False(FlagNameResolver.TryResolve("not-a-flag", out _));
    }
}
