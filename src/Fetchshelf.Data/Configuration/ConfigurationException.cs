namespace Fetchshelf.Data.Configuration;

/// <summary>Thrown when the configuration file cannot be loaded, e.g. an unrecognized key (Fetchshelf-spec.md §4).</summary>
public sealed class ConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
