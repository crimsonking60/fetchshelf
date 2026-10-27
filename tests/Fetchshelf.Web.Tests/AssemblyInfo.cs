using Xunit;

// Tests in this assembly set the FETCHSHELF_CONFIG_FILE process environment variable around
// WebApplicationFactory startup; serialize execution so they cannot race each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
