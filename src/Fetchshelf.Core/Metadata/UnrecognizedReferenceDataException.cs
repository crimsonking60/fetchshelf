namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Thrown when an incoming MAM record's media type, category, language, size, or date cannot be
/// recognized. Such records must be rejected with a descriptive error rather
/// than silently dropped.
/// </summary>
public sealed class UnrecognizedReferenceDataException(string fieldName, object? value)
    : Exception($"Unrecognized {fieldName}: '{value}' is not a known reference value.")
{
    public string FieldName { get; } = fieldName;

    public object? Value { get; } = value;
}
