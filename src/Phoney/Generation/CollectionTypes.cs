using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Phoney.Generation;

/// <summary>How a collection is built: lists keep their order, sets hold distinct elements, dictionaries map keys to values.</summary>
internal enum CollectionKind
{
    /// <summary>A sequence of elements.</summary>
    List,

    /// <summary>Distinct elements.</summary>
    Set,

    /// <summary>Keys and values.</summary>
    Dictionary,
}

/// <summary>Which member types are generated as collections, shared by the reflection model and the constraint cases.</summary>
/// <remarks>
/// Every shape is built like one of <c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c> or <c>Dictionary&lt;TKey, TValue&gt;</c> (so a seed gives the same elements whichever
/// type holds them) and converted when the member's type is another one. The source generator mirrors this table in <c>ModelBuilder</c>.
/// </remarks>
internal static class CollectionTypes
{
    private static readonly Dictionary<Type, CollectionKind> Definitions = new()
    {
        [typeof(List<>)] = CollectionKind.List,
        [typeof(IList<>)] = CollectionKind.List,
        [typeof(ICollection<>)] = CollectionKind.List,
        [typeof(IEnumerable<>)] = CollectionKind.List,
        [typeof(IReadOnlyList<>)] = CollectionKind.List,
        [typeof(IReadOnlyCollection<>)] = CollectionKind.List,
        [typeof(ImmutableArray<>)] = CollectionKind.List,
        [typeof(ImmutableList<>)] = CollectionKind.List,
        [typeof(IImmutableList<>)] = CollectionKind.List,
        [typeof(ReadOnlyCollection<>)] = CollectionKind.List,
        [typeof(ImmutableQueue<>)] = CollectionKind.List,
        [typeof(IImmutableQueue<>)] = CollectionKind.List,
        [typeof(ImmutableStack<>)] = CollectionKind.List,
        [typeof(IImmutableStack<>)] = CollectionKind.List,
        [typeof(Collection<>)] = CollectionKind.List,
        [typeof(ObservableCollection<>)] = CollectionKind.List,
        [typeof(Queue<>)] = CollectionKind.List,
        [typeof(Stack<>)] = CollectionKind.List,
        [typeof(LinkedList<>)] = CollectionKind.List,
        [typeof(ConcurrentBag<>)] = CollectionKind.List,
        [typeof(ConcurrentQueue<>)] = CollectionKind.List,
        [typeof(ConcurrentStack<>)] = CollectionKind.List,
        [typeof(HashSet<>)] = CollectionKind.Set,
        [typeof(ISet<>)] = CollectionKind.Set,
        [typeof(IReadOnlySet<>)] = CollectionKind.Set,
        [typeof(ImmutableHashSet<>)] = CollectionKind.Set,
        [typeof(IImmutableSet<>)] = CollectionKind.Set,
        [typeof(ImmutableSortedSet<>)] = CollectionKind.Set,
        [typeof(SortedSet<>)] = CollectionKind.Set,
        [typeof(FrozenSet<>)] = CollectionKind.Set,
        [typeof(Dictionary<,>)] = CollectionKind.Dictionary,
        [typeof(IDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(IReadOnlyDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(ImmutableDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(IImmutableDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(ImmutableSortedDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(SortedDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(SortedList<,>)] = CollectionKind.Dictionary,
        [typeof(ConcurrentDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(FrozenDictionary<,>)] = CollectionKind.Dictionary,
        [typeof(ReadOnlyDictionary<,>)] = CollectionKind.Dictionary,
    };

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
    public static CollectionKind? KindOf(Type type) =>
        type.IsGenericType && Definitions.TryGetValue(type.GetGenericTypeDefinition(), out var kind) ? kind : null;

    /// <summary>The collections whose elements can be added to an instance the object already holds (they implement <c>ICollection&lt;T&gt;</c> or <c>IDictionary&lt;K,V&gt;</c> and are mutable).</summary>
    private static readonly HashSet<Type> Fillable =
    [
        typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(HashSet<>), typeof(ISet<>), typeof(Collection<>), typeof(ObservableCollection<>),
        typeof(SortedSet<>), typeof(LinkedList<>), typeof(Dictionary<,>), typeof(IDictionary<,>), typeof(SortedDictionary<,>), typeof(SortedList<,>), typeof(ConcurrentDictionary<,>),
    ];

    /// <summary>
    /// Whether a read-only member of <paramref name="type"/> is filled by adding to the collection it holds. Queues, stacks, bags and immutable, frozen and read-only
    /// collections are not: they have no <c>Add</c>, or cannot change.
    /// </summary>
    public static bool IsFillable(Type type) => type.IsGenericType && Fillable.Contains(type.GetGenericTypeDefinition());

    /// <summary>Whether members of <paramref name="type"/> get a generated number of elements (<c>byte[]</c> is filled with bytes instead).</summary>
    public static bool IsGeneratedCollection(Type type) => type != typeof(byte[]) && ElementType(type, out _) is not null;
}
