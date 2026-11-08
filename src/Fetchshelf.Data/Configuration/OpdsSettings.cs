namespace Fetchshelf.Data.Configuration;

/// <summary>The optional `[opds]` section: serves the ebook libraries as an OPDS catalog at `/opds`.</summary>
public sealed class OpdsSettings
{
    /// <summary>The `[[library]]` names to serve; empty serves every library. Checked against the libraries by `ConfigLoader`.</summary>
    public List<string> Libraries { get; set; } = [];
}
