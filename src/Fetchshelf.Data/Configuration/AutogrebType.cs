namespace Fetchshelf.Data.Configuration;

/// <summary>
/// An `[[autograb]]` entry's `type`: `bookmarks`, `freeleech`, `new`, `mine`, or
/// `{ uploader = &lt;id&gt; }`. Deserialized by <see cref="AutogrebTypeYamlConverter"/> since YAML
/// allows either a string scalar or a one-key mapping here.
/// </summary>
public sealed class AutogrebType
{
    public static readonly AutogrebType Bookmarks = new("bookmarks", null);

    public static readonly AutogrebType Freeleech = new("freeleech", null);

    public static readonly AutogrebType New = new("new", null);

    public static readonly AutogrebType Mine = new("mine", null);

    public static AutogrebType Uploader(long uploaderId) => new("uploader", uploaderId);

    public string Kind { get; }

    public long? UploaderId { get; }

    private AutogrebType(string kind, long? uploaderId)
    {
        Kind = kind;
        UploaderId = uploaderId;
    }
}
