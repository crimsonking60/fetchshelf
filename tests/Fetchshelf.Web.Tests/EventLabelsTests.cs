using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Pages.Events;

namespace Fetchshelf.Web.Tests;

public class EventLabelsTests
{
    [Theory]
    [InlineData(EventKind.Grabbed, "Grabbed")]
    [InlineData(EventKind.RemovedFromMam, "Removed from MAM")]
    public void For_EventKind_ReturnsReadableLabel(EventKind kind, string expected) =>
        Assert.Equal(expected, EventLabels.For(kind));

    [Fact]
    public void FieldChanges_StoredChanges_ListsEachAsFieldFromTo() =>
        Assert.Equal(
            "Title: Old → New; Category: AudiobookGeneralFiction → AudiobookFantasy",
            EventLabels.FieldChanges(
                """[{"Field":"Title","From":"Old","To":"New"},{"Field":"LegacyCategory","From":"AudiobookGeneralFiction","To":"AudiobookFantasy"}]"""));

    [Fact]
    public void RemovedFiles_StoredList_JoinsTheFileNames() =>
        Assert.Equal("a.m4b, cover.jpg", EventLabels.RemovedFiles("""["a.m4b","cover.jpg"]"""));

    [Fact]
    public void RemovedFiles_EmptyList_SaysNone() =>
        Assert.Equal("none", EventLabels.RemovedFiles("[]"));

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"unexpected\":true}")]
    public void FieldChanges_UnexpectedText_ShowsItUnchanged(string stored) =>
        Assert.Equal(stored, EventLabels.FieldChanges(stored));

    [Fact]
    public void Details_Linked_SaysByWhichLibraryAndWhere() =>
        Assert.Equal(
            "Linked by audiobooks to /library/Book",
            EventLabels.Details(new EventEntity { OccurredAt = DateTimeOffset.UtcNow, Kind = EventKind.Linked, LinkerName = "audiobooks", LibraryPath = "/library/Book" }));

    [Fact]
    public void Details_Grabbed_SaysByWhichJobAndTheCost() =>
        Assert.Equal(
            "Grabbed by Freeleech (cost: Free)",
            EventLabels.Details(new EventEntity { OccurredAt = DateTimeOffset.UtcNow, Kind = EventKind.Grabbed, GrabberName = "Freeleech", Cost = "Free" }));

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void Shortened_200OrFewerCharacters_IsNull(int length) =>
        Assert.Null(EventLabels.Shortened(new string('x', length)));

    [Fact]
    public void Shortened_MoreThan200Characters_IsTheFirst180() =>
        Assert.Equal(new string('x', 180), EventLabels.Shortened(new string('x', 201)));

    [Fact]
    public void Shortened_ACharacterOfTwoUnitsAtTheCut_IsLeftWhole()
    {
        // "𝔄" is two UTF-16 units; at 179-180 it would be cut in half.
        var details = new string('x', 179) + "𝔄" + new string('x', 40);

        Assert.Equal(new string('x', 179), EventLabels.Shortened(details));
    }
}
