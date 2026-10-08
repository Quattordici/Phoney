using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Phony.Data;

/// <summary>
/// Maps dotted data paths to integer key ids. Ids <c>0..DataKeys.Count-1</c> are the compiled faker.js
/// catalog; paths introduced by user-registered data get ids after that.
/// </summary>
internal static class KeyRegistry
{
    private static readonly FrozenDictionary<string, int> Catalog =
        DataKeys.Paths.Select((path, id) => (path, id)).ToFrozenDictionary(x => x.path, x => x.id, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, int> CatalogGroups = BuildCatalogGroups();

    private static readonly ConcurrentDictionary<string, int> CustomKeys = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> CustomGroups = new(StringComparer.Ordinal);
    private static readonly List<int> CustomKeyGroups = [];
    private static readonly object Gate = new();

    /// <summary>Total number of key ids handed out so far.</summary>
    public static int Count => DataKeys.Count + CustomKeys.Count;

    /// <summary>Total number of entry groups known so far.</summary>
    public static int GroupCount => DataKeys.GroupCount + CustomGroups.Count;

    /// <summary>Looks up the id of a catalog or custom path.</summary>
    public static bool TryGetId(string path, out int id) =>
        Catalog.TryGetValue(path, out id) || CustomKeys.TryGetValue(path, out id);

    /// <summary>Returns the id of <paramref name="path"/>, registering it as a custom key when unknown.</summary>
    public static int GetOrAdd(string path)
    {
        if (TryGetId(path, out var id))
            return id;

        ValidatePath(path);
        lock (Gate)
        {
            if (CustomKeys.TryGetValue(path, out id))
                return id;
            var group = GroupName(path);
            if (!CatalogGroups.TryGetValue(group, out var groupId))
                groupId = CustomGroups.GetOrAdd(group, _ => DataKeys.GroupCount + CustomGroups.Count);
            id = DataKeys.Count + CustomKeyGroups.Count;
            CustomKeyGroups.Add(groupId);
            CustomKeys[path] = id;
            return id;
        }
    }

    /// <summary>Returns the dotted path of a key id (for error messages).</summary>
    public static string PathOf(int id)
    {
        if (id < DataKeys.Count)
            return DataKeys.Paths[id];
        foreach (var (path, customId) in CustomKeys)
        {
            if (customId == id)
                return path;
        }

        throw new ArgumentOutOfRangeException(nameof(id));
    }

    /// <summary>Returns the entry group of a key id; fallback is resolved per group.</summary>
    public static int GroupOf(int id)
    {
        if (id < DataKeys.Count)
            return DataKeys.GroupOf[id];
        lock (Gate)
            return CustomKeyGroups[id - DataKeys.Count];
    }

    /// <summary>faker.js merges locales per <c>module.entry</c>, i.e. the first two path segments.</summary>
    public static string GroupName(string path)
    {
        var first = path.IndexOf('.');
        var second = first < 0 ? -1 : path.IndexOf('.', first + 1);
        return second < 0 ? path : path[..second];
    }

    /// <summary>Rejects paths that are not <c>module.entry[.more]</c>.</summary>
    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('.') <= 0 || path.EndsWith('.') || path.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"'{path}' is not a valid data path. Use dotted paths such as 'person.nickname' or 'person.first_name.generic'.", nameof(path));
    }

    /// <summary>Maps group names of the compiled catalog to their ids, so custom keys join existing groups.</summary>
    private static FrozenDictionary<string, int> BuildCatalogGroups()
    {
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < DataKeys.Count; i++)
            groups.TryAdd(GroupName(DataKeys.Paths[i]), DataKeys.GroupOf[i]);
        return groups.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
