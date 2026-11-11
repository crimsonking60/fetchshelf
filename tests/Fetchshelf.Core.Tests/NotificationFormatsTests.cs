using System.Text.Json.Nodes;
using Fetchshelf.Core.Notifications;

namespace Fetchshelf.Core.Tests;

public class NotificationFormatsTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string Url = "https://notify.example/topic";
    private const string WebUrl = "https://fetchshelf.example.net/";

    private static Notification Grabbed(string? linkPath = "/torrents/abc", string title = "Grabbed The Way of Kings") => new(
        NotificationEvent.Grabbed,
        title,
        "Grabbed The Way of Kings by Brandon Sanderson (Autograb Freeleech, freeleech).",
        At,
        new Dictionary<string, object?> { ["mam_id"] = 123, ["grabber"] = "Autograb Freeleech" },
        linkPath);

    private static Notification Of(NotificationEvent notificationEvent) =>
        new(notificationEvent, "A title", "A message", At, null, "/Errors");

    private static void AssertJson(string expected, string actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"Expected {expected}\nbut was {actual}");

    private static string Header(NotificationRequest request, string name) =>
        request.Headers.Single(h => h.Key == name).Value;

    [Fact]
    public void Json_Grabbed_HasTheEventTheMessageTheLinkTheTimeAndItsFields()
    {
        var request = NotificationFormats.Build(NotificationFormat.Json, Grabbed(), Url, token: null, WebUrl);

        Assert.Equal(Url, request.Url);
        Assert.Equal("application/json", request.ContentType);
        Assert.Empty(request.Headers);
        AssertJson(
            """
            {"event":"grabbed","title":"Grabbed The Way of Kings",
             "message":"Grabbed The Way of Kings by Brandon Sanderson (Autograb Freeleech, freeleech).",
             "url":"https://fetchshelf.example.net/torrents/abc","time":"2026-11-11T12:00:00Z",
             "fields":{"mam_id":123,"grabber":"Autograb Freeleech"}}
            """,
            request.Body);
    }

    [Fact]
    public void Json_WithoutAWebUrl_HasNoLinkAndWithoutFields_AnEmptyObject()
    {
        var request = NotificationFormats.Build(NotificationFormat.Json, Of(NotificationEvent.JobFailing), Url, null, webUrl: null);

        AssertJson(
            """{"event":"job_failing","title":"A title","message":"A message","time":"2026-11-11T12:00:00Z","fields":{}}""",
            request.Body);
    }

    [Fact]
    public void Json_NonLatinText_IsNotEscaped() =>
        Assert.Contains("Война и мир", NotificationFormats.Build(NotificationFormat.Json, Grabbed(title: "Война и мир"), Url, null, null).Body);

    [Fact]
    public void Token_IsSentAsABearerHeaderInEveryFormat()
    {
        foreach (var format in Enum.GetValues<NotificationFormat>())
        {
            var request = NotificationFormats.Build(format, Grabbed(), Url, "tk_secret", null);

            Assert.Equal(new KeyValuePair<string, string>("Authorization", "Bearer tk_secret"), request.Headers[0]);
        }
    }

    [Fact]
    public void Ntfy_Grabbed_HasTheMessageAsTheBodyAndTitleTagsAndClickHeaders()
    {
        var request = NotificationFormats.Build(NotificationFormat.Ntfy, Grabbed(), Url, null, WebUrl);

        Assert.Equal("Grabbed The Way of Kings by Brandon Sanderson (Autograb Freeleech, freeleech).", request.Body);
        Assert.Equal("text/plain; charset=utf-8", request.ContentType);
        Assert.Equal(
            [("Title", "Grabbed The Way of Kings"), ("Tags", "books"), ("Click", "https://fetchshelf.example.net/torrents/abc")],
            request.Headers.Select(h => (h.Key, h.Value)));
    }

    [Theory]
    [InlineData(NotificationEvent.MamRefused, "warning", true)]
    [InlineData(NotificationEvent.JobFailing, "warning", true)]
    [InlineData(NotificationEvent.RateLimited, "warning", false)]
    [InlineData(NotificationEvent.JobErrors, "warning", false)]
    [InlineData(NotificationEvent.HitAndRun, "warning", false)]
    [InlineData(NotificationEvent.Grabbed, "books", false)]
    [InlineData(NotificationEvent.Linked, "books", false)]
    [InlineData(NotificationEvent.JobRecovered, "white_check_mark", false)]
    [InlineData(NotificationEvent.BudgetSpent, "information_source", false)]
    public void Ntfy_TagsAndPriority_FollowTheEvent(NotificationEvent notificationEvent, string tag, bool high)
    {
        var request = NotificationFormats.Build(NotificationFormat.Ntfy, Of(notificationEvent), Url, null, null);

        Assert.Equal(tag, Header(request, "Tags"));
        Assert.Equal(high, request.Headers.Any(h => h is { Key: "Priority", Value: "high" }));
        Assert.DoesNotContain(request.Headers, h => h.Key == "Click");
    }

    [Fact]
    public void Ntfy_NonAsciiTitle_IsAnRfc2047EncodedWordHeadersCanCarry()
    {
        var request = NotificationFormats.Build(NotificationFormat.Ntfy, Grabbed(title: "Grabbed Война и мир"), Url, null, null);

        var title = Header(request, "Title");
        Assert.StartsWith("=?UTF-8?B?", title, StringComparison.Ordinal);
        Assert.Equal("Grabbed Война и мир", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(title["=?UTF-8?B?".Length..^2])));
    }

    [Fact]
    public void Gotify_Grabbed_HasTitleMessagePriorityAndTheClickLink()
    {
        var request = NotificationFormats.Build(NotificationFormat.Gotify, Grabbed(), Url, null, WebUrl);

        AssertJson(
            """
            {"title":"Grabbed The Way of Kings",
             "message":"Grabbed The Way of Kings by Brandon Sanderson (Autograb Freeleech, freeleech).",
             "priority":4,
             "extras":{"client::notification":{"click":{"url":"https://fetchshelf.example.net/torrents/abc"}}}}
            """,
            request.Body);
    }

    [Fact]
    public void Gotify_ProblemWithoutALink_HasPriority8AndNoExtras() =>
        AssertJson(
            """{"title":"A title","message":"A message","priority":8}""",
            NotificationFormats.Build(NotificationFormat.Gotify, Of(NotificationEvent.JobFailing), Url, null, null).Body);

    [Theory]
    [InlineData(NotificationEvent.MamRefused, 0xE74C3C)]
    [InlineData(NotificationEvent.JobErrors, 0xE74C3C)]
    [InlineData(NotificationEvent.JobRecovered, 0x2ECC71)]
    [InlineData(NotificationEvent.Grabbed, 0x3498DB)]
    public void Discord_EmbedColour_IsRedForProblemsGreenForRecoveryBlueOtherwise(NotificationEvent notificationEvent, int color)
    {
        var request = NotificationFormats.Build(NotificationFormat.Discord, Of(notificationEvent), Url, null, WebUrl);

        AssertJson(
            $$"""{"content":null,"embeds":[{"title":"A title","description":"A message","url":"https://fetchshelf.example.net/Errors","color":{{color}}}]}""",
            request.Body);
    }

    [Fact]
    public void Discord_LongText_IsCutToWhatDiscordAccepts()
    {
        var notification = new Notification(NotificationEvent.Grabbed, new string('t', 400), new string('m', 5_000), At);

        var embed = JsonNode.Parse(NotificationFormats.Build(NotificationFormat.Discord, notification, Url, null, null).Body)!["embeds"]![0]!;

        Assert.Equal(256, embed["title"]!.GetValue<string>().Length);
        Assert.Equal(4_000, embed["description"]!.GetValue<string>().Length);
    }

    [Theory]
    [InlineData(NotificationEvent.MamRefused, "failure")]
    [InlineData(NotificationEvent.JobFailing, "failure")]
    [InlineData(NotificationEvent.JobErrors, "warning")]
    [InlineData(NotificationEvent.JobRecovered, "success")]
    [InlineData(NotificationEvent.Linked, "info")]
    public void Apprise_Type_FollowsTheEventAndTheLinkEndsTheBody(NotificationEvent notificationEvent, string type)
    {
        var request = NotificationFormats.Build(NotificationFormat.Apprise, Of(notificationEvent), Url, null, WebUrl);

        AssertJson(
            $$"""{"title":"A title","body":"A message\nhttps://fetchshelf.example.net/Errors","type":"{{type}}"}""",
            request.Body);
    }

    [Fact]
    public void Link_JoinsTheWebUrlAndThePathWhateverTheSlashes()
    {
        Assert.Equal("https://x.net/torrents/abc", NotificationFormats.Link(Grabbed("/torrents/abc"), "https://x.net/"));
        Assert.Equal("https://x.net/torrents/abc", NotificationFormats.Link(Grabbed("torrents/abc"), "https://x.net"));
        Assert.Null(NotificationFormats.Link(Grabbed(), null));
        Assert.Null(NotificationFormats.Link(Grabbed(linkPath: null), "https://x.net"));
    }

    [Theory]
    [InlineData("json", NotificationFormat.Json)]
    [InlineData("ntfy", NotificationFormat.Ntfy)]
    [InlineData("gotify", NotificationFormat.Gotify)]
    [InlineData("discord", NotificationFormat.Discord)]
    [InlineData("apprise", NotificationFormat.Apprise)]
    public void FormatNames_RoundTrip(string name, NotificationFormat format)
    {
        Assert.True(NotificationFormatNames.TryParse(name, out var parsed));
        Assert.Equal((format, name), (parsed, NotificationFormatNames.Name(format)));
    }

    [Fact]
    public void FormatNames_AnUnknownNameIsNotAFormat() => Assert.False(NotificationFormatNames.TryParse("slack", out _));
}
