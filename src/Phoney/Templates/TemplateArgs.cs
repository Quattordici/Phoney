using System.Globalization;
using System.Text.Json;

namespace Phoney.Templates;

/// <summary>
/// Arguments of a template call such as <c>{{string.numeric(4)}}</c> or <c>{{number.int({"min":1,"max":9})}}</c>.
/// </summary>
/// <remarks>
/// Parsed like faker.js: the text between the parentheses is first read as a JSON array of parameters;
/// if that fails, the whole text is one unquoted string (<c>{{helpers.fromRegExp([A-Z]{3})}}</c>). JavaScript literals, which are easier to write
/// inside a C# string, are read like their JSON: single-quoted strings and unquoted keys (<c>{{string.alpha({length: 3, casing: 'upper'})}}</c>).
/// </remarks>
public sealed class TemplateArgs
{
    private readonly JsonElement[] _json;

    internal static readonly TemplateArgs Empty = new([]);

    private TemplateArgs(JsonElement[] json) => _json = json;

    /// <summary>Number of arguments.</summary>
    public int Count => _json.Length;

    /// <summary>Parses the text between parentheses (see the class remarks).</summary>
    internal static TemplateArgs Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;
        var arguments = TryParseJson(text) ?? (JavaScriptLiterals.ToJson(text) is { } json ? TryParseJson(json) : null);
        if (arguments is not null)
            return arguments;

