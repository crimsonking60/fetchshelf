namespace Fetchshelf.Data.Configuration;

/// <summary>
/// An `[[autograb]]` entry's `type`: `bookmarks`, `freeleech`, `new`, `mine`, or
/// `{ uploader = &lt;id&gt; }`. Deserialized by <see cref="AutograbTypeYamlConverter"/> since YAML
/// allows either a string scalar or a one-key mapping here.
/// </summary>
public sealed class AutograbType
{
    public static readonly AutograbType Bookmarks = new("bookmarks", null);

    public static readonly AutograbType Freeleech = new("freeleech", null);

    public static readonly AutograbType New = new("new", null);

    public static readonly AutograbType Mine = new("mine", null);

    public static AutograbType Uploader(long uploaderId) => new("uploader", uploaderId);

    public string Kind { get; }

    public long? UploaderId { get; }

    private AutograbType(string kind, long? uploaderId)
    {
        Kind = kind;
        UploaderId = uploaderId;
    }
}
