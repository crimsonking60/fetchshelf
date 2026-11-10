using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class SafetyBuffersTests
{
    private static readonly SafetyBuffers Buffers = new(
        globalUnsatBuffer: 5,
        globalWedgeBuffer: 2,
        [
            new AutograbBuffers("Own both", UnsatBuffer: 20, WedgeBuffer: 0),
            new AutograbBuffers("Own unsat only", UnsatBuffer: 15, WedgeBuffer: null),
        ]);

    [Theory]
    [InlineData("Manual search", 5, 2)]
    [InlineData("Removed or renamed autograb", 5, 2)]
    [InlineData(null, 5, 2)]
    [InlineData("Own both", 20, 0)]
    [InlineData("Own unsat only", 15, 2)]
    public void For_GrabberName_GivesItsOwnBuffersOrTheGlobalOnes(string? grabberName, int unsat, int wedge) =>
        Assert.Equal((unsat, wedge), (Buffers.UnsatBufferFor(grabberName), Buffers.WedgeBufferFor(grabberName)));
}
