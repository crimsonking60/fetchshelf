using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class DescriptionTextTests
{
    [Fact]
    public void From_DropsTagsScriptsAndExtraBlankLines()
    {
        var text = DescriptionText.From("<p>One</p><style>p{}</style><p>Two &lt;3</p><br><br><br><br>Three");

        Assert.Equal("One\nTwo <3\n\nThree", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("<p> </p><br>")]
    public void From_NothingLeft_IsNull(string? html) => Assert.Null(DescriptionText.From(html));
}
