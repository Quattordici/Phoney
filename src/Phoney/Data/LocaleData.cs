namespace Phoney.Data;

/// <summary>
/// The merged, read-only data of a locale including its fallback chain. Lookups are a single array index.
/// Instances are cached and shared by all <see cref="Faker"/>s using the locale.
/// </summary>
public sealed class LocaleData
{
    private readonly Entry?[] _entries;
    private readonly LocaleResource?[] _origin;

    /// <param name="info">The locale.</param>
    /// <param name="entries">The entries that are already there (user data); a slot that is <see langword="null"/> is read from <paramref name="origin"/> when asked for.</param>
    /// <param name="origin">By key, the embedded file the entry is decoded from on first use, or <see langword="null"/> when there is none.</param>
    internal LocaleData(LocaleInfo info, Entry?[] entries, LocaleResource?[] origin)
    {
        Info = info;
        _entries = entries;
        _origin = origin;
    }

    /// <summary>The locale this data belongs to.</summary>
    public LocaleInfo Info { get; }

    /// <summary>Returns the entry at <paramref name="path"/> (e.g. <c>person.first_name.female</c>), or <see langword="null"/> when there is none.</summary>
    /// <remarks>Returns <see cref="UnavailableEntry"/> when the locale explicitly has no such data.</remarks>
    public Entry? Get(string path) => KeyRegistry.TryGetId(path, out var id) ? Get(id) : null;

    /// <summary>Whether usable data exists at <paramref name="path"/>.</summary>
    public bool Has(string path) => Get(path) is { Kind: not EntryKind.Unavailable };

    /// <summary>Entry by key id; <see langword="null"/> when missing.</summary>
    internal Entry? Get(int key)
    {
        var entries = _entries;
        return (uint)key < (uint)entries.Length ? entries[key] ?? Decode(key) : null;
    }

    /// <summary>Decodes an embedded entry on first use. Every caller gets the instance the resource published, so a race only repeats the lookup.</summary>
    private Entry? Decode(int key)
    {
        var entry = _origin[key]?.Get(key);
        if (entry is not null)
            _entries[key] = entry;
        return entry;
    }

    /// <summary>Whether usable data exists for the key.</summary>
    internal bool Has(int key) => Get(key) is { Kind: not EntryKind.Unavailable };

    /// <summary>String entry for the key, or a descriptive exception.</summary>
    internal StringsEntry Strings(int key) => Get(key) as StringsEntry ?? throw Missing(key, "strings");

    /// <summary>String entry for the key, or <see langword="null"/>.</summary>
    internal StringsEntry? TryStrings(int key) => Get(key) as StringsEntry;

    /// <summary>Records entry for the key, or a descriptive exception.</summary>
    internal RecordsEntry Records(int key) => Get(key) as RecordsEntry ?? throw Missing(key, "records");

    /// <summary>Integer entry for the key, or a descriptive exception.</summary>
    internal IntsEntry Ints(int key) => Get(key) as IntsEntry ?? throw Missing(key, "integers");

    /// <summary>Explains why the key has no usable data: missing, not applicable, or of another kind.</summary>
    internal Exception Missing(int key, string expected)
    {
        var path = KeyRegistry.PathOf(key);
        var entry = Get(key);
        return entry switch
        {
            null => new PhoneyDataException($"Locale '{Info.Code}' has no data for '{path}' (also checked fallback locales: {string.Join(", ", Info.Fallback)})."),
            { Kind: EntryKind.Unavailable } => new PhoneyDataUnavailableException($"The data for '{path}' isn't applicable to locale '{Info.Code}'."),
            _ => new PhoneyDataException($"Data '{path}' in locale '{Info.Code}' is {entry.Kind}, expected {expected}."),
        };
    }
}
