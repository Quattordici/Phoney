namespace Phoney.Data;

/// <summary>
/// Country names of one language paired with their ISO 3166-1 alpha-2 codes, read from one <c>code=name|</c> string the first time it is asked. A code
/// may have several names: the first is the one <see cref="NameOf"/> gives, and each of them leads back to the code. A name or code the table doesn't
/// know is looked up in <c>fallback</c> (English for the other languages), so English names keep working everywhere.
/// </summary>
internal sealed class CountryNameTable(string pairs, CountryNameTable? fallback = null)
{
    private Maps? _maps;

    /// <summary>The name this table gives <paramref name="alpha2"/>, else the fallback's, else <see langword="null"/>.</summary>
    public string? NameOf(string alpha2) => Read().Names.GetValueOrDefault(alpha2) ?? fallback?.NameOf(alpha2);

    /// <summary>The alpha-2 code of <paramref name="name"/> in this table, else in the fallback, else <see langword="null"/>.</summary>
    public string? CodeOf(string name) => Read().Codes.GetValueOrDefault(name) ?? fallback?.CodeOf(name);

    // Two threads may both parse the first time; either result is the same and is never changed afterwards.
    private Maps Read() => _maps ??= Parse(pairs);

    private static Maps Parse(string pairs)
    {
        var names = new Dictionary<string, string>(260, StringComparer.Ordinal);
        var codes = new Dictionary<string, string>(280, StringComparer.Ordinal);
        foreach (var pair in pairs.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var (code, name) = (pair[..separator], pair[(separator + 1)..]);
            names.TryAdd(code, name);
            codes.Add(name, code);
        }

        return new Maps(names, codes);
    }

    private sealed record Maps(Dictionary<string, string> Names, Dictionary<string, string> Codes);
}
