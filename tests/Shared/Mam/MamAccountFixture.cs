// Linked into Fetchshelf.Clients.Mam.Tests and Fetchshelf.Web.Tests.
namespace Fetchshelf.Tests.Shared;

/// <summary>
/// An answer from MAM's `jsonLoad.php?snatch_summary=true`, made from a real one (2026-11-11) with the personal values
/// replaced. It holds every field and snatch-summary entry that answer had, in its order.
/// </summary>
internal static class MamAccountFixture
{
    public const string Json = """
        {
            "classname": "Power User",
            "country_code": "xx",
            "country_name": "Nowhere",
            "downloaded": "436.0 GiB",
            "downloaded_bytes": 468151435264,
            "ratio": 2.84,
            "seedbonus": 41250,
            "snatch_summary": {
                "connectable": "yes",
                "created": 1500000000,
                "created_at": "2017-07-14 02:40:00",
                "duplicates": {"count": 0, "name": "Duplicate peer entries"},
                "inactHnr": {"count": 0, "name": "Not Seeding - H&R - Not Yet Satisfied", "size": null},
                "inactSat": {"count": 12, "name": "Not Seeding - Satisfied", "size": 13958643712},
                "inactUnsat": {"count": 0, "name": "Not Seeding - Not Yet Satisfied", "size": null},
                "ipv6_mac": false,
                "ite": {"count": 0, "latest": 0, "name": "Important Tracker Errors"},
                "leeching": {"count": 2, "name": "Leeching Torrents", "size": 1073741824},
                "partial": false,
                "recently_deleted": 0,
                "reseed": {"count": 0, "inactive": 0, "name": "Reseed requests"},
                "sSat": {"count": 230, "name": "Seeding - Satisfied", "size": 912680550400},
                "seedHnr": {"count": 0, "name": "Seeding - H&R - Not Yet Satisfied", "size": null},
                "seedUnsat": {"count": 14, "name": "Seeding - Not Yet Satisfied", "size": 16106127360},
                "unsat": {"count": 14, "limit": 20, "name": "Unsatisfied", "size": 16106127360},
                "upAct": {"count": 3, "name": "Seeding -  Uploads", "size": 3221225472},
                "upInact": {"count": 0, "name": "Not Seeding - Uploads", "size": null},
                "v6_connectable": false
            },
            "uid": 123456,
            "uploaded": "1.21 TiB",
            "uploaded_bytes": 1330424250000,
            "username": "example-user",
            "wedges": 37
        }
        """;

    /// <summary>The fixture with some snatch-summary entries given other counts, such as <c>("inactHnr", 1)</c>.</summary>
    public static string WithCounts(params (string Key, int Count)[] counts)
    {
        var json = Json;
        foreach (var (key, count) in counts)
        {
            var start = json.IndexOf($"\"{key}\": {{\"count\": ", StringComparison.Ordinal);
            var valueStart = json.IndexOf("\"count\": ", start, StringComparison.Ordinal) + "\"count\": ".Length;
            var valueEnd = json.IndexOf(',', valueStart);
            json = json[..valueStart] + count + json[valueEnd..];
        }

        return json;
    }
}
