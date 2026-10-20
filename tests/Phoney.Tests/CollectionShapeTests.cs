using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>Every kind of collection Phoney builds besides arrays, lists, sets and dictionaries.</summary>
public sealed class Catalog
{
    public ImmutableArray<string> Tags { get; set; }
    public ImmutableList<int> Numbers { get; set; } = null!;
    public IImmutableList<string> Labels { get; set; } = null!;
    public ImmutableHashSet<string> Words { get; set; } = null!;
    public IImmutableSet<int> Levels { get; set; } = null!;
    public ImmutableSortedSet<int> Ranks { get; set; } = null!;
    public ImmutableDictionary<string, int> Scores { get; set; } = null!;
    public IImmutableDictionary<string, string> Names { get; set; } = null!;
    public ImmutableSortedDictionary<string, int> Ordered { get; set; } = null!;
    public FrozenSet<string> Seen { get; set; } = null!;
    public FrozenDictionary<string, int> Counts { get; set; } = null!;
    public ConcurrentDictionary<string, int> Hits { get; set; } = null!;
    public ConcurrentBag<int> Bag { get; set; } = null!;
    public ConcurrentQueue<string> Pending { get; set; } = null!;
    public ConcurrentStack<int> History { get; set; } = null!;
    public SortedSet<int> SortedNumbers { get; set; } = null!;
    public SortedDictionary<string, int> SortedScores { get; set; } = null!;
    public SortedList<string, int> SortedPrices { get; set; } = null!;
    public Queue<int> Fifo { get; set; } = null!;
    public Stack<int> Lifo { get; set; } = null!;
    public LinkedList<int> Chain { get; set; } = null!;
    public ReadOnlyCollection<string> ReadOnly { get; set; } = null!;
    public Collection<string> Editable { get; set; } = null!;
    public System.Collections.ObjectModel.ObservableCollection<string> Observable { get; set; } = null!;
    public System.Collections.ObjectModel.ReadOnlyDictionary<string, int> Lookup { get; set; } = null!;
    public ImmutableQueue<string> Waiting { get; set; } = null!;
    public IImmutableQueue<int> Upcoming { get; set; } = null!;
    public ImmutableStack<int> Undo { get; set; } = null!;
    public IImmutableStack<string> Redo { get; set; } = null!;
    [MinLength(4)] public ImmutableArray<int> AtLeastFour { get; set; }
}

[FakeFor<Catalog>]
public static partial class CatalogFakes;

public sealed class CollectionShapeTests
{
    private static ObjectGenerator<Catalog> Generated() => CatalogFakes.Catalog.Seed(8);

    private static ObjectGenerator<Catalog> Reflected() => new ObjectGenerator<Catalog>(new ReflectionModel<Catalog>()).Seed(8);

    private static PropertyInfo[] Collections() => typeof(Catalog).GetProperties();

