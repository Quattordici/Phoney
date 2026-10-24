#nullable enable
using System;
using System.Collections.Generic;

// This file is compiled into the runtime and into the source generator (see Phoney.SourceGenerator.csproj), so both decide the shape of a member type from the same table.
// Keep it free of APIs newer than netstandard2.0.
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

/// <summary>How the built <c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c> or <c>Dictionary&lt;TKey, TValue&gt;</c> becomes the member's own collection type.</summary>
internal enum CollectionConversion
{
    /// <summary>The built collection already is the member's type (or can be assigned to it).</summary>
    None,

    /// <summary>The built collection is passed to a constructor of the member's type.</summary>
    Constructor,

    /// <summary>The built collection is passed to a static factory method of a builder class.</summary>
    Factory,
}

/// <summary>What is known about a generated collection type: how it is built, converted and filled.</summary>
internal readonly struct CollectionShape
{
    /// <summary>Creates a shape.</summary>
    public CollectionShape(CollectionKind kind, CollectionConversion conversion, string? builder, string? method, bool fillable)
    {
        Kind = kind;
        Conversion = conversion;
        Builder = builder;
        Method = method;
        Fillable = fillable;
    }

    /// <summary>Which collection the elements are built into first.</summary>
    public CollectionKind Kind { get; }

    /// <summary>How the built collection is turned into the member's type.</summary>
    public CollectionConversion Conversion { get; }

    /// <summary>For <see cref="CollectionConversion.Factory"/>: the simple name of the static class that holds the factory, in the namespace of the collection.</summary>
    public string? Builder { get; }

    /// <summary>For <see cref="CollectionConversion.Factory"/>: the name of the generic static factory method.</summary>
    public string? Method { get; }

    /// <summary>Whether a read-only member of this type is filled by adding to the collection it holds.</summary>
    public bool Fillable { get; }
}

