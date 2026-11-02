namespace Fetchshelf.Data.Tests;

/// <summary>
/// Tests that set FETCHSHELF_CONF_* process environment variables. ConfigLoader applies those to every
/// config it loads, so these run alone, after the parallel tests, rather than leaking into other classes.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentVariableCollection
{
    public const string Name = "Environment variables";
}
