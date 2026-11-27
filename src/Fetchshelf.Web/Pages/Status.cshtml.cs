using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// What a page opened in the browser shows for an error status that came without a page of its own, such as a list
/// or torrent that isn't there, instead of a blank page. Status-code pages re-execute the request here, keeping its
/// status.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class StatusModel : PageModel
{
    public string Heading { get; private set; } = "Not found";

    public string Explanation { get; private set; } = string.Empty;

    public void OnGet(int code)
    {
        var path = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;
        var address = path is null ? "this address" : $"this address ({path})";
        (Heading, Explanation) = code switch
        {
            StatusCodes.Status404NotFound => ("Not found", $"Nothing is at {address}. It may have been removed, or the link may be out of date."),
            StatusCodes.Status400BadRequest => ("Bad request", $"Fetchshelf couldn't make sense of the request for {address}. Go back and try again."),
            _ => ($"Error {code}", $"Fetchshelf couldn't answer the request for {address}. The log may say why."),
        };
    }
}
