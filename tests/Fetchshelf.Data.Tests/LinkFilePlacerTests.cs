using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public class LinkFilePlacerTests
{
    [Fact]
    public void DestinationPath_SourceInDiscNumberedFolder_PlacesInDiscSubfolder()
    {
        var result = LinkFilePlacer.DestinationPath("/library/Author/Title", "Disc 2/track01.mp3");

        Assert.Equal(Path.Combine("/library/Author/Title", "Disc 2", "track01.mp3"), result);
    }

    [Fact]
    public void DestinationPath_SourceInNonDiscFolder_IsFlattened()
    {
        var result = LinkFilePlacer.DestinationPath("/library/Author/Title", "extras/misc/track01.mp3");

        Assert.Equal(Path.Combine("/library/Author/Title", "track01.mp3"), result);
    }

    [Fact]
    public void DestinationPath_SourceAtTopLevel_PlacesDirectlyInDestination()
    {
        var result = LinkFilePlacer.DestinationPath("/library/Author/Title", "track01.mp3");

        Assert.Equal(Path.Combine("/library/Author/Title", "track01.mp3"), result);
    }
}