    private static List<string> Items(Catalog catalog, PropertyInfo property) =>
        ((IEnumerable)property.GetValue(catalog)!).Cast<object>().Select(o => o.ToString()!).ToList();

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Every_collection_member_is_created_and_filled(bool generated)
    {
        var catalogs = (generated ? Generated() : Reflected()).CollectionSize(1, 3).Generate(10);

        foreach (var property in Collections())
        {
            catalogs.ShouldAllBe(c => property.GetValue(c) != null || property.PropertyType.IsValueType, property.Name);
            catalogs.ShouldAllBe(c => Items(c, property).Count >= 1 && Items(c, property).Count <= 3 || property.Name == "AtLeastFour", property.Name);
        }

        catalogs.ShouldAllBe(c => c.Tags.All(t => t.Length > 0) && !c.Tags.IsDefault);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_collections_have_the_requested_type_and_the_size_asked_for(bool generated)
    {
        var catalog = (generated ? Generated() : Reflected()).CollectionSize(2, 2).Generate();

        catalog.Tags.Length.ShouldBe(2);
        catalog.Numbers.ShouldBeOfType<ImmutableList<int>>().Count.ShouldBe(2);
        catalog.Labels.ShouldBeAssignableTo<IImmutableList<string>>().Count.ShouldBe(2);
        catalog.Ranks.Count.ShouldBeLessThanOrEqualTo(2);
        catalog.Ordered.Keys.Order(StringComparer.Ordinal).ShouldBe(catalog.Ordered.Keys); // kept sorted by the collection
        catalog.Seen.Count.ShouldBe(2);
        catalog.Counts.Count.ShouldBe(2);
        catalog.Hits.Count.ShouldBe(2);
        catalog.SortedNumbers.Min.ShouldBeLessThanOrEqualTo(catalog.SortedNumbers.Max);
        catalog.Fifo.Count.ShouldBe(2);
        catalog.Chain.Count.ShouldBe(2);
        catalog.ReadOnly.Count.ShouldBe(2);
        catalog.Observable.Count.ShouldBe(2);
        catalog.Lookup.Count.ShouldBe(2);
        catalog.Waiting.Count().ShouldBe(2);
        catalog.Upcoming.Count().ShouldBe(2);
        catalog.Undo.Count().ShouldBe(2);
        catalog.Redo.Count().ShouldBe(2);
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data()
    {
        var generated = Generated().Generate(15);
        var reflected = Reflected().Generate(15);

        for (var i = 0; i < generated.Count; i++)
        {
            foreach (var property in Collections())
            {
                var expected = Items(generated[i], property);
                var actual = Items(reflected[i], property);
                // Hash based collections enumerate in an order that is theirs to choose; what they hold must be the same.
                var name = property.PropertyType.Name;
                if (name.Contains("Hash", StringComparison.Ordinal) || name.Contains("Concurrent", StringComparison.Ordinal) || name.Contains("Frozen", StringComparison.Ordinal))
                {
                    expected.Sort(StringComparer.Ordinal);
                    actual.Sort(StringComparer.Ordinal);
                }

                actual.ShouldBe(expected, $"{property.Name} of item {i}");
            }
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Annotations_set_the_size_and_Invalid_breaks_it(bool generated)
    {
        var generator = generated ? Generated() : Reflected();

        generator.Generate(10).ShouldAllBe(c => c.AtLeastFour.Length >= 4);
        generator.Invalid(c => c.AtLeastFour).Generate(10).ShouldAllBe(c => c.AtLeastFour.Length == 3);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Populate_fills_a_default_ImmutableArray_and_keeps_one_that_is_set(bool generated)
    {
        var generator = generated ? Generated() : Reflected();

        var filled = generator.Populate(new Catalog());
        var kept = generator.Populate(new Catalog { Tags = ["kept"] });

        filled.Tags.IsDefault.ShouldBeFalse();
        filled.Tags.ShouldNotBeEmpty();
        kept.Tags.ShouldBe(["kept"]);
        kept.Numbers.ShouldNotBeEmpty();
    }

    [Test]
    public void The_non_generic_entry_points_create_objects_of_a_type_known_at_runtime()
    {
        var type = typeof(Ticket);

        var one = Fake.One(type).ShouldBeOfType<Ticket>();
        var many = Fake.Many(type, 5);

        one.Code.ShouldNotBeNullOrEmpty();
        many.Count.ShouldBe(5);
        many.ShouldAllBe(x => x is Ticket);
        var scalar = typeof(int);
        Fake.One(scalar).ShouldBeOfType<int>();
        string Code()
        {
            using var _ = Fake.Seeded(3);
            return ((Ticket)Fake.One(type)).Code;
        }

        Code().ShouldBe(Code());
    }

    [Test]
    public void The_non_generic_entry_points_validate_their_arguments_and_report_errors_unwrapped()
    {
        Should.Throw<ArgumentNullException>(() => Fake.One(null!));
        var negative = -1; // not a constant, which PHONEY011 would report
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Many(typeof(Ticket), negative));
        Should.Throw<ArgumentException>(() => Fake.One(typeof(List<>))).Message.ShouldContain("open generic");
    }
}
