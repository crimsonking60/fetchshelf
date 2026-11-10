using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class PersonNameKeyTests
{
    [Theory]
    [InlineData("J. R. R. Tolkien", "J.R.R. Tolkien")]
    [InlineData("J.R.R. Tolkien", "j.r.r. tolkien")]
    [InlineData("Ursula K. Le Guin", "Ursula K. LeGuin")]
    [InlineData("Gabriel García Márquez", "Gabriel Garcia Marquez")]
    [InlineData("Jo Nesbø", "Jo Nesbo")]
    [InlineData("Jean-Paul Sartre", "Jean Paul Sartre")]
    public void Of_SpellingsThatDifferOnlyInPunctuationSpacingCaseOrAccents_Agree(string a, string b)
    {
        Assert.Equal(PersonNameKey.Of(a), PersonNameKey.Of(b));
    }

    [Theory]
    [InlineData("Brandon Sanderson", "Brandon Sandersen")]
    [InlineData("Лев Толстой", "Алексей Толстой")]
    public void Of_DifferentNames_Differ(string a, string b)
    {
        Assert.NotEqual(PersonNameKey.Of(a), PersonNameKey.Of(b));
    }

    [Fact]
    public void Of_NameInAnotherScript_KeepsItsLetters()
    {
        Assert.Equal("левтолстой", PersonNameKey.Of("Лев Толстой"));
    }
}
