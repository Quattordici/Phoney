namespace Phonery.DataCompiler;

/// <summary>Shape of a flattened locale value. Numeric values must match <c>Phonery.Data.EntryKind</c>.</summary>
internal enum EntryKind : byte
{
    Unavailable = 0,
    Strings = 1,
    Weighted = 2,
    Ints = 3,
    Records = 4,
}

/// <summary>A leaf value of a locale definition, addressed by its dotted path (e.g. <c>person.first_name.female</c>).</summary>
internal sealed record LeafEntry(string Path, string Group, EntryKind Kind)
{
    /// <summary>String values (also the values of weighted entries).</summary>
    public IReadOnlyList<string> Strings { get; init; } = [];

    /// <summary>Weights of a weighted entry.</summary>
    public IReadOnlyList<int> Weights { get; init; } = [];

    /// <summary>Integer values.</summary>
    public IReadOnlyList<int> Ints { get; init; } = [];

    /// <summary>Record field names.</summary>
    public IReadOnlyList<string> Fields { get; init; } = [];

    /// <summary>Row-major record values; <c>null</c> when a record lacks the field.</summary>
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = [];

    /// <summary>Whether any value contains a <c>{{...}}</c> expression.</summary>
    public bool HasTemplates =>
        Strings.Any(s => s.Contains("{{", StringComparison.Ordinal)) ||
        Rows.Any(r => r.Any(v => v?.Contains("{{", StringComparison.Ordinal) == true));
}

/// <summary>One faker.js locale with its leaves and fallback chain.</summary>
internal sealed record RawLocale(
    string Code,
    IReadOnlyList<string> Fallback,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, LeafEntry> Leaves)
{
    /// <summary>Entry groups (<c>module.entry</c>) this locale defines itself.</summary>
    public HashSet<string> Groups { get; } = [.. Leaves.Values.Select(l => l.Group)];
}
