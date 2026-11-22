using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

/// <summary>MAM's genre names, and how a name from the configuration or a page finds its genre.</summary>
public class GenreLabelsTests
{
    [Fact]
    public void EveryGenre_HasALabel_AndEveryLabelItsOwnKey()
    {
        Assert.Equal(Enum.GetValues<GenreCategory>().Order(), GenreLabels.All);
        Assert.All(GenreLabels.All, genre => Assert.DoesNotContain("Unknown", GenreLabels.Label(genre)));
        Assert.Equal(GenreLabels.All.Count, GenreLabels.All.Select(genre => GenreLabels.KeyOf(GenreLabels.Label(genre))).Distinct().Count());
    }

    [Theory]
    [InlineData("Action/Adventure", GenreCategory.Action)]
    [InlineData("action / adventure", GenreCategory.Action)]
    [InlineData("actionadventure", GenreCategory.Action)]
    [InlineData("LGBTQIA+", GenreCategory.Lgbt)]
    [InlineData("lgbtqia", GenreCategory.Lgbt)]
    [InlineData("Politics/Sociology", GenreCategory.Politics)]
    [InlineData("Sheet Collection", GenreCategory.SheetCollection)]
    [InlineData("Sheet Collection MP3", GenreCategory.SheetCollectionMp3)]
    [InlineData("Occult / Metaphysical Practices", GenreCategory.Occult)]
    [InlineData("Self-Help", GenreCategory.SelfHelp)]
    public void TryParse_MamsLabel_InAnyCaseSpacingOrPunctuation(string name, GenreCategory expected)
    {
        Assert.True(GenreLabels.TryParse(name, out var genre));
        Assert.Equal(expected, genre);
    }

    [Theory]
    [InlineData("Politics")]
    [InlineData("Lgbt")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameThatIsNotMamsLabel_IsNoGenre(string? name) => Assert.False(GenreLabels.TryParse(name, out _));

    [Fact]
    public void Label_AnIdFetchshelfDoesNotKnow_SaysSo() =>
        Assert.Equal("Unknown Category (id: 70)", GenreLabels.Label((GenreCategory)70));

    [Theory]
    [InlineData("fiction", FictionCategory.Fiction)]
    [InlineData("Fiction", FictionCategory.Fiction)]
    [InlineData("nonfiction", FictionCategory.Nonfiction)]
    [InlineData("Non-fiction", FictionCategory.Nonfiction)]
    public void TryParseFiction_TheTwoMainCategories(string name, FictionCategory expected)
    {
        Assert.True(GenreLabels.TryParseFiction(name, out var fiction));
        Assert.Equal(expected, fiction);
    }

    [Fact]
    public void TryParseFiction_AnythingElse_IsNeither() => Assert.False(GenreLabels.TryParseFiction("poetry", out _));
}
