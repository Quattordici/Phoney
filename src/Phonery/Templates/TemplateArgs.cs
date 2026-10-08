using System.Globalization;
using System.Text.Json;

namespace Phonery.Templates;

/// <summary>
/// Arguments of a template call such as <c>{{string.numeric(4)}}</c> or <c>{{number.int({"min":1,"max":9})}}</c>.
/// </summary>
/// <remarks>
/// Parsed like faker.js: the text between the parentheses is first read as a JSON array of parameters;
/// if that fails, the whole text is one unquoted string (<c>{{helpers.fromRegExp([A-Z]{3})}}</c>).
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
        try
        {
            using var document = JsonDocument.Parse("[" + text + "]");
            return new TemplateArgs([.. document.RootElement.EnumerateArray().Select(e => e.Clone())]);
        }
        catch (JsonException)
        {
            // Not JSON: faker.js passes the raw text as a single string argument.
            using var document = JsonDocument.Parse("\"" + JsonEncodedText.Encode(text).ToString() + "\"");
            return new TemplateArgs([document.RootElement.Clone()]);
        }
    }

    /// <summary>Argument <paramref name="index"/> as a string, or <paramref name="fallback"/> when absent.</summary>
    public string? String(int index, string? fallback = null) =>
        index < _json.Length ? (_json[index].ValueKind == JsonValueKind.String ? _json[index].GetString() : _json[index].GetRawText()) : fallback;

    /// <summary>
    /// Argument <paramref name="index"/> as an integer; when the argument is an options object, reads
    /// <paramref name="option"/> from it instead (<c>{"length": 4}</c>).
    /// </summary>
    public int? Int(int index, string? option = null)
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
            JsonValueKind.Number => value.TryGetInt32(out var i) ? i : (int)value.GetDouble(),
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
    public string[] Strings(int index)
    {
        if (index >= _json.Length || _json[index].ValueKind != JsonValueKind.Array)
            throw new FormatException($"Template argument {index} must be a JSON array.");
        return [.. _json[index].EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())];
    }
}
