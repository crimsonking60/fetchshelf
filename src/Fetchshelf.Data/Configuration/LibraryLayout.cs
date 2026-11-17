namespace Fetchshelf.Data.Configuration;

/// <summary>A `[[library]]`'s `layout`: how books are placed under its `library_dir`.</summary>
public enum LibraryLayout
{
    /// <summary>One folder per book, as `folder_template` says: what Audiobookshelf reads.</summary>
    Book,

    /// <summary>One folder per series holding every book of it, one file each: what comic servers such as Komga read.</summary>
    Series,
}
