using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// What a page opened in the browser shows for an error status that came without a page of its own, such as a list
/// or torrent that isn't there, instead of a blank page. Status-code pages re-execute the request here, keeping its
/// status. Each one is logged, with what was asked for and where the link came from, as the page alone leaves no trace.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class StatusModel(ILogger<StatusModel> logger) : PageModel
{
    public string Heading { get; private set; } = "Not found";

    public string Explanation { get; private set; } = string.Empty;

    public void OnGet(int code)
    {
        var original = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        var path = original?.OriginalPath;
        if (original is not null)
        {
            logger.LogWarning(
                "{Method} {Path}{Query} answered {StatusCode}; the browser came from {Referer}",
                Request.Method, original.OriginalPath, original.OriginalQueryString, code,
                Request.Headers.Referer.FirstOrDefault() is { Length: > 0 } referer ? referer : "no page");
        }

        var address = path is null ? "this address" : $"this address ({path})";
        (Heading, Explanation) = code switch
        {
            StatusCodes.Status404NotFound => ("Not found", $"Nothing is at {address}. It may have been removed, or the link may be out of date."),
            StatusCodes.Status400BadRequest => ("Bad request", $"Fetchshelf couldn't make sense of the request for {address}. Go back and try again."),
            _ => ($"Error {code}", $"Fetchshelf couldn't answer the request for {address}. The log may say why."),
        };
    }
}
