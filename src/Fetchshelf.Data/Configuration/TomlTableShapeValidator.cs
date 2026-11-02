using System.Collections;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// Checks the parsed TOML document, before it is turned into YAML, for a section written as a single
/// table (<c>[name]</c>) where the settings expect a repeatable one (<c>[[name]]</c>), or the other way
/// round. YamlDotNet reports either mistake only as a type error about the converted YAML text.
/// </summary>
internal static class TomlTableShapeValidator
{
    public static void Validate(Dictionary<object, object?> document, Type settingsType, Func<Type, bool> hasTypeConverter) =>
        ValidateTable(document, settingsType, prefix: null, hasTypeConverter);

    private static void ValidateTable(Dictionary<object, object?> table, Type type, string? prefix, Func<Type, bool> hasTypeConverter)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(SettingName, StringComparer.Ordinal);

        foreach (var (key, value) in table)
        {
            if (key is not string name || !properties.TryGetValue(name, out var property))
            {
                continue; // unknown keys are left to the deserializer, which rejects them
            }

            var path = prefix is null ? name : $"{prefix}.{name}";
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (RepeatedSectionType(propertyType, hasTypeConverter) is { } entryType)
            {
                if (value is Dictionary<object, object?>)
                {
                    throw new ConfigurationException(
                        $"Invalid configuration: '{path}' is a repeatable section; write [[{path}]], not [{path}].");
                }

                if (value is List<object?> entries)
                {
                    foreach (var entry in entries.OfType<Dictionary<object, object?>>())
                    {
                        ValidateTable(entry, entryType, path, hasTypeConverter);
                    }
                }
            }
            else if (IsSection(propertyType, hasTypeConverter))
            {
                if (value is List<object?>)
                {
                    throw new ConfigurationException(
                        $"Invalid configuration: there can be only one '{path}' section; write [{path}], not [[{path}]].");
                }

                if (value is Dictionary<object, object?> nested)
                {
                    ValidateTable(nested, propertyType, path, hasTypeConverter);
                }
            }
        }
    }

    private static string SettingName(PropertyInfo property) =>
        property.GetCustomAttribute<YamlMemberAttribute>()?.Alias ?? UnderscoredNamingConvention.Instance.Apply(property.Name);

    private static bool IsSection(Type type, Func<Type, bool> hasTypeConverter) =>
        type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type) && !hasTypeConverter(type);

    private static Type? RepeatedSectionType(Type type, Func<Type, bool> hasTypeConverter) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            && type.GetGenericArguments()[0] is var entryType && IsSection(entryType, hasTypeConverter)
            ? entryType
            : null;
}
