namespace Fetchshelf.Data.Configuration;

/// <summary>
/// One main type's `categories` config value: `true` = all categories of that
/// type (<see cref="Names"/> null), `false` = none (<see cref="Names"/> empty), or an explicit list
/// of category names. Deserialized by <see cref="CategoryListOrBoolYamlConverter"/> since YAML
/// allows either a boolean scalar or a sequence here.
/// </summary>
public sealed class CategoryListOrBool
{
    public static readonly CategoryListOrBool All = new(null);

    public static readonly CategoryListOrBool None = new([]);

    public IReadOnlyList<string>? Names { get; }

    private CategoryListOrBool(IReadOnlyList<string>? names) => Names = names;

    public static CategoryListOrBool Of(IReadOnlyList<string> names) => new(names);
}
