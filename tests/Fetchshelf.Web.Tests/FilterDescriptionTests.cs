using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Pages.Config;

namespace Fetchshelf.Web.Tests;

public sealed class FilterDescriptionTests
{
    [Fact]
    public void Describe_NothingSet_ShowsOnlyTheDefaultCategories() =>
        Assert.Equal(["Categories: audio (all), ebook (all)"], FilterDescription.Describe(new TagRuleEntry()));

    [Fact]
    public void Describe_EveryPartSet_ShowsEachOnALineOfItsOwn()
    {
        var rule = new TagRuleEntry
        {
            MediaType = ["ComicBook", "Manga"],
            Categories = new CategoriesConfig
            {
                Audio = CategoryListOrBool.None,
                Ebook = CategoryListOrBool.Of(["Comics/Graphic Novels"]),
                Radio = CategoryListOrBool.All,
            },
            Languages = ["English", "German"],
            Flags = new() { ["explicit"] = false, ["abridged"] = true },
            MinSize = "10 MiB",
            MaxSize = "2 GiB",
            UploadedAfter = "2024-01-01",
            MinSeeders = 5,
            MaxSnatched = 100,
            MaxAuthors = 3,
            ExcludeUploader = ["someone"],
        };

        Assert.Equal(
            [
                "Categories: ebook (Comics/Graphic Novels), radio (all)",
                "Media types: ComicBook, Manga",
                "Languages: English, German",
                "Flags: abridged required, explicit forbidden",
                "Size: at least 10 MiB, at most 2 GiB",
                "Uploaded: after 2024-01-01",
                "Seeders: at least 5",
                "Snatched: at most 100",
                "Authors: at most 3",
                "Not uploaded by: someone",
            ],
            FilterDescription.Describe(rule));
    }

    [Fact]
    public void Describe_AnEmptyLanguageList_SaysAnyLanguage() =>
        Assert.Contains("Languages: any", FilterDescription.Describe(new TagRuleEntry { Languages = [] }));

    [Fact]
    public void Describe_NoMainTypeAllowed_SaysItMatchesNothing() =>
        Assert.Equal(
            "Categories: none, so it matches nothing",
            FilterDescription.Describe(new TagRuleEntry { Categories = new CategoriesConfig { Audio = CategoryListOrBool.None, Ebook = CategoryListOrBool.None } })[0]);
}
