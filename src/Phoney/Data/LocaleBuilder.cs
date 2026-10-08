using System.Globalization;
using System.Text.Json;
using Phoney.Data;

namespace Phoney;

/// <summary>
/// Describes custom locale data for <see cref="Locales.Register(string, Action{LocaleBuilder})"/> and
/// <see cref="Locales.Extend"/>. Paths use faker.js naming, e.g. <c>person.first_name.generic</c>.
/// </summary>
public sealed class LocaleBuilder
{
    private readonly Dictionary<int, Entry?> _changes = [];
    private List<string>? _fallback;

    internal LocaleBuilder(string code) => Code = code;

    /// <summary>Code of the locale being registered or extended.</summary>
    internal string Code { get; }

    /// <summary>Human readable name set by <see cref="Title"/> or imported metadata.</summary>
    internal string? TitleText { get; private set; }

    /// <summary>Fallback chain; defaults to <c>en → base</c>.</summary>
    internal IReadOnlyList<string> FallbackCodes => _fallback ?? (Code == "en" ? ["base"] : ["en", "base"]);

    /// <summary>Entries set on this builder, by key id.</summary>
    internal IEnumerable<KeyValuePair<int, Entry?>> Changes => _changes;

    /// <summary>
    /// Sets the locales consulted, in order, for data this locale lacks. Each fallback brings its own chain, so
    /// <c>FallbackTo("sv")</c> yields <c>sv → en → base</c>. Defaults to <c>en → base</c>.
    /// </summary>
    public LocaleBuilder FallbackTo(params string[] codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var chain = new List<string>();
        foreach (var code in codes)
        {
            var info = LocaleStore.Info(code);
            foreach (var c in info.Fallback.Prepend(info.Code))
            {
                if (!chain.Contains(c))
                    chain.Add(c);
            }
        }

        if (!chain.Contains("base"))
            chain.Add("base");
        _fallback = chain;
        return this;
    }

    /// <summary>Sets the human readable name of the locale.</summary>
    public LocaleBuilder Title(string title)
    {
        TitleText = title;
        return this;
    }

    /// <summary>Sets a list of values (may contain <c>{{...}}</c> templates).</summary>
    public LocaleBuilder Set(string path, params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _changes[KeyRegistry.GetOrAdd(path)] = values.Length == 0 ? null : new StringsEntry([.. values]);
        return this;
    }

    /// <summary>Sets weighted values; higher weights are picked more often.</summary>
    public LocaleBuilder SetWeighted(string path, params (string Value, int Weight)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _changes[KeyRegistry.GetOrAdd(path)] = values.Length == 0
            ? null
            : new WeightedEntry([.. values.Select(v => v.Value)], [.. values.Select(v => v.Weight)]);
        return this;
    }

    /// <summary>Sets a list of integers.</summary>
    public LocaleBuilder SetInts(string path, params int[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _changes[KeyRegistry.GetOrAdd(path)] = values.Length == 0 ? null : new IntsEntry([.. values]);
        return this;
    }

    /// <summary>Marks data as not applicable to this locale; fallback locales will not be consulted for it.</summary>
    public LocaleBuilder Unavailable(string path)
    {
        _changes[KeyRegistry.GetOrAdd(path)] = UnavailableEntry.Instance;
        return this;
    }

    /// <summary>
    /// Imports a faker.js-shaped locale definition, e.g. <c>{ "person": { "first_name": { "generic": ["Kalle"] } } }</c>.
    /// </summary>
    public LocaleBuilder ImportJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ImportJson(document.RootElement);
    }

    /// <summary>Imports a faker.js-shaped locale definition from <paramref name="utf8Json"/>.</summary>
    public LocaleBuilder ImportJson(Stream utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        return ImportJson(document.RootElement);
    }

    /// <summary>Imports every module of a faker.js locale definition (metadata only supplies the title).</summary>
    private LocaleBuilder ImportJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("A locale definition must be a JSON object of modules.");
        foreach (var module in root.EnumerateObject())
        {
            if (module.Name == "metadata")
            {
                if (module.Value.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    TitleText = title.GetString();
                continue;
            }

            if (module.Value.ValueKind != JsonValueKind.Object)
                throw new FormatException($"Module '{module.Name}' must be a JSON object.");
            foreach (var entry in module.Value.EnumerateObject())
                Import(module.Name + "." + entry.Name, entry.Value);
        }

        return this;
    }