/// <summary>
/// Which member types are generated as collections and what kind of types are left alone, decided from names so the reflection model (over <c>System.Type</c>) and the
/// source generator (over Roslyn symbols) read one table. A collection is named by the metadata name of its open generic type, as <c>Type.FullName</c> gives it
/// (<c>System.Collections.Generic.List`1</c>).
/// </summary>
/// <remarks>
/// Every shape is built like one of <c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c> or <c>Dictionary&lt;TKey, TValue&gt;</c> (so a seed gives the same elements whichever
/// type holds them) and converted when the member's type is another one.
/// </remarks>
internal static class MemberShapes
{
    /// <summary>Framework types (<c>System.*</c>, <c>Microsoft.*</c>) are not populated recursively; their constructors expect real data.</summary>
    public static bool IsFrameworkNamespace(string ns) =>
        ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal);

    /// <summary>Looks up a generated collection by the metadata name of its open generic type (<c>System.Collections.Generic.List`1</c>).</summary>
    public static bool TryCollection(string metadataName, out CollectionShape shape) => Catalog.Shapes.TryGetValue(metadataName, out shape);

    /// <summary>
    /// The names of the parts of a <c>KeyValuePair</c> (<c>Key</c>, <c>Value</c>) or of a <c>ValueTuple</c> or <c>Tuple</c> of up to seven items (<c>Item1</c>…), which conventions
    /// are matched with, from the metadata name of the open generic type and its number of type arguments; <see langword="null"/> for other types.
    /// </summary>
    public static string[]? CompositePartNames(string metadataName, int arity)
    {
        if (metadataName == "System.Collections.Generic.KeyValuePair`2")
            return arity == 2 ? new[] { "Key", "Value" } : null;
        if (arity < 1 || arity > 7 || !(metadataName.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || metadataName.StartsWith("System.Tuple`", StringComparison.Ordinal)))
            return null;

        var names = new string[arity];
        for (var i = 0; i < arity; i++)
            names[i] = "Item" + (i + 1);
        return names;
    }

    /// <summary>The metadata names of all generated collections.</summary>
    public static IReadOnlyCollection<string> CollectionNames => Catalog.Shapes.Keys;

    /// <summary>The table, built the first time a collection is looked up.</summary>
    private static class Catalog
    {
        public static readonly Dictionary<string, CollectionShape> Shapes = Build();

        private static Dictionary<string, CollectionShape> Build()
        {
            const string generic = "System.Collections.Generic.";
            const string immutable = "System.Collections.Immutable.";
            const string objectModel = "System.Collections.ObjectModel.";
            const string concurrent = "System.Collections.Concurrent.";
            const string frozen = "System.Collections.Frozen.";
            var shapes = new Dictionary<string, CollectionShape>(41, StringComparer.Ordinal);

            void Add(string name, CollectionKind kind, CollectionConversion conversion = CollectionConversion.None, string? builder = null, string? method = null, bool fillable = false) =>
                shapes.Add(name, new CollectionShape(kind, conversion, builder, method, fillable));
            void Constructed(string name, CollectionKind kind, bool fillable = false) => Add(name, kind, CollectionConversion.Constructor, fillable: fillable);
            void Created(string name, CollectionKind kind, string builder) => Add(name, kind, CollectionConversion.Factory, builder, "CreateRange");

            Add(generic + "List`1", CollectionKind.List, fillable: true);
            Add(generic + "IList`1", CollectionKind.List, fillable: true);
            Add(generic + "ICollection`1", CollectionKind.List, fillable: true);
            Add(generic + "IEnumerable`1", CollectionKind.List);
            Add(generic + "IReadOnlyList`1", CollectionKind.List);
            Add(generic + "IReadOnlyCollection`1", CollectionKind.List);
            Created(immutable + "ImmutableArray`1", CollectionKind.List, "ImmutableArray");
            Created(immutable + "ImmutableList`1", CollectionKind.List, "ImmutableList");
            Created(immutable + "IImmutableList`1", CollectionKind.List, "ImmutableList");
            Constructed(objectModel + "ReadOnlyCollection`1", CollectionKind.List);
            Created(immutable + "ImmutableQueue`1", CollectionKind.List, "ImmutableQueue");
            Created(immutable + "IImmutableQueue`1", CollectionKind.List, "ImmutableQueue");
            Created(immutable + "ImmutableStack`1", CollectionKind.List, "ImmutableStack");
            Created(immutable + "IImmutableStack`1", CollectionKind.List, "ImmutableStack");
            Constructed(objectModel + "Collection`1", CollectionKind.List, fillable: true);
            Constructed(objectModel + "ObservableCollection`1", CollectionKind.List, fillable: true);
            Constructed(generic + "Queue`1", CollectionKind.List);
            Constructed(generic + "Stack`1", CollectionKind.List);
            Constructed(generic + "LinkedList`1", CollectionKind.List, fillable: true);
            Constructed(concurrent + "ConcurrentBag`1", CollectionKind.List);
            Constructed(concurrent + "ConcurrentQueue`1", CollectionKind.List);
            Constructed(concurrent + "ConcurrentStack`1", CollectionKind.List);

            Add(generic + "HashSet`1", CollectionKind.Set, fillable: true);
            Add(generic + "ISet`1", CollectionKind.Set, fillable: true);
            Add(generic + "IReadOnlySet`1", CollectionKind.Set);
            Created(immutable + "ImmutableHashSet`1", CollectionKind.Set, "ImmutableHashSet");
            Created(immutable + "IImmutableSet`1", CollectionKind.Set, "ImmutableHashSet");
            Created(immutable + "ImmutableSortedSet`1", CollectionKind.Set, "ImmutableSortedSet");
            Constructed(generic + "SortedSet`1", CollectionKind.Set, fillable: true);
            Add(frozen + "FrozenSet`1", CollectionKind.Set, CollectionConversion.Factory, "FrozenSet", "ToFrozenSet");

            Add(generic + "Dictionary`2", CollectionKind.Dictionary, fillable: true);
            Add(generic + "IDictionary`2", CollectionKind.Dictionary, fillable: true);
            Add(generic + "IReadOnlyDictionary`2", CollectionKind.Dictionary);
            Created(immutable + "ImmutableDictionary`2", CollectionKind.Dictionary, "ImmutableDictionary");
            Created(immutable + "IImmutableDictionary`2", CollectionKind.Dictionary, "ImmutableDictionary");
            Created(immutable + "ImmutableSortedDictionary`2", CollectionKind.Dictionary, "ImmutableSortedDictionary");
            Constructed(generic + "SortedDictionary`2", CollectionKind.Dictionary, fillable: true);
            Constructed(generic + "SortedList`2", CollectionKind.Dictionary, fillable: true);
            Constructed(concurrent + "ConcurrentDictionary`2", CollectionKind.Dictionary, fillable: true);
            Add(frozen + "FrozenDictionary`2", CollectionKind.Dictionary, CollectionConversion.Factory, "FrozenDictionary", "ToFrozenDictionary");
            Constructed(objectModel + "ReadOnlyDictionary`2", CollectionKind.Dictionary);

            return shapes;
        }
    }
}
