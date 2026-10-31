namespace Fetchshelf.Data.Configuration;

/// <summary>Thrown when the configuration file cannot be loaded, e.g. an unrecognized key.</summary>
public sealed class ConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
