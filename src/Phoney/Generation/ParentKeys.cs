using System.Collections;

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
/// Sets the foreign keys of <c>WithParentKey</c> on a finished object graph. Like <see cref="BackReferences"/>, it walks settable members with the models of
/// the objects' runtime types, so reflection and generated models agree, and it draws nothing, so seeds don't move.
/// </summary>
internal static class ParentKeys
{
    /// <summary>Applies <paramref name="keys"/> to the graph below <paramref name="root"/>, shallower foreign keys first (a deeper one may copy a key set by them).</summary>
    public static void Apply(object root, IReadOnlyList<ParentKey> keys)
    {
        var ancestors = new List<object>();
        foreach (var key in keys.OrderBy(k => k.Path.Length))
            Visit(root, key, 0, ancestors);
    }

    private static void Visit(object node, ParentKey key, int depth, List<object> ancestors)
    {
        if (ModelRegistry.Get(node.GetType()) is not { } model || IndexOf(model, key.Path[depth]) is not (>= 0 and var index))
            return;
        var member = model.Members[index];
        if (depth == key.Path.Length - 1)
        {
            if (member.IsSettable && Nearest(ancestors, key.Parent) is { } parent)
                model.SetMember(node, index, key.Key(parent));
            return;
        }

        if ((member.IsSettable || member.FillsExisting) && model.GetMember(node, index) is { } value)
            VisitChildren(node, value, CollectionTypes.IsGeneratedCollection(member.Type), key, depth, ancestors);
    }

    /// <summary>Visits <paramref name="value"/> (each element when it is a collection) one level deeper, with <paramref name="node"/> as the nearest ancestor.</summary>
    private static void VisitChildren(object node, object value, bool isCollection, ParentKey key, int depth, List<object> ancestors)
    {
        ancestors.Add(node);
        if (isCollection)
        {
            foreach (var element in value is IDictionary dictionary ? dictionary.Values : (IEnumerable)value)
            {
                if (element is not null)
                    Visit(element, key, depth + 1, ancestors);
            }
        }
        else
        {
            Visit(value, key, depth + 1, ancestors);
        }

        ancestors.RemoveAt(ancestors.Count - 1);
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

    /// <summary>The closest object above the foreign key's owner that is a <paramref name="type"/>.</summary>
    private static object? Nearest(List<object> ancestors, Type type)
    {
        for (var i = ancestors.Count - 1; i >= 0; i--)
        {
            if (type.IsInstanceOfType(ancestors[i]))
                return ancestors[i];
        }

        return null;
    }
}
