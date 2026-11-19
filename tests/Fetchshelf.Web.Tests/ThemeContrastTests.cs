using System.Globalization;
using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>Checks the contrast rules that the header of themes.css states, for every theme, against the values in the file.</summary>
public partial class ThemeContrastTests
{
    public static TheoryData<string> ThemeIds => new() { "dark-warm", "dark-cool", "light-warm", "light-cool", "system" };

    private static readonly string[] Surfaces = ["bg", "surface", "surface-2", "zebra", "row-hover"];

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Text_AndLinks_MeetAaOnEverySurface(string theme)
    {
        var t = Load(theme);
        foreach (var surface in Surfaces)
        {
            AssertRatio(t, "text", surface, 4.5);
            AssertRatio(t, "text-muted", surface, 4.5);
            AssertRatio(t, "link", surface, 4.5);
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Indicators_AreVisibleOnPageAndPanels(string theme)
    {
        var t = Load(theme);
        foreach (var surface in new[] { "bg", "surface", "surface-2" })
        {
            AssertRatio(t, "accent-line", surface, 3.0);
        }

        foreach (var surface in new[] { "bg", "surface", "field" })
        {
            AssertRatio(t, "border-strong", surface, 3.0);
        }

        AssertRatio(t, "focus", "bg", 3.0);
        AssertRatio(t, "focus", "surface", 3.0);
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Fills_HaveReadableText(string theme)
    {
        var t = Load(theme);
        AssertRatio(t, "on-accent", "accent", 4.5);
        AssertRatio(t, "on-danger", "c-danger", 4.5);
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void StatusColors_AreReadableOnTheirOwnBadgeTint(string theme)
    {
        var t = Load(theme);
        foreach (var name in new[] { "c-info", "c-ok", "c-warn", "c-danger", "c-vip" })
        {
            // A badge is the color mixed 14% into --surface, as site.css does.
            var tint = Mix(t[name], t["surface"], 0.14);
            var ratio = Contrast(t[name], tint);
            Assert.True(ratio >= 4.5, $"{theme}: --{name} on its badge tint is {ratio:0.00}:1");
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Rows_AreToldApartFromTheirSurface(string theme)
    {
        var t = Load(theme);
        var zebra = Contrast(t["zebra"], t["surface"]);
        Assert.True(zebra >= 1.06, $"{theme}: zebra against surface is {zebra:0.00}:1");
        var panel = Contrast(t["surface"], t["bg"]);
        Assert.True(panel >= 1.09, $"{theme}: surface against bg is {panel:0.00}:1");
    }

    [Fact]
    public void MatchSystem_InALightSystem_HasLightWarmsValues()
    {
        var system = Load("system");
        var lightWarm = Load("light-warm");

        Assert.Equal(lightWarm.OrderBy(p => p.Key), system.OrderBy(p => p.Key));
    }

    private static void AssertRatio(Dictionary<string, (double R, double G, double B)> t, string fg, string bg, double minimum)
    {
        var ratio = Contrast(t[fg], t[bg]);
        Assert.True(ratio >= minimum, $"--{fg} on --{bg} is {ratio:0.00}:1, needs {minimum}:1");
    }

    private static Dictionary<string, (double R, double G, double B)> Load(string theme)
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Fetchshelf.Web", "wwwroot", "css", "themes.css"));
        var block = Regex.Match(css, $$"""data-theme="{{theme}}"\]\s*\{(?<body>.*?)\}""", RegexOptions.Singleline);
        Assert.True(block.Success, $"No block for {theme}");
        var values = new Dictionary<string, (double, double, double)>();
        foreach (Match m in TokenRegex().Matches(block.Groups["body"].Value))
        {
            var hex = m.Groups["hex"].Value;
            values[m.Groups["name"].Value] = (
                int.Parse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return values;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Fetchshelf.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Fetchshelf.sln not found above the test output");
    }

    private static (double R, double G, double B) Mix((double R, double G, double B) a, (double R, double G, double B) b, double share) =>
        (a.R * share + b.R * (1 - share), a.G * share + b.G * (1 - share), a.B * share + b.B * (1 - share));

    private static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((double R, double G, double B) c)
    {
        static double Channel(double v)
        {
            v /= 255;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    [GeneratedRegex(@"--(?<name>[\w-]+):\s*#(?<hex>[0-9a-fA-F]{6});")]
    private static partial Regex TokenRegex();
}
