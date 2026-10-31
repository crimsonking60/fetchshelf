namespace Fetchshelf.Data.Configuration;

/// <summary>One `[[tag]]` entry: the shared filter plus optional category/tags.</summary>
public sealed class TagRuleEntry : SharedFilterConfig
{
    public string? Category { get; set; }

    public List<string> Tags { get; set; } = [];
}