    /// <summary>Flattens one value: objects become dotted paths, <c>null</c> marks data as unavailable.</summary>
    private void Import(string path, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                Unavailable(path);
                break;
            case JsonValueKind.String:
                Set(path, value.GetString()!);
                break;
            case JsonValueKind.Object when value.EnumerateObject().Any(p => !IsIdentifier(p.Name)):
                ImportMap(path, value);
                break;
            case JsonValueKind.Object:
                foreach (var child in value.EnumerateObject())
                    Import(path + "." + child.Name, child.Value);
                break;
            case JsonValueKind.Array:
                ImportArray(path, [.. value.EnumerateArray()]);
                break;
            default:
                throw new FormatException($"Unsupported value at '{path}': {value.ValueKind}.");
        }
    }

    /// <summary>Imports an array as strings, integers, weighted values or records, like the data compiler.</summary>
    private void ImportArray(string path, JsonElement[] items)
    {
        if (items.All(i => i.ValueKind == JsonValueKind.String))
        {
            Set(path, [.. items.Select(i => i.GetString()!)]);
        }
        else if (items.All(i => i.ValueKind == JsonValueKind.Number))
        {
            SetInts(path, [.. items.Select(i => i.GetInt32())]);
        }
        else if (items.All(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("value", out _) && i.TryGetProperty("weight", out _)))
        {
            SetWeighted(path, [.. items.Select(i => (i.GetProperty("value").GetString()!, i.GetProperty("weight").GetInt32()))]);
        }
        else if (items.All(i => i.ValueKind == JsonValueKind.Object))
        {
            var fields = items.SelectMany(i => i.EnumerateObject().Select(p => p.Name)).Distinct().ToArray();
            var values = items
                .SelectMany(i => fields.Select(f => i.TryGetProperty(f, out var v) ? Scalar(v) : null))
                .ToArray();
            _changes[KeyRegistry.GetOrAdd(path)] = items.Length == 0 ? null : new RecordsEntry(fields, values);
        }
        else
        {
            throw new FormatException($"The array at '{path}' mixes value kinds.");
        }
    }

    /// <summary>
    /// Imports a map keyed by values rather than names (like faker.js <c>system.mime_type</c>) as a records
    /// table with a <c>key</c> field — the same rule the data compiler applies to the embedded data.
    /// </summary>
    private void ImportMap(string path, JsonElement map)
    {
        var items = map.EnumerateObject().ToArray();
        var fields = items.SelectMany(i => i.Value.ValueKind == JsonValueKind.Object ? i.Value.EnumerateObject().Select(p => p.Name) : [])
            .Distinct()
            .Prepend("key")
            .ToArray();
        var values = items.SelectMany(item => fields.Select(f =>
                f == "key" ? item.Name
                : item.Value.ValueKind == JsonValueKind.Object && item.Value.TryGetProperty(f, out var v)
                    ? v.ValueKind == JsonValueKind.Array ? string.Join(",", v.EnumerateArray().Select(Scalar)) : Scalar(v)
                    : null))
            .ToArray();
        _changes[KeyRegistry.GetOrAdd(path)] = items.Length == 0 ? null : new RecordsEntry(fields, values);
    }

    /// <summary>Whether an object key is a name (as opposed to data such as a MIME type).</summary>
    private static bool IsIdentifier(string name) => name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>A JSON scalar as text, as stored in record tables.</summary>
    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => bool.TrueString.ToLower(CultureInfo.InvariantCulture),
        JsonValueKind.False => bool.FalseString.ToLower(CultureInfo.InvariantCulture),
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };

    /// <summary>The entries of a newly registered locale, indexed by key id.</summary>
    internal Entry?[] BuildEntries()
    {
        var entries = new Entry?[KeyRegistry.Count];
        foreach (var (key, entry) in _changes)
            entries[key] = entry;
        return entries;
    }
}
