using System.Collections.Immutable;

namespace Phoney.Generation;

/// <summary>A rule of <c>WithParentKey</c>: the member at <paramref name="Path"/> gets <paramref name="Key"/> of the nearest object above it that is a <paramref name="Parent"/>.</summary>
/// <param name="Path">The member names from the root to the foreign key (collections are walked element by element).</param>
/// <param name="Parent">The type of the object whose key is copied.</param>
/// <param name="Key">Reads the key from that object.</param>
internal sealed record ParentKey(string[] Path, Type Parent, Func<object, object?> Key)
{
    /// <summary>The path joined with dots, which identifies the rule (a later rule for the same foreign key replaces it).</summary>
    public string Name { get; } = string.Join(".", Path);
}

/// <summary>
/// Sets the foreign keys of <c>WithParentKey</c> on a finished object graph, one key at a time along its path. It walks like every <see cref="ObjectGraph"/>,
/// so reflection and generated models agree, and it draws nothing, so seeds don't move.
/// </summary>
internal sealed class ParentKeys : ObjectGraph
{
    // The key being applied; one walk per key, each from the root.
    private ParentKey _key = null!;

    private ParentKeys()
    {
    }

    /// <summary>
    /// <paramref name="keys"/> with <paramref name="key"/> in place of a key for the same foreign key, kept shallowest first (a deeper key may copy a key set
    /// by a shallower one), and in the order they were added among keys of the same depth.
    /// </summary>
    public static ImmutableList<ParentKey> Add(ImmutableList<ParentKey> keys, ParentKey key)
    {
        keys = keys.RemoveAll(k => k.Name == key.Name);
        var at = keys.FindLastIndex(k => k.Path.Length <= key.Path.Length) + 1;
        return keys.Insert(at, key);
    }

    /// <summary>Applies <paramref name="keys"/>, ordered by <see cref="Add"/>, to the graph below <paramref name="root"/>.</summary>
    public static void Apply(object root, ImmutableList<ParentKey> keys)
    {
        var walk = new ParentKeys();
        foreach (var key in keys)
        {
            walk._key = key;
            walk.Visit(root);
        }
    }

    /// <inheritdoc/>
    protected override void Visit(object node)
    {
        if (ModelOf(node) is not { } model || IndexOf(model, _key.Path[Depth]) is not (>= 0 and var index))
            return;
        var member = model.Members[index];
        if (Depth == _key.Path.Length - 1)
        {
            if (member.IsSettable && Nearest(_key.Parent) is { } parent)
                model.SetMember(node, index, _key.Key(parent));
            return;
        }

        if (IsWalkable(member) && model.GetMember(node, index) is { } value)
            Descend(node, member, value);
    }

    private static int IndexOf(IFakeModel model, string name)
    {
        var members = model.Members;
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Name == name)
                return i;
        }

        return -1;
    }
}
