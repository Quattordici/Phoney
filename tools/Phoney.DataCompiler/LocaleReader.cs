using System.Text.Json;

namespace Phoney.DataCompiler;

/// <summary>Reads the raw faker.js export (<c>data/raw</c>) and flattens every locale into leaf entries.</summary>
internal static class LocaleReader
{
    /// <summary>Reads the manifest and every locale file of the raw export.</summary>
    public static (string FakerVersion, IReadOnlyList<string> Methods, IReadOnlyList<RawLocale> Locales) Read(string rawDirectory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(rawDirectory, "_manifest.json")));
        var root = manifest.RootElement;
        var fakerVersion = root.GetProperty("fakerVersion").GetString()!;
        var methods = root.GetProperty("methods").EnumerateArray().Select(m => m.GetString()!).ToList();

        var locales = new List<RawLocale>();
        foreach (var locale in root.GetProperty("locales").EnumerateArray())
        {
            var code = locale.GetProperty("code").GetString()!;
            var fallback = locale.GetProperty("fallback").EnumerateArray().Select(f => f.GetString()!).ToList();
            var metadata = locale.GetProperty("metadata").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.ToString(), StringComparer.Ordinal);

            using var definition = JsonDocument.Parse(File.ReadAllText(Path.Combine(rawDirectory, code + ".json")));
            var leaves = new Dictionary<string, LeafEntry>(StringComparer.Ordinal);
            foreach (var module in definition.RootElement.EnumerateObject())
            {
                if (module.Name == "metadata")
                    continue;
                foreach (var entry in module.Value.EnumerateObject())
                {
                    var group = module.Name + "." + entry.Name;
                    Flatten(group, group, entry.Value, leaves, code);
                }
            }

            locales.Add(new RawLocale(code, fallback, metadata, leaves));
        }

        return (fakerVersion, methods, locales);
    }

    /// <summary>Flattens a value into leaves: objects become dotted paths, <c>null</c> marks unavailable data.</summary>
    private static void Flatten(string group, string path, JsonElement value, Dictionary<string, LeafEntry> leaves, string code)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                leaves.Add(path, new LeafEntry(path, group, EntryKind.Unavailable));
                break;
            case JsonValueKind.Object when value.EnumerateObject().Any(p => !IsIdentifier(p.Name)):
                leaves.Add(path, MapToRecords(group, path, value, code));
                break;
            case JsonValueKind.Object:
                foreach (var child in value.EnumerateObject())
                    Flatten(group, path + "." + child.Name, child.Value, leaves, code);
                break;
            case JsonValueKind.String:
                leaves.Add(path, new LeafEntry(path, group, EntryKind.Strings) { Strings = [value.GetString()!] });
                break;
            case JsonValueKind.Array:
                leaves.Add(path, FlattenArray(group, path, value, code));
                break;
            default:
                throw new InvalidDataException($"{code}: unsupported value kind {value.ValueKind} at '{path}'.");
        }
    }

    /// <summary>Classifies an array as strings, integers, weighted values or records.</summary>
    private static LeafEntry FlattenArray(string group, string path, JsonElement array, string code)
    {
        var items = array.EnumerateArray().ToList();
        if (items.Count == 0 || items.All(i => i.ValueKind == JsonValueKind.String))
            return new LeafEntry(path, group, EntryKind.Strings) { Strings = items.Select(i => i.GetString()!).ToList() };

        if (items.All(i => i.ValueKind == JsonValueKind.Number))
            return new LeafEntry(path, group, EntryKind.Ints) { Ints = items.Select(i => i.GetInt32()).ToList() };

        if (items.All(i => i.ValueKind == JsonValueKind.Object))
        {
            if (items.All(IsWeighted))
            {
                return new LeafEntry(path, group, EntryKind.Weighted)
                {
                    Strings = items.Select(i => i.GetProperty("value").GetString()!).ToList(),
                    Weights = items.Select(i => i.GetProperty("weight").GetInt32()).ToList(),
                };
            }

            var fields = new List<string>();
            foreach (var item in items)
                foreach (var property in item.EnumerateObject())
                    if (!fields.Contains(property.Name))
                        fields.Add(property.Name);

            var rows = items
                .Select(item => (IReadOnlyList<string?>)fields
                    .Select(f => item.TryGetProperty(f, out var v) ? Scalar(v, $"{code}: {path}.{f}") : null)
                    .ToList())
                .ToList();
            return new LeafEntry(path, group, EntryKind.Records) { Fields = fields, Rows = rows };
        }

        throw new InvalidDataException($"{code}: array at '{path}' mixes value kinds.");
    }

    /// <summary>
    /// Converts a data map whose keys are values rather than names (e.g. <c>system.mime_type</c>, keyed by
    /// <c>application/epub+zip</c>) into a records table with a <c>key</c> field, because such keys cannot be
    /// part of a dotted path. Nested arrays become comma-separated values.
    /// </summary>
    private static LeafEntry MapToRecords(string group, string path, JsonElement map, string code)
    {
        var fields = new List<string> { "key" };
        foreach (var item in map.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{code}: map '{path}' must contain objects, got {item.Value.ValueKind} at '{item.Name}'.");
            foreach (var property in item.Value.EnumerateObject())
                if (!fields.Contains(property.Name))
                    fields.Add(property.Name);
        }

        var rows = map.EnumerateObject()
            .Select(item => (IReadOnlyList<string?>)fields
                .Select(f => f == "key"
                    ? item.Name
                    : item.Value.TryGetProperty(f, out var v)
                        ? v.ValueKind == JsonValueKind.Array
                            ? string.Join(",", v.EnumerateArray().Select(e => Scalar(e, $"{code}: {path}.{item.Name}.{f}")))
                            : Scalar(v, $"{code}: {path}.{item.Name}.{f}")
                        : null)
                .ToList())
            .ToList();
        return new LeafEntry(path, group, EntryKind.Records) { Fields = fields, Rows = rows };
    }

    /// <summary>Whether an object key is a name (as opposed to data such as a MIME type).</summary>
    private static bool IsIdentifier(string name) => name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Whether an object is a <c>{ value, weight }</c> pair.</summary>
    private static bool IsWeighted(JsonElement item)
    {
        var names = item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
        return names is ["value", "weight"];
    }

    /// <summary>A JSON scalar as text; fails for nested values.</summary>
    private static string Scalar(JsonElement value, string where) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw new InvalidDataException($"{where}: record fields must be scalars, got {value.ValueKind}."),
    };
}
