using System.Text;

namespace Fetchshelf.Core.Organizer;

/// <summary>
/// A parsed <c>folder_template</c>: the folders a book goes into under <c>library_dir</c>, one part per folder. Tokens in
/// braces (<c>{title}</c>) are filled in from the book's metadata; text in square brackets is an optional group, left out
/// unless every token in it has a value (<c>[, {edition}]</c>). <c>{{</c>, <c>}}</c>, <c>[[</c> and <c>]]</c> are literal
/// braces and brackets. A folder whose tokens are all empty is left out, the way a book without a series has no series
/// folder.
/// </summary>
public sealed class FolderLayout
{
    /// <summary>The tokens a template may use.</summary>
    public static readonly IReadOnlyList<string> Tokens =
        ["author", "authors", "series", "series_number", "title", "edition", "narrator", "language", "year"];

    /// <summary>The layout used when a library sets no <c>folder_template</c>: <c>Author/Series/Series #n - Title, Edition {Narrator}</c>.</summary>
    public const string DefaultTemplate = "{author}/{series}/[{series} #{series_number} - ]{title}[, {edition}][ {{{narrator}}}]";

    public static readonly FolderLayout Default = Parse(DefaultTemplate);

    private readonly IReadOnlyList<Part> _parts;

    private FolderLayout(string template, IReadOnlyList<Part> parts)
    {
        Template = template;
        _parts = parts;
    }

    /// <summary>The template as written.</summary>
    public string Template { get; }

    /// <summary>
    /// The folder names for a book, each cleaned by <paramref name="rules"/> and at most
    /// <see cref="FileNameCleaner.MaxComponentBytes"/> long. A folder too long with its title gets the title shortened,
    /// ending in <c>…</c>, so the series number, edition and narrator that tell copies apart stay; one too long without
    /// it, or too long with no title left, is cut at the end.
    /// </summary>
    public IReadOnlyList<string> Render(IReadOnlyDictionary<string, string> values, FileNameRules rules)
    {
        string Value(string token) => values.TryGetValue(token, out var value) ? value : string.Empty;

        var names = new List<string>();
        foreach (var part in _parts)
        {
            var tokens = part.Segments.SelectMany(s => s.Tokens).ToList();
            if (tokens.Count > 0 && tokens.All(t => Value(t).Length == 0))
            {
                continue;
            }

            names.Add(RenderPart(part, Value, rules, FileNameCleaner.MaxComponentBytes));
        }

        return names;
    }

    /// <summary>
    /// One name from <paramref name="template"/>, a template without <c>/</c> that needn't hold <c>{title}</c>, cleaned and
    /// shortened as a folder's: for names that aren't a book's folder, such as the series layout's series folders and
    /// file names. <paramref name="reserveBytes"/> are kept free of the limit, for an extension added after it.
    /// </summary>
    /// <exception cref="FormatException">The template can't be read, or holds a <c>/</c>.</exception>
    public static string RenderName(string template, IReadOnlyDictionary<string, string> values, FileNameRules rules, int reserveBytes = 0)
    {
        var layout = ParseCore(template, requireTitle: false);
        if (layout._parts is not [var part])
        {
            throw new FormatException("a name is one folder or file, so it can't hold a '/'");
        }

        return RenderPart(part, token => values.TryGetValue(token, out var value) ? value : string.Empty, rules, FileNameCleaner.MaxComponentBytes - reserveBytes);
    }

    private static string RenderPart(Part part, Func<string, string> value, FileNameRules rules, int maxBytes)
    {
        var full = part.Render(value);
        var over = OverLimit(full, rules, maxBytes);
        var title = value("title");
        if (over <= 0 || title.Length == 0 || !part.Segments.Any(s => s.Tokens.Contains("title")))
        {
            return CleanWithin(full, rules, maxBytes);
        }

        // The longest start of the title that fits, found by halving: cleaning can lengthen it (a colon becomes " -"),
        // so the bytes it passes the limit by don't say how much to cut.
        string? best = null;
        var (shortest, longest) = (1, Encoding.UTF8.GetByteCount(title) - 1);
        while (shortest <= longest)
        {
            var keep = (shortest + longest) / 2;
            var candidate = WithTitle(part, value, FileNameCleaner.CutToBytes(title, keep).TrimEnd().TrimEnd(CutPunctuation) + "…");
            if (OverLimit(candidate, rules, maxBytes) <= 0)
            {
                best = candidate;
                shortest = keep + 1;
            }
            else
            {
                longest = keep - 1;
            }
        }

        return CleanWithin(best ?? full, rules, maxBytes);
    }

    /// <summary>The name cleaned, and cut at the end if it is still longer than <paramref name="maxBytes"/>.</summary>
    private static string CleanWithin(string name, FileNameRules rules, int maxBytes)
    {
        var cleaned = FileNameCleaner.Clean(name, rules);
        return Encoding.UTF8.GetByteCount(cleaned) <= maxBytes ? cleaned : FileNameCleaner.CutToBytes(cleaned, maxBytes).TrimEnd();
    }