        // Neither JSON nor JavaScript literals: faker.js passes the raw text as a single string argument.
        using var document = JsonDocument.Parse("\"" + JsonEncodedText.Encode(text).ToString() + "\"");
        return new TemplateArgs([document.RootElement.Clone()]);
    }

    /// <summary>The arguments when <paramref name="text"/> is a JSON list of values, otherwise <see langword="null"/>.</summary>
    private static TemplateArgs? TryParseJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse("[" + text + "]");
            return new TemplateArgs([.. document.RootElement.EnumerateArray().Select(e => e.Clone())]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Argument <paramref name="index"/> as a string, or <paramref name="fallback"/> when absent.</summary>
    public string? String(int index, string? fallback = null) =>
        index < _json.Length ? (_json[index].ValueKind == JsonValueKind.String ? _json[index].GetString() : _json[index].GetRawText()) : fallback;

    /// <summary>
    /// Argument <paramref name="index"/> as text: the value itself when it is a plain value (<c>{{internet.mac("-")}}</c>), or the <paramref name="option"/> of an options object
    /// (<c>{{internet.mac({"separator":"-"})}}</c>). Numbers and booleans use their JSON text; absent, null, array and object values return <paramref name="fallback"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="option"/> is <see langword="null"/>.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public string? String(int index, string option, string? fallback)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (index >= _json.Length)
            return fallback;
        var value = _json[index];
        if (value.ValueKind == JsonValueKind.Object && !value.TryGetProperty(option, out value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Array or JsonValueKind.Object => fallback,
            _ => value.GetRawText(),
        };
    }

    /// <summary>
    /// Argument <paramref name="index"/>, or its <paramref name="option"/> when it is an options object, as a set of characters: a string (<c>"ab"</c>) or an array of strings (<c>["a","b"]</c>),
    /// joined without separators. Non-string array elements use their JSON text. Missing values and values other than strings or arrays return <see langword="null"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="option"/> is <see langword="null"/>.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public string? Characters(int index, string option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (index >= _json.Length)
            return null;
        var value = _json[index];
        if (value.ValueKind == JsonValueKind.Object && !value.TryGetProperty(option, out value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => string.Concat(value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())),
            _ => null,
        };
    }

    /// <summary>The <paramref name="option"/> of an options object as text; <paramref name="fallback"/> for a plain argument, which belongs to the function's main parameter, and when the option is absent.</summary>
    internal string? OptionString(int index, string option, string? fallback) =>
        index < _json.Length && _json[index].ValueKind == JsonValueKind.Object ? String(index, option, fallback) : fallback;

    /// <summary>Like <see cref="Characters"/> for an option that only an options object can give.</summary>
    internal string? OptionCharacters(int index, string option) =>
        IsOptions(index) ? Characters(index, option) : null;

    /// <summary>Whether argument <paramref name="index"/> is an options object (<c>{"min": 1, "max": 6}</c>) rather than a plain value.</summary>
    public bool IsOptions(int index) => index < _json.Length && _json[index].ValueKind == JsonValueKind.Object;

    /// <summary>
    /// A <c>min</c>/<c>max</c> range, as faker.js reads it: from an options object (<c>{"min": 1, "max": 6}</c>), from one plain value, which is the
    /// maximum (<c>number.int(6)</c>), or from two plain values, the minimum and the maximum.
    /// </summary>
    internal (int? Min, int? Max) IntRange()
    {
        if (IsOptions(0))
            return (Int(0, "min"), Int(0, "max"));
        return Count > 1 ? (Int(0), Int(1)) : (null, Int(0));
    }

    /// <summary>Like <see cref="IntRange"/> for fractional numbers.</summary>
    internal (double? Min, double? Max) DoubleRange()
    {
        if (IsOptions(0))
            return (Double(0, "min"), Double(0, "max"));
        return Count > 1 ? (Double(0), Double(1)) : (null, Double(0));
    }

    /// <summary>
    /// Argument <paramref name="index"/> as an integer; when the argument is an options object, reads
    /// <paramref name="option"/> from it instead (<c>{"length": 4}</c>).
    /// </summary>
    public int? Int(int index, string? option = null)
    {
        // The arguments never change and templates are evaluated over and over, so each number is read from the JSON once.
        var cached = Volatile.Read(ref _ints);
        for (var entry = cached; entry is not null; entry = entry.Next)
        {
            if (entry.Index == index && string.Equals(entry.Option, option, StringComparison.Ordinal))
                return entry.Value;
        }

        var value = ReadInt(index, option);
        if ((cached?.Count ?? 0) < MaxCachedInts)
            Volatile.Write(ref _ints, new CachedInt(index, option, value, cached)); // a racing write may drop an entry, which is then read again
        return value;
    }

    private const int MaxCachedInts = 8;

    private CachedInt? _ints;

    /// <summary>A number <see cref="Int"/> read, in an immutable list so threads evaluating the same template can share it without locks.</summary>
    private sealed class CachedInt(int index, string? option, int? value, CachedInt? next)
    {
        public int Index { get; } = index;

        public string? Option { get; } = option;

        public int? Value { get; } = value;

        public CachedInt? Next { get; } = next;

        public int Count { get; } = (next?.Count ?? 0) + 1;
    }

    private int? ReadInt(int index, string? option)
    {
        if (index >= _json.Length)
            return null;
        var value = _json[index];
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (option is null || !value.TryGetProperty(option, out value))
                return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var i) ? i : (int)Math.Clamp(value.GetDouble(), int.MinValue, int.MaxValue), // a cast alone is platform dependent out of range
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }

    /// <summary>Argument <paramref name="index"/> as a double, or an options property when it is an object.</summary>
    public double? Double(int index, string? option = null)
    {
        if (index >= _json.Length)
            return null;
        var value = _json[index];
        if (value.ValueKind == JsonValueKind.Object && (option is null || !value.TryGetProperty(option, out value)))
            return null;
        return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    }

    /// <summary>Argument <paramref name="index"/> as a boolean, or an options property when it is an object.</summary>
    public bool? Bool(int index, string? option = null)
    {
        if (index >= _json.Length)
            return null;
        var value = _json[index];
        if (value.ValueKind == JsonValueKind.Object && (option is null || !value.TryGetProperty(option, out value)))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>Argument <paramref name="index"/> as an array of strings (<c>["a","b"]</c>).</summary>
    public string[] Strings(int index) => [.. SharedStrings(index)];

    /// <summary>Like <see cref="Strings"/>, read from the JSON once and shared: callers must not change the array.</summary>
    internal string[] SharedStrings(int index)
    {
        if (index >= _json.Length || _json[index].ValueKind != JsonValueKind.Array)
            throw new FormatException($"Template argument {index} must be a JSON array.");
        var strings = Volatile.Read(ref _strings);
        if (strings?[index] is { } shared)
            return shared;
        var values = _json[index].EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText()).ToArray();
        if (strings is null)
        {
            Interlocked.CompareExchange(ref _strings, new string[]?[_json.Length], null);
            strings = _strings!;
        }

        Volatile.Write(ref strings[index], values); // racing threads store equal arrays
        return values;
    }

    private string[]?[]? _strings;
}
