namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What happened to one candidate processed by <see cref="SelectionRunner"/>, Fetchshelf-spec.md §7.2.</summary>
public enum SelectionOutcome
{
    Ignored,
    RefreshedSelected,
    RefreshedLibrary,
    MetadataOnlyCreated,
    MetadataOnlyStop,
    NoPreferredFormat,
    DuplicateOfSelected,
    DuplicateOfLibrary,
    CostNotFree,
    Selected,
}
