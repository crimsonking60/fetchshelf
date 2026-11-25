using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class MagazineIssueNameTests
{
    /// <summary>An issue in short: magazine | issue | volume.number | year-month, each with its last after a "+" for a double issue.</summary>
    private static string Describe(MagazineIssue issue) => string.Join(" | ",
        issue.Magazine ?? "-",
        issue.Issue is { } i ? $"{i}{(issue.IssueLast is { } il ? $"+{il}" : "")}" : "-",
        issue.Volume is { } v ? $"{v}.{issue.Number}{(issue.NumberLast is { } nl ? $"+{nl}" : "")}" : "-",
        issue.Year is { } y ? $"{y}-{issue.Month?.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) ?? "?"}{(issue.MonthLast is { } ml ? $"+{ml:D2}" : "")}" : "-");

    [Theory]
    [InlineData("Lantern Issue 001", "Lantern | 1 | - | -")]
    [InlineData("Star Rangers Issue34 2020", "Star Rangers | 34 | - | 2020-?")]
    [InlineData("Hollow House Review No 268 December 2022", "Hollow House Review | 268 | - | 2022-12")]
    [InlineData("Hollow House Review No. 268", "Hollow House Review | 268 | - | -")]
    [InlineData("Hollow House Quarterly No 3 September-October 1981", "Hollow House Quarterly | 3 | - | 1981-09+10")]
    [InlineData("Hollow House Review No 118 Fall 2000", "Hollow House Review | 118 | - | 2000-09")]
    [InlineData("Lantern Monthly 1967 Issue 1 (November 09)", "Lantern Monthly | 1 | - | 1967-11")]
    [InlineData("Lantern Monthly 1978 Issue 281-282 (December 28)", "Lantern Monthly | 281+282 | - | 1978-12")]
    [InlineData("Lantern 001 June 2010", "Lantern | 1 | - | 2010-06")]
    [InlineData("Lantern Adventure 1 Fall 1978", "Lantern Adventure | 1 | - | 1978-09")]
    [InlineData("Nightfall Stories v10n04 (Apr 1986)", "Nightfall Stories | - | 10.4 | 1986-04")]
    [InlineData("Nightfall Stories v14n11-12 (Nov 1990)", "Nightfall Stories | - | 14.11+12 | 1990-11")]
    [InlineData("Nightfall Stories v34n04-05 (Apr-May 2010)", "Nightfall Stories | - | 34.4+5 | 2010-04+05")]
    [InlineData("Nightfall Stories v78n01 1990-01", "Nightfall Stories | - | 78.1 | 1990-01")]
    [InlineData("Nightfall Stories v034n01 (1968-01)", "Nightfall Stories | - | 34.1 | 1968-01")]
    [InlineData("Nightfall Stories v034n04 [1968-04]", "Nightfall Stories | - | 34.4 | 1968-04")]
    [InlineData("Nightfall Stories v035 n01 [1968-07]", "Nightfall Stories | - | 35.1 | 1968-07")]
    [InlineData("Nightfall Stories v87n04&05 1994-10&11", "Nightfall Stories | - | 87.4+5 | 1994-10+11")]
    [InlineData("Nightfall Stories 2013 03", "Nightfall Stories | - | - | 2013-03")]
    [InlineData("Nightfall Stories 2013 01-02", "Nightfall Stories | - | - | 2013-01+02")]
    [InlineData("2019 01-02 (Nightfall Stories Book 136)", "Nightfall Stories | - | - | 2019-01+02")]
    [InlineData("1953, 10 (October), NSM", "NSM | - | - | 1953-10")]
    [InlineData("Lantern_Monthly 01 Jan 1950", "Lantern Monthly | - | - | 1950-01")]
    [InlineData("Lantern Mechanics 10 1927", "Lantern Mechanics | - | - | 1927-10")]
    [InlineData("Lantern Mechanics-07-1961", "Lantern Mechanics | - | - | 1961-07")]
    public void IssueNames_AreRead(string name, string expected) =>
        Assert.Equal(expected, Describe(MagazineIssueName.Read(name)!));

    [Theory]
    [InlineData("Lantern 1-40")]
    [InlineData("Nightfall 683")]
    [InlineData("Nightfall 683 (Jan 2009)")] // a comic's issue number with its cover date: not a magazine's numbering
    [InlineData("04 - The Long Winter")]
    [InlineData("Lantern#5-Cover")]
    [InlineData("001_Nightfall - Year Zero")]
    [InlineData("Star Rangers v2 #69")]
    [InlineData("Hollow House Notes.Vol.13.1917")]
    [InlineData("Jones - Lantern Mechanisms. Vol.1 - 1930")]
    [InlineData("Mort - Terry Pratchett")]
    public void OtherNames_AreNotIssues(string name) => Assert.Null(MagazineIssueName.Read(name));

    [Fact]
    public void NameInFront_SaysWhetherTheNameStartsWithTheMagazine()
    {
        Assert.True(MagazineIssueName.Read("Lantern Issue 1")!.NameInFront);
        Assert.False(MagazineIssueName.Read("1953, 10 (October), NSM")!.NameInFront);
        Assert.False(MagazineIssueName.Read("2019 01-02 (Nightfall Stories Book 136)")!.NameInFront);
    }

    [Fact]
    public void SameIssue_WhenEveryNumberingBothHaveAgrees()
    {
        var issue = MagazineIssueName.Read("Nightfall Stories v79n04 1990-10")!;

        Assert.True(issue.SameIssueAs(MagazineIssueName.Read("Nightfall Stories v79n04")!));
        Assert.False(issue.SameIssueAs(MagazineIssueName.Read("Nightfall Stories v79n05 1990-10")!));
    }
}
