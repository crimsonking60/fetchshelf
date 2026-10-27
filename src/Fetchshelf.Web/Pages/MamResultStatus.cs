namespace Fetchshelf.Web.Pages;

/// <summary>Where a MaM search result stands in Fetchshelf's own pipeline, most-advanced state wins.</summary>
public enum MamResultStatus
{
    None,
    Selected,
    Downloaded,
    Linked,
}
