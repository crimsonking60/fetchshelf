using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class FolderNameComputerTests
{
    private static TorrentMetadata Build(
        IReadOnlyList<Series>? series = null,
        Edition? edition = null,
        IReadOnlyList<string>? narrators = null,
        string title = "The Way of Kings",
        IReadOnlyList<string>? authors = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
            Language = Language.English,
            Title = title,
            Edition = edition,
            Authors = authors ?? ["Brandon Sanderson"],
            Narrators = narrators ?? [],
            Series = series ?? [],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UtcNow,
        };

    private static Series MakeSeries(string name, params SeriesEntry[] entries) =>
        new() { Name = name, Entries = entries };

    [Fact]
    public void Compute_SeriesWithEntriesAndNarrator_UsesFullForm()
    {
        var metadata = Build(
            series: [MakeSeries("Stormlight Archive", SeriesEntry.ForNumber(1, "1"))],
            narrators: ["Michael Kramer"]);

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: false, _ => false);

        Assert.Equal(
            Path.Combine("/library", "Brandon Sanderson", "Stormlight Archive", "Stormlight Archive #1 - The Way of Kings {Michael Kramer}"),
            result);
    }

    [Fact]
    public void Compute_NoSeries_UsesAuthorTitleForm()
    {
        var metadata = Build(title: "Project Hail Mary", narrators: ["Ray Porter"]);

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: false, _ => false);

        Assert.Equal(
            Path.Combine("/library", "Brandon Sanderson", "Project Hail Mary {Ray Porter}"),
            result);
    }

    [Fact]
    public void Compute_SeriesWithNoEntries_LastComponentIsJustTitle()
    {
        var metadata = Build(series: [MakeSeries("Mistborn")]);

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: false, _ => false);

        Assert.Equal(Path.Combine("/library", "Brandon Sanderson", "Mistborn", "The Way of Kings"), result);
    }

    [Fact]
    public void Compute_EditionPresent_AppendsEditionToLastComponent()
    {
        var metadata = Build(edition: new Edition("2nd Edition", 2));

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: false, _ => false);

        Assert.Equal(Path.Combine("/library", "Brandon Sanderson", "The Way of Kings, 2nd Edition"), result);
    }

    [Fact]
    public void Compute_ExcludeNarratorAndNarratorlessFolderDoesNotExist_OmitsNarrator()
    {
        var metadata = Build(narrators: ["Michael Kramer"]);

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: true, _ => false);

        Assert.Equal(Path.Combine("/library", "Brandon Sanderson", "The Way of Kings"), result);
    }

    [Fact]
    public void Compute_ExcludeNarratorButNarratorlessFolderExists_FallsBackToNarratorForm()
    {
        var metadata = Build(narrators: ["Michael Kramer"]);
        var narratorlessPath = Path.Combine("/library", "Brandon Sanderson", "The Way of Kings");

        var result = FolderNameComputer.Compute(
            "/library", metadata, excludeNarratorInLibraryDir: true, path => path == narratorlessPath);

        Assert.Equal(Path.Combine("/library", "Brandon Sanderson", "The Way of Kings {Michael Kramer}"), result);
    }

    [Fact]
    public void Compute_NameLongerThanTheFileSystemAllows_ShortensTheTitle()
    {
        // A real title whose folder name came to 257 bytes and failed to link.
        var metadata = Build(
            title: "Slavery: Not Forgiven, Never Forgotten – The Most Powerful Slave Narratives, Historical Documents & Influential "
                + "Novels: The Underground Railroad, Memoirs of Frederick Douglass, 12 Years a Slave, Uncle Tom's Cabin, History of "
                + "Abolitionism, Lynch Law, Civil",
            authors: ["Mark Twain"]);

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: false, _ => false);

        var name = Path.GetFileName(result);
        Assert.Equal(
            "Slavery: Not Forgiven, Never Forgotten – The Most Powerful Slave Narratives, Historical Documents & Influential "
                + "Novels: The Underground Railroad, Memoirs of Frederick Douglass, 12 Years a Slave, Uncle Tom's Cabin, History of "
                + "Abolitionism, Lynch Law…",
            name);
        Assert.Equal(Path.Combine("/library", "Mark Twain"), Path.GetDirectoryName(result));
    }

    [Fact]
    public void Compute_Portable_ReplacesTheColon()
    {
        var metadata = Build(title: "Dune: Part One", narrators: ["Scott Brick"]);

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: false, _ => false, rules: FileNameRules.Portable);

        Assert.Equal(Path.Combine("/library", "Brandon Sanderson", "Dune - Part One {Scott Brick}"), result);
    }

    [Fact]
    public void Compute_Template_UsesItsLayout()
    {
        var metadata = Build(
            series: [MakeSeries("Stormlight Archive", SeriesEntry.ForNumber(1, "1"))],
            authors: ["Brandon Sanderson", "Someone Else"]);
        var layout = FolderLayout.Parse("{authors}/[{series} {series_number} - ]{title} ({language}, {year})");

        var result = FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: false, _ => false, layout);

        Assert.Equal(
            Path.Combine("/library", "Brandon Sanderson, Someone Else", $"Stormlight Archive 1 - The Way of Kings (English, {metadata.UploadTime.Year})"),
            result);
    }

    [Fact]
    public void Compute_TemplateWithExcludeNarrator_LeavesTheNarratorOutUnlessThatFolderExists()
    {
        var metadata = Build(narrators: ["Michael Kramer"]);
        var layout = FolderLayout.Parse("{author}/{title}[ - {narrator}]");
        var narratorless = Path.Combine("/library", "Brandon Sanderson", "The Way of Kings");

        Assert.Equal(narratorless, FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: true, _ => false, layout));
        Assert.Equal(
            Path.Combine("/library", "Brandon Sanderson", "The Way of Kings - Michael Kramer"),
            FolderNameComputer.Compute("/library", metadata, excludeNarratorInLibraryDir: true, path => path == narratorless, layout));
    }
}
