namespace Fetchshelf.Data.Configuration;

/// <summary>A `[[library]]`'s `layout`: how books are placed under its `library_dir`.</summary>
public enum LibraryLayout
{
    /// <summary>One folder per book, as `folder_template` says: what Audiobookshelf reads.</summary>
    Book,

    /// <summary>One folder per series holding every book of it, one file each: what comic servers such as Komga read.</summary>
    Series,
}

/// <summary>A `[[library]]`'s `language_in_folder`: when a book's folder (or, in a series library, its file) names its language.</summary>
public enum LanguageInFolder
{
    /// <summary>Only when the plain folder already holds the same title in another language, so the two don't merge.</summary>
    OnClash,

    /// <summary>For every book not in the first of the top-level `languages`.</summary>
    Other,
}
