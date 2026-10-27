using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public sealed class NotionListConfigTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-notion-list-config-tests-").FullName;

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_MinimalEntry_ParsesFields()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[notion_list]]
            data_source = "abc123"
            token = "secret_token"
            name = "Want to read"
            mam_fields = ["MaM"]

            [[notion_list.grab]]
            cost = "free"
            """);

        var entry = Assert.Single(ConfigLoader.Load(ConfigPath).NotionList);

        Assert.Equal("abc123", entry.DataSource);
        Assert.Equal("secret_token", entry.Token);
        Assert.Equal("Want to read", entry.Name);
        Assert.Equal(["MaM"], entry.MamFields);
        Assert.Single(entry.Grab);
    }

    [Theory]
    [InlineData("""
        mam_id = ""

        [[notion_list]]
        token = "secret_token"
        name = "Want to read"
        mam_fields = ["MaM"]

        [[notion_list.grab]]
        cost = "free"
        """, "data_source")]
    [InlineData("""
        mam_id = ""

        [[notion_list]]
        data_source = "abc123"
        name = "Want to read"
        mam_fields = ["MaM"]

        [[notion_list.grab]]
        cost = "free"
        """, "token")]
    [InlineData("""
        mam_id = ""

        [[notion_list]]
        data_source = "abc123"
        token = "secret_token"
        mam_fields = ["MaM"]

        [[notion_list.grab]]
        cost = "free"
        """, "name")]
    public void Load_MissingRequiredField_Throws(string toml, string expectedFieldInMessage)
    {
        File.WriteAllText(ConfigPath, toml);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains(expectedFieldInMessage, exception.Message);
    }

    [Fact]
    public void Load_NoMamFields_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[notion_list]]
            data_source = "abc123"
            token = "secret_token"
            name = "Want to read"

            [[notion_list.grab]]
            cost = "free"
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("mam_fields", exception.Message);
    }

    [Fact]
    public void Load_NoGrabBlocks_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[notion_list]]
            data_source = "abc123"
            token = "secret_token"
            name = "Want to read"
            mam_fields = ["MaM"]
            """);

        var exception = Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));

        Assert.Contains("grab", exception.Message);
    }

    [Fact]
    public void Load_UnknownKeyInEntry_Throws()
    {
        File.WriteAllText(ConfigPath, """
            mam_id = ""

            [[notion_list]]
            data_source = "abc123"
            token = "secret_token"
            name = "Want to read"
            mam_fields = ["MaM"]
            not_a_real_field = 1

            [[notion_list.grab]]
            cost = "free"
            """);

        Assert.Throws<ConfigurationException>(() => ConfigLoader.Load(ConfigPath));
    }
}
