using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class BasketLine
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}

/// <summary>Collections that can only be read (<c>{ get; }</c>, a private setter, a readonly field) and are filled in place.</summary>
public sealed class Basket
{
    public string Owner { get; set; } = "";
    public List<string> Items { get; } = [];
    public IList<BasketLine> Lines { get; } = new List<BasketLine>();
    public ISet<string> Tags { get; } = new HashSet<string>();
    public Dictionary<string, int> Counts { get; } = [];
    public IDictionary<string, string> Notes { get; } = new Dictionary<string, string>();
    public ICollection<int> Numbers { get; private set; } = new List<int>();
    public readonly List<string> Fields = [];
    public ObservableCollection<string> Watched { get; } = [];
    [MinLength(4)] public List<string> Long { get; } = [];

    // Nothing to add to, or no way to add: left alone.
    public IReadOnlyList<string> Hidden { get; } = new List<string>();
    public Queue<string> Pending { get; } = new();
    public List<string>? Missing { get; }
    public IList<string> Fixed { get; } = Array.Empty<string>();
}

public sealed class Layout
{
    public List<string> Early { get; } = [];
    public string Name { get; set; } = "";
    public List<string> Late { get; } = [];
}

public sealed class PlainName
{
    public string Name { get; set; } = "";
}

public sealed class Volume
{
    public string Title { get; set; } = "";
    public List<Section> Sections { get; } = [];
}

public sealed class Section
{
    public string Heading { get; set; } = "";
    public Volume? Volume { get; set; }
}

[FakeFor<Basket>, FakeFor<Volume>]
public static partial class BasketFakes;

public sealed class ReadOnlyCollectionTests
{
    private static readonly string[] AB = ["a", "b"];
    private static readonly string[] Only = ["only"];

    private static ObjectGenerator<Basket> Make() => Fake.For<Basket>().Seed(6);

    private static ObjectGenerator<Basket> Generated() => BasketFakes.Basket.Seed(6);

