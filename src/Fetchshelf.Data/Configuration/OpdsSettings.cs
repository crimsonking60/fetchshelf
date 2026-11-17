namespace Fetchshelf.Data.Configuration;

/// <summary>The optional `[opds]` section: serves the ebook libraries as an OPDS catalog at `/opds`.</summary>
public sealed class OpdsSettings
{
    /// <summary>The `[[library]]` names to serve; empty serves every library. Checked against the libraries by `ConfigLoader`.</summary>
    public List<string> Libraries { get; set; } = [];

    /// <summary>With <see cref="Password"/>, the login OPDS readers must give (HTTP Basic). Unset serves the catalog to anyone.</summary>
    public string? Username { get; set; }

    /// <summary>The login's password: a secret. Read from <see cref="PasswordFile"/> when that is set.</summary>
    public string? Password { get; set; }

    /// <summary>A file holding <see cref="Password"/>, like <c>mam_id_file</c>.</summary>
    public string? PasswordFile { get; set; }

    /// <summary>Whether OPDS readers must log in.</summary>
    public bool RequiresLogin => !string.IsNullOrEmpty(Username);
}
