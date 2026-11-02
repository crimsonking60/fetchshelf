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
}
