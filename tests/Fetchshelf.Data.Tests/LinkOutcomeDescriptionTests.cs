using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public class LinkOutcomeDescriptionTests
{
    [Theory]
    [InlineData(LinkOutcome.Hardlinked, "Hardlinked")]
    [InlineData(LinkOutcome.Copied, "Copied")]
    [InlineData(LinkOutcome.Symlinked, "Symlinked")]
    public void Describe_AllFilesPlacedTheSameWay_NamesThatWay(LinkOutcome outcome, string expectedAction) =>
        Assert.Equal((expectedAction, string.Empty), LinkOutcomeDescription.Describe([outcome, outcome]));

    [Fact]
    public void Describe_AllFilesAlreadyThere_SaysSo() =>
        Assert.Equal(
            ("Placed", " (all files were already there)"),
            LinkOutcomeDescription.Describe([LinkOutcome.AlreadyExists, LinkOutcome.AlreadyExists]));

    [Fact]
    public void Describe_FilesPlacedDifferentWays_CountsEachWay() =>
        Assert.Equal(
            ("Placed", " (2 hardlinked, 1 copied, 1 already there)"),
            LinkOutcomeDescription.Describe(
                [LinkOutcome.Copied, LinkOutcome.Hardlinked, LinkOutcome.AlreadyExists, LinkOutcome.Hardlinked]));
}
