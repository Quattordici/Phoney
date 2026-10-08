namespace Phony.DataCompiler;

/// <summary>
/// The global key catalog: every leaf path found in any locale gets a stable (per data version) integer id,
/// so the runtime can index locale data with an array lookup instead of hashing strings.
/// </summary>
internal sealed class Catalog
{
    /// <summary>All leaf paths, sorted; the index is the key id.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>All entry groups (<c>module.entry</c>), sorted.</summary>
    public IReadOnlyList<string> Groups { get; }

    /// <summary>Key id by path.</summary>
    public IReadOnlyDictionary<string, int> KeyIds { get; }

    /// <summary>Group id of each key id.</summary>
    public IReadOnlyList<int> GroupOfKey { get; }

    /// <summary>Entry kind of each key id.</summary>
    public IReadOnlyList<EntryKind> KindOfKey { get; }

    /// <summary>Builds the catalog from every locale's leaves; fails when a key has different kinds in different locales.</summary>
    public Catalog(IReadOnlyList<RawLocale> locales)
    {
        var kinds = new Dictionary<string, EntryKind>(StringComparer.Ordinal);
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var locale in locales)
        {
            foreach (var leaf in locale.Leaves.Values)
            {
                groups[leaf.Path] = leaf.Group;
                if (leaf.Kind == EntryKind.Unavailable)
                {
                    kinds.TryAdd(leaf.Path, EntryKind.Unavailable);
                    continue;
                }

                if (kinds.TryGetValue(leaf.Path, out var existing) && existing != EntryKind.Unavailable && existing != leaf.Kind)
                    throw new InvalidDataException($"{locale.Code}: '{leaf.Path}' is {leaf.Kind} but {existing} in another locale.");
                kinds[leaf.Path] = leaf.Kind;
            }
        }

        Keys = [.. kinds.Keys.Order(StringComparer.Ordinal)];
        Groups = [.. groups.Values.Distinct().Order(StringComparer.Ordinal)];
        KeyIds = Keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i, StringComparer.Ordinal);
        var groupIds = Groups.Select((g, i) => (g, i)).ToDictionary(x => x.g, x => x.i, StringComparer.Ordinal);
        GroupOfKey = [.. Keys.Select(k => groupIds[groups[k]])];
        KindOfKey = [.. Keys.Select(k => kinds[k])];
    }
}
