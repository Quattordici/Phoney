using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>The one table of generated collection shapes, shared by the reflection model and the source generator.</summary>
public sealed class MemberShapesTests
{
    /// <summary>Every collection shape Phoney generates, as the open generic type, how it is built and whether it is converted from the built collection.</summary>
    private static readonly (Type Type, CollectionKind Kind, CollectionConversion Conversion)[] Expected =
    [
        (typeof(List<>), CollectionKind.List, CollectionConversion.None),
        (typeof(IList<>), CollectionKind.List, CollectionConversion.None),
        (typeof(ICollection<>), CollectionKind.List, CollectionConversion.None),
        (typeof(IEnumerable<>), CollectionKind.List, CollectionConversion.None),
        (typeof(IReadOnlyList<>), CollectionKind.List, CollectionConversion.None),
        (typeof(IReadOnlyCollection<>), CollectionKind.List, CollectionConversion.None),
        (typeof(ImmutableArray<>), CollectionKind.List, CollectionConversion.Factory),
        (typeof(ImmutableList<>), CollectionKind.List, CollectionConversion.Factory),
        (typeof(IImmutableList<>), CollectionKind.List, CollectionConversion.Factory),
        (typeof(ReadOnlyCollection<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(ImmutableQueue<>), CollectionKind.List, CollectionConversion.Factory),
        (typeof(IImmutableQueue<>), CollectionKind.List, CollectionConversion.Factory),
        (typeof(ImmutableStack<>), CollectionKind.List, CollectionConversion.Factory),
        (typeof(IImmutableStack<>), CollectionKind.List, CollectionConversion.Factory),
        (typeof(Collection<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(ObservableCollection<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(Queue<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(Stack<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(LinkedList<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(ConcurrentBag<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(ConcurrentQueue<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(ConcurrentStack<>), CollectionKind.List, CollectionConversion.Constructor),
        (typeof(HashSet<>), CollectionKind.Set, CollectionConversion.None),
        (typeof(ISet<>), CollectionKind.Set, CollectionConversion.None),
        (typeof(IReadOnlySet<>), CollectionKind.Set, CollectionConversion.None),
        (typeof(ImmutableHashSet<>), CollectionKind.Set, CollectionConversion.Factory),
        (typeof(IImmutableSet<>), CollectionKind.Set, CollectionConversion.Factory),
        (typeof(ImmutableSortedSet<>), CollectionKind.Set, CollectionConversion.Factory),
        (typeof(SortedSet<>), CollectionKind.Set, CollectionConversion.Constructor),
        (typeof(FrozenSet<>), CollectionKind.Set, CollectionConversion.Factory),
        (typeof(Dictionary<,>), CollectionKind.Dictionary, CollectionConversion.None),
        (typeof(IDictionary<,>), CollectionKind.Dictionary, CollectionConversion.None),
        (typeof(IReadOnlyDictionary<,>), CollectionKind.Dictionary, CollectionConversion.None),
        (typeof(ImmutableDictionary<,>), CollectionKind.Dictionary, CollectionConversion.Factory),
        (typeof(IImmutableDictionary<,>), CollectionKind.Dictionary, CollectionConversion.Factory),
        (typeof(ImmutableSortedDictionary<,>), CollectionKind.Dictionary, CollectionConversion.Factory),
        (typeof(SortedDictionary<,>), CollectionKind.Dictionary, CollectionConversion.Constructor),
        (typeof(SortedList<,>), CollectionKind.Dictionary, CollectionConversion.Constructor),
        (typeof(ConcurrentDictionary<,>), CollectionKind.Dictionary, CollectionConversion.Constructor),
        (typeof(FrozenDictionary<,>), CollectionKind.Dictionary, CollectionConversion.Factory),
        (typeof(ReadOnlyDictionary<,>), CollectionKind.Dictionary, CollectionConversion.Constructor),
    ];

    /// <summary>The collections whose elements are added to the instance a read-only member already holds.</summary>
    private static readonly Type[] Fillable =
    [
        typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(HashSet<>), typeof(ISet<>), typeof(Collection<>), typeof(ObservableCollection<>),
        typeof(SortedSet<>), typeof(LinkedList<>), typeof(Dictionary<,>), typeof(IDictionary<,>), typeof(SortedDictionary<,>), typeof(SortedList<,>), typeof(ConcurrentDictionary<,>),
    ];

    [Test]
    public void Every_generated_collection_shape_is_in_the_table_with_its_kind_and_conversion()
    {
        foreach (var (type, kind, conversion) in Expected)
        {
            MemberShapes.TryCollection(type.FullName!, out var shape).ShouldBeTrue(type.Name);
            shape.Kind.ShouldBe(kind, type.Name);
            shape.Conversion.ShouldBe(conversion, type.Name);
        }
    }

    [Test]
    public void The_table_holds_no_other_shapes()
    {
        MemberShapes.CollectionNames.Count.ShouldBe(Expected.Length);
        MemberShapes.CollectionNames.ShouldBe(Expected.Select(e => e.Type.FullName!), ignoreOrder: true);
    }

    [Test]
    public void The_kind_follows_from_the_number_of_type_parameters()
    {
        foreach (var (type, kind, _) in Expected)
            type.GetGenericArguments().Length.ShouldBe(kind == CollectionKind.Dictionary ? 2 : 1, type.Name);
    }

    [Test]
    public void The_fillable_collections_are_the_mutable_ones_that_can_be_added_to()
    {
        foreach (var (type, _, _) in Expected)
        {
            MemberShapes.TryCollection(type.FullName!, out var shape).ShouldBeTrue(type.Name);
            shape.Fillable.ShouldBe(Fillable.Contains(type), type.Name);
        }
    }

    [Test]
    public void Names_that_are_not_generated_collections_are_not_in_the_table()
    {
        MemberShapes.TryCollection("System.String", out _).ShouldBeFalse();
        MemberShapes.TryCollection("System.Collections.Generic.KeyValuePair`2", out _).ShouldBeFalse();
        MemberShapes.TryCollection("System.Collections.Generic.List", out _).ShouldBeFalse();
        MemberShapes.TryCollection("", out _).ShouldBeFalse();
    }

    [Test]
    public void A_factory_names_a_builder_that_exists_next_to_the_collection_with_that_method()
    {
        var factories = 0;
        foreach (var (type, _, conversion) in Expected)
        {
            MemberShapes.TryCollection(type.FullName!, out var shape).ShouldBeTrue(type.Name);
            if (conversion != CollectionConversion.Factory)
            {
                shape.Builder.ShouldBeNull(type.Name);
                shape.Method.ShouldBeNull(type.Name);
                continue;
            }

            factories++;
            var builder = type.Assembly.GetType(type.Namespace + "." + shape.Builder);
            builder.ShouldNotBeNull(type.Name);
            builder.GetMethods().Any(m => m.Name == shape.Method && m.IsStatic && m.IsGenericMethodDefinition).ShouldBeTrue(type.Name);
        }

        factories.ShouldBe(15);
    }

    [Test]
    [Arguments("System", true)]
    [Arguments("System.Collections.Generic", true)]
    [Arguments("Microsoft.Extensions.Logging", true)]
    [Arguments("Systemic", false)]
    [Arguments("MyApp.System", false)]
    [Arguments("", false)]
    public void Framework_types_are_the_ones_in_System_and_Microsoft_namespaces(string ns, bool expected) =>
        MemberShapes.IsFrameworkNamespace(ns).ShouldBe(expected);

    [Test]
    public void A_key_value_pair_has_a_key_and_a_value()
    {
        MemberShapes.CompositePartNames("System.Collections.Generic.KeyValuePair`2", 2).ShouldBe(["Key", "Value"]);
        MemberShapes.CompositePartNames("System.Collections.Generic.KeyValuePair`2", 3).ShouldBeNull();
    }

    [Test]
    [Arguments("System.ValueTuple`1", 1)]
    [Arguments("System.ValueTuple`4", 4)]
    [Arguments("System.ValueTuple`7", 7)]
    [Arguments("System.Tuple`1", 1)]
    [Arguments("System.Tuple`3", 3)]
    [Arguments("System.Tuple`7", 7)]
    public void A_tuple_has_numbered_items(string metadataName, int arity) =>
        MemberShapes.CompositePartNames(metadataName, arity).ShouldBe(Enumerable.Range(1, arity).Select(i => "Item" + i));

    [Test]
    [Arguments("System.ValueTuple`8", 8)]
    [Arguments("System.Tuple`8", 8)]
    [Arguments("System.ValueTuple", 0)]
    [Arguments("System.Foo.Tuple`2", 2)]
    [Arguments("MyApp.Tuple`2", 2)]
    [Arguments("System.Collections.Generic.List`1", 1)]
    [Arguments("", 0)]
    public void Other_types_are_not_composites(string metadataName, int arity) =>
        MemberShapes.CompositePartNames(metadataName, arity).ShouldBeNull();

    [Test]
    public void The_names_match_the_real_framework_types()
    {
        foreach (var type in new[] { typeof(KeyValuePair<int, string>), typeof(ValueTuple<int, string>), typeof(Tuple<int, string, bool>) })
        {
            var names = MemberShapes.CompositePartNames(type.GetGenericTypeDefinition().FullName!, type.GetGenericArguments().Length);
            names.ShouldNotBeNull(type.Name);
            foreach (var name in names)
                (type.GetProperty(name) is not null || type.GetField(name) is not null).ShouldBeTrue(type.Name + "." + name);
        }
    }
}
