using System.Collections.Concurrent;

namespace Phoney.Generation;

/// <summary>Which member types are generated as collections, shared by the reflection model and the constraint cases.</summary>
/// <remarks>
/// The shapes themselves are in <see cref="MemberShapes"/>, which the source generator reads too; this class looks them up by <see cref="Type"/>, once per generic type.
/// </remarks>
internal static class CollectionTypes
{
    /// <summary>The shape of each generic type definition seen so far (<see langword="null"/> for generic types that are not generated collections).</summary>
    private static readonly ConcurrentDictionary<Type, CollectionShape?> Shapes = new();

    /// <summary>How the generic <paramref name="type"/> is built, converted and filled, or <see langword="null"/> when it is not a generated collection.</summary>
    public static CollectionShape? ShapeOf(Type type) =>
        type.IsGenericType ? Shapes.GetOrAdd(type.GetGenericTypeDefinition(), static definition => MemberShapes.TryCollection(definition.FullName ?? "", out var shape) ? shape : null) : null;

    /// <summary>Element type of arrays and generic collections; <paramref name="key"/> is set for dictionaries.</summary>
    public static Type? ElementType(Type type, out Type? key)
    {
        key = null;
        if (type == typeof(string))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        if (KindOf(type) is not { } kind)
            return null;

        var args = type.GetGenericArguments();
        if (kind != CollectionKind.Dictionary)
            return args[0];
        key = args[0];
        return args[1];
    }

    /// <summary>How <paramref name="type"/> is built (<see langword="null"/> for arrays and types that are not generated collections).</summary>
    public static CollectionKind? KindOf(Type type) => ShapeOf(type)?.Kind;

    /// <summary>
    /// Whether a read-only member of <paramref name="type"/> is filled by adding to the collection it holds. Queues, stacks, bags and immutable, frozen and read-only
    /// collections are not: they have no <c>Add</c>, or cannot change.
    /// </summary>
    public static bool IsFillable(Type type) => ShapeOf(type)?.Fillable == true;

    /// <summary>Whether members of <paramref name="type"/> get a generated number of elements (<c>byte[]</c> is filled with bytes instead).</summary>
    public static bool IsGeneratedCollection(Type type) => type != typeof(byte[]) && ElementType(type, out _) is not null;
}
