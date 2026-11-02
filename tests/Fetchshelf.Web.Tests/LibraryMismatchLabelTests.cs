using Fetchshelf.Web.Pages.Torrents;

namespace Fetchshelf.Web.Tests;

public class LibraryMismatchLabelTests
{
    [Theory]
    [InlineData("NoLibrary", "No library")]
    [InlineData("NewLibraryDir", "Different library")]
    [InlineData("NewPath", "Different folder")]
    [InlineData("SomethingNew", "SomethingNew")]
    [InlineData(null, null)]
    public void For_StoredState_ReturnsReadableLabel(string? state, string? expected) =>
        Assert.Equal(expected, LibraryMismatchLabel.For(state));
}
