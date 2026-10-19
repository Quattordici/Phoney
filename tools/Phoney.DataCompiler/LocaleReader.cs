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

        var read = new List<(string Code, List<string> Fallback, Dictionary<string, string> Metadata, Dictionary<string, LeafEntry> Leaves)>();
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

            read.Add((code, fallback, metadata, leaves));
        }

        var byCode = read.ToDictionary(l => l.Code, l => l.Leaves, StringComparer.Ordinal);
        var locales = read.Select(l =>
        {
            // The templates that can use this locale's values: its own and those of its fallback chain.
            var joined = JoinedEntries(l.Fallback.Prepend(l.Code).Where(byCode.ContainsKey).SelectMany(code => byCode[code].Values));
            return new RawLocale(l.Code, l.Fallback, l.Metadata, l.Leaves.ToDictionary(e => e.Key, e => TrimEnds(e.Value, joined), StringComparer.Ordinal));
        }).ToList();
        return (fakerVersion, methods, locales);
    }

    /// <summary>
    /// The entry groups that some template joins straight to the placeholder before it (<c>{{person.lastName}}{{location.city_suffix}}</c>, or <c>{{location.buildingNumber}}</c> by its
    /// method name): their values start with the space or comma that separates them (<c>" aan de Rijn"</c>, <c>", 12"</c>).
    /// </summary>
    private static HashSet<string> JoinedEntries(IEnumerable<LeafEntry> leaves)
    {
        var joined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in leaves.SelectMany(l => l.Strings.Concat(l.Rows.SelectMany(r => r)).OfType<string>()))
        {
            for (var at = text.IndexOf("}}{{", StringComparison.Ordinal); at >= 0; at = text.IndexOf("}}{{", at + 4, StringComparison.Ordinal))
            {
                var start = at + 4;
                var end = start;
                while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '_' or '.'))
                    end++;
                var parts = text[start..end].Split('.');
                if (parts.Length >= 2)
                    joined.Add(parts[0] + "." + SnakeCase(parts[1]));
            }
        }

        return joined;
    }

    /// <summary>A faker.js method name as the name of its data (<c>buildingNumber</c> → <c>building_number</c>).</summary>
    private static string SnakeCase(string name) => string.Concat(name.Select((c, i) => char.IsAsciiLetterUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));

    /// <summary>
    /// Removes the spaces faker.js has at the ends of a few values by mistake (<c>" بابل"</c>, <c>"রংপুর "</c>, and hy street suffixes, which their pattern already puts a space before).
    /// Values of <paramref name="joined"/> entries keep a leading space, which separates them from the word they are joined to.
    /// </summary>
    private static LeafEntry TrimEnds(LeafEntry leaf, HashSet<string> joined)
    {
        var keepStart = joined.Contains(leaf.Group);
        string Trim(string value) => keepStart ? value.TrimEnd() : value.Trim();
        return leaf with
        {
            Strings = [.. leaf.Strings.Select(Trim)],
            Rows = [.. leaf.Rows.Select(row => (IReadOnlyList<string?>)[.. row.Select(v => v is null ? null : Trim(v))])],
        };
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
                leaves.Add(path, new LeafEntry(path, group, EntryKind.Strings) { Strings = [Clean(value.GetString()!)] });
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
            return new LeafEntry(path, group, EntryKind.Strings) { Strings = items.Select(i => Clean(i.GetString()!)).ToList() };

        if (items.All(i => i.ValueKind == JsonValueKind.Number))
            return new LeafEntry(path, group, EntryKind.Ints) { Ints = items.Select(i => i.GetInt32()).ToList() };

        if (items.All(i => i.ValueKind == JsonValueKind.Object))
        {
            if (items.All(IsWeighted))
            {
                return new LeafEntry(path, group, EntryKind.Weighted)
                {
                    Strings = items.Select(i => Clean(i.GetProperty("value").GetString()!)).ToList(),
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

    /// <summary>
    /// Collapses runs of spaces into one: faker.js has a few by mistake (<c>"{{word.noun}} {{person.bio_supporter}}  {{internet.emoji}}"</c>, <c>"Búdkova  cesta"</c>) and
    /// nothing uses them on purpose. Single leading spaces stay: suffixes such as <c>" do Norte"</c> are joined to the word before them.
    /// </summary>
    private static string Clean(string value)
    {
        if (!value.Contains("  ", StringComparison.Ordinal))
            return value;
        var text = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c != ' ' || text.Length == 0 || text[^1] != ' ')
                text.Append(c);
        }

        return text.ToString();
    }

    /// <summary>Whether an object is a <c>{ value, weight }</c> pair.</summary>
    private static bool IsWeighted(JsonElement item)
    {
        var names = item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
        return names is ["value", "weight"];
    }

    /// <summary>A JSON scalar as text; fails for nested values.</summary>
    private static string Scalar(JsonElement value, string where) => value.ValueKind switch
    {
        JsonValueKind.String => Clean(value.GetString()!),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw new InvalidDataException($"{where}: record fields must be scalars, got {value.ValueKind}."),
    };
}