    private static ObjectGenerator<Basket> Reflected() => new ObjectGenerator<Basket>(new ReflectionModel<Basket>()).Seed(6);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Collections_with_a_getter_only_are_filled_in_place(bool generated)
    {
        var baskets = (generated ? Generated() : Reflected()).Generate(10);

        baskets.ShouldAllBe(b => b.Items.Count >= 1 && b.Lines.Count >= 1 && b.Tags.Count >= 1 && b.Counts.Count >= 1 && b.Notes.Count >= 1);
        baskets.ShouldAllBe(b => b.Numbers.Count >= 1 && b.Fields.Count >= 1 && b.Watched.Count >= 1);
        baskets.SelectMany(b => b.Lines).ShouldAllBe(l => l.Sku.Length > 0 && l.Quantity > 0); // elements are generated like those of any other list
        baskets.SelectMany(b => b.Items).ShouldAllBe(i => i.Length > 0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Collections_that_cannot_be_added_to_are_left_alone(bool generated)
    {
        var baskets = (generated ? Generated() : Reflected()).Generate(5);

        baskets.ShouldAllBe(b => b.Hidden.Count == 0 && b.Pending.Count == 0 && b.Missing == null && b.Fixed.Count == 0);
    }

    [Test]
    public void The_collections_are_drawn_last_so_the_rest_of_the_data_is_that_of_a_type_without_them()
    {
        var layouts = Fake.For<Layout>().Seed(2).Generate(10);
        var plain = Fake.For<PlainName>().Seed(2).Generate(10);

        layouts.Select(l => l.Name).ShouldBe(plain.Select(p => p.Name));
        layouts.ShouldAllBe(l => l.Early.Count > 0 && l.Late.Count > 0);
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data()
    {
        string Describe(Basket b) =>
            $"{b.Owner}|{string.Join(",", b.Items)}|{string.Join(",", b.Lines.Select(l => l.Sku + l.Quantity))}|{string.Join(",", b.Tags.Order())}|{string.Join(",", b.Counts.OrderBy(p => p.Key).Select(p => p.Key + p.Value))}|" +
            $"{string.Join(",", b.Notes.OrderBy(p => p.Key).Select(p => p.Key + p.Value))}|{string.Join(",", b.Numbers)}|{string.Join(",", b.Fields)}|{string.Join(",", b.Watched)}|{string.Join(",", b.Long)}";

        Reflected().Generate(15).Select(Describe).ShouldBe(Generated().Generate(15).Select(Describe));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Sizes_and_annotations_apply(bool generated)
    {
        var generator = generated ? Generated() : Reflected();

        generator.CollectionSize(2, 2).Generate(5).ShouldAllBe(b => b.Items.Count == 2 && b.Lines.Count == 2 && b.Counts.Count == 2);
        generator.Generate(5).ShouldAllBe(b => b.Long.Count >= 4);
        generator.Invalid(b => b.Long).Generate(5).ShouldAllBe(b => b.Long.Count == 3);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Rules_apply_to_them_like_to_any_member(bool generated)
    {
        var generator = generated ? Generated() : Reflected();

        generator.With(b => b.Items, new List<string> { "a", "b" }).Generate(3).ShouldAllBe(b => b.Items.SequenceEqual(AB));
        generator.With(b => b.Tags, f => new HashSet<string> { "x" }).Generate(3).ShouldAllBe(b => b.Tags.Count == 1 && b.Tags.Contains("x"));
        generator.Ignore(b => b.Items).Ignore(b => b.Lines).Generate(3).ShouldAllBe(b => b.Items.Count == 0 && b.Lines.Count == 0 && b.Tags.Count > 0);
        generator.With(b => b.Lines.Each().Sku, "SKU").Generate(3).SelectMany(b => b.Lines).ShouldAllBe(l => l.Sku == "SKU");
    }

    [Test]
    public void A_rule_that_depends_on_the_object_cannot_replace_a_collection_that_is_only_filled()
    {
#pragma warning disable PHONEY007 // the analyzer reports this at compile time; the test checks what happens without it
        var generator = Make().With(b => b.Items, (f, b) => new List<string> { b.Owner });
#pragma warning restore PHONEY007

        Should.Throw<InvalidOperationException>(() => generator.Generate()).Message.ShouldContain("Items");
    }

    [Test]
    public void Objects_made_by_CreateWith_are_filled_by_the_rules_for_the_collections()
    {
        var baskets = Make().CreateWith(_ => new Basket()).With(b => b.Items, new List<string> { "only" }).Generate(2);

        baskets.ShouldAllBe(b => b.Items.SequenceEqual(Only));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Populate_fills_empty_collections_keeps_the_others_and_All_replaces_them(bool generated)
    {
        var generator = generated ? Generated() : Reflected();
        Basket Existing()
        {
            var basket = new Basket { Owner = "kept" };
            basket.Items.Add("mine");
            return basket;
        }

        var missing = generator.Populate(Existing());
        var all = generator.Populate(Existing(), PopulateMode.All);

        missing.Owner.ShouldBe("kept");
        missing.Items.ShouldBe(["mine"]);
        missing.Lines.ShouldNotBeEmpty();
        all.Items.ShouldNotContain("mine");
        all.Items.ShouldNotBeEmpty();
    }

    [Test]
    public void Members_that_are_filled_in_place_are_part_of_the_model_but_not_settable()
    {
        var members = ModelRegistry.For<Basket>().Members;

        members.Where(m => m.FillsExisting).Select(m => m.Name).ShouldBe(["Fields", "Items", "Lines", "Tags", "Counts", "Notes", "Numbers", "Watched", "Long", "Missing", "Fixed"]); // fields first, then properties
        members.Where(m => m.FillsExisting).ShouldAllBe(m => !m.IsSettable && !m.IsConstructorParameter);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Back_references_reach_through_collections_that_are_filled_in_place(bool generated)
    {
        var generator = generated ? BasketFakes.Volume : new ObjectGenerator<Volume>(new ReflectionModel<Volume>());

        var volumes = generator.Seed(3).WithBackReferences().Generate(5);

        volumes.SelectMany(v => v.Sections).ShouldNotBeEmpty();
        volumes.ShouldAllBe(v => v.Sections.All(s => ReferenceEquals(s.Volume, v)));
    }
}
