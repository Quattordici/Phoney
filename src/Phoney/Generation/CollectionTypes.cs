namespace Phoney.Generation;

/// <summary>Which member types are generated as collections, shared by the reflection model and the constraint cases.</summary>
internal static class CollectionTypes
{
    /// <summary>Element type of arrays and generic collections; <paramref name="key"/> is set for dictionaries.</summary>
    public static Type? ElementType(Type type, out Type? key)
    {
        key = null;
        if (type == typeof(string))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        if (!type.IsGenericType)
            return null;

        var definition = type.GetGenericTypeDefinition();
        var args = type.GetGenericArguments();
        if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
        {
            key = args[0];
            return args[1];
        }

        return definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
               definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) ||
               definition == typeof(HashSet<>) || definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>)
            ? args[0]
            : null;
    }

    /// <summary>Whether members of <paramref name="type"/> get a generated number of elements (<c>byte[]</c> is filled with bytes instead).</summary>
    public static bool IsGeneratedCollection(Type type) => type != typeof(byte[]) && ElementType(type, out _) is not null;
}