    /// <summary>Punctuation left dangling where a title is cut, dropped before the <c>…</c>.</summary>
    private static readonly char[] CutPunctuation = [' ', ',', ';', ':', '-', '–', '—'];

    private static string WithTitle(Part part, Func<string, string> value, string title) =>
        part.Render(token => token == "title" ? title : value(token));

    /// <summary>How many bytes <paramref name="name"/> passes <paramref name="maxBytes"/> by once its characters are cleaned.</summary>
    private static int OverLimit(string name, FileNameRules rules, int maxBytes) =>
        Encoding.UTF8.GetByteCount(FileNameCleaner.CleanCharacters(name, rules)) - maxBytes;

    /// <summary>Parses a <c>folder_template</c>; a <see cref="FormatException"/> says what is wrong with it.</summary>
    public static FolderLayout Parse(string template) => ParseCore(template, requireTitle: true);

    private static FolderLayout ParseCore(string template, bool requireTitle)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new FormatException("it is empty");
        }

        if (template.StartsWith('/'))
        {
            throw new FormatException("it starts with '/'; it is a path under library_dir, so leave the '/' out");
        }

        var parts = new List<Part>();
        var segments = new List<Segment>();
        Group? group = null;
        var literal = new StringBuilder();

        void EndLiteral()
        {
            if (literal.Length > 0)
            {
                (group?.Segments ?? segments).Add(new Literal(literal.ToString()));
                literal.Clear();
            }
        }

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            var next = i + 1 < template.Length ? template[i + 1] : '\0';
            switch (c)
            {
                case '{' when next == '{':
                case '}' when next == '}':
                case '[' when next == '[':
                case ']' when next == ']':
                    literal.Append(c);
                    i++;
                    break;
                case '{':
                    var close = template.IndexOf('}', i + 1);
                    if (close < 0)
                    {
                        throw new FormatException($"the '{{' at position {i + 1} has no closing '}}'; write '{{{{' for a literal brace");
                    }

                    var name = template[(i + 1)..close];
                    if (!Tokens.Contains(name))
                    {
                        throw new FormatException($"'{{{name}}}' is not a token; the tokens are {string.Join(", ", Tokens.Select(t => $"{{{t}}}"))}");
                    }

                    EndLiteral();
                    (group?.Segments ?? segments).Add(new Token(name));
                    i = close;
                    break;
                case '}':
                    throw new FormatException($"the '}}' at position {i + 1} closes no '{{'; write '}}}}' for a literal brace");
                case '[':
                    if (group is not null)
                    {
                        throw new FormatException($"the '[' at position {i + 1} is inside another group; groups can't be nested");
                    }

                    EndLiteral();
                    group = new Group([]);
                    break;
                case ']':
                    if (group is null)
                    {
                        throw new FormatException($"the ']' at position {i + 1} closes no '['; write ']]' for a literal bracket");
                    }

                    EndLiteral();
                    if (!group.Tokens.Any())
                    {
                        throw new FormatException($"the group ending at position {i + 1} has no token, so it would always be left out");
                    }

                    segments.Add(group);
                    group = null;
                    break;
                case '/':
                    if (group is not null)
                    {
                        throw new FormatException($"the '/' at position {i + 1} is inside a group; a group can't span folders");
                    }

                    EndLiteral();
                    if (segments.Count == 0)
                    {
                        throw new FormatException($"the '/' at position {i + 1} makes an empty folder");
                    }

                    parts.Add(new Part(segments));
                    segments = [];
                    break;
                default:
                    literal.Append(c);
                    break;
            }
        }

        if (group is not null)
        {
            throw new FormatException("a '[' has no closing ']'; write '[[' for a literal bracket");
        }

        EndLiteral();
        if (segments.Count == 0)
        {
            throw new FormatException("it ends with '/', which makes an empty folder");
        }

        parts.Add(new Part(segments));
        if (requireTitle && !parts.Any(p => p.Segments.Any(s => s.Tokens.Contains("title"))))
        {
            throw new FormatException("it has no {title}, so books by the same author would share a folder");
        }

        return new FolderLayout(template, parts);
    }

    private abstract record Segment
    {
        public abstract IEnumerable<string> Tokens { get; }

        public abstract string Render(Func<string, string> value);
    }

    private sealed record Literal(string Text) : Segment
    {
        public override IEnumerable<string> Tokens => [];

        public override string Render(Func<string, string> value) => Text;
    }

    private sealed record Token(string Name) : Segment
    {
        public override IEnumerable<string> Tokens => [Name];

        public override string Render(Func<string, string> value) => value(Name);
    }

    /// <summary>An optional group: its text only when every token in it has a value.</summary>
    private sealed record Group(List<Segment> Segments) : Segment
    {
        public override IEnumerable<string> Tokens => Segments.SelectMany(s => s.Tokens);

        public override string Render(Func<string, string> value) =>
            Tokens.All(t => value(t).Length > 0) ? string.Concat(Segments.Select(s => s.Render(value))) : string.Empty;
    }

    private sealed record Part(IReadOnlyList<Segment> Segments)
    {
        public string Render(Func<string, string> value) => string.Concat(Segments.Select(s => s.Render(value)));
    }
}
