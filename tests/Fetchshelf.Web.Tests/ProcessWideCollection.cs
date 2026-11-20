namespace Fetchshelf.Web.Tests;

/// <summary>
/// Test classes that change process-wide state other tests rely on, such as <c>Console.Error</c>, which every host logs
/// to. The collection runs alone, after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideCollection
{
    public const string Name = "Process-wide state";
}
