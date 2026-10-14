using Phoney.Generation;

namespace Phoney.Tests;

// Types only these tests use: Fake.Generators is process-wide, so no other test may generate them while it is changed.
public sealed class Manifest
{
    public string Title { get; set; } = "";
    public Item Main { get; set; } = null!;
    public List<Item> Items { get; set; } = [];
    public IShape? Badge { get; set; }
}

public sealed class Item
{
    public string Sku { get; set; } = "";
    public int Count { get; set; }
}

[FakeFor<Manifest>]
public static partial class ManifestFakes;

/// <summary>App-wide type generators, registered once for every generator compiled afterwards.</summary>
[NotInParallel]
public sealed class FakeGeneratorsTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Item> Sku(string sku) => Fake.For<Item>().With(i => i.Sku, sku);

    private static void With(Action<FakeGenerators> register, Action test)
    {
        try
        {
            register(Fake.Generators);
            test();
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }

    [Test]
    public void A_registered_generator_creates_the_type_wherever_it_is_nested() =>
        With(g => g.Add(Sku("APP")), () =>
        {
            var manifests = Fake.For<Manifest>().Seed(1).Generate(10);

            manifests.ShouldAllBe(m => m.Main.Sku == "APP");
            manifests.SelectMany(m => m.Items).ShouldNotBeEmpty();
            manifests.SelectMany(m => m.Items).ShouldAllBe(i => i.Sku == "APP");
        });

    [Test]
    public void Without_a_registration_nothing_changes() =>
        Fake.For<Manifest>().Seed(1).Generate(5).ShouldAllBe(m => m.Main.Sku != "APP");

    [Test]
    public void Remove_and_Clear_undo_a_registration()
    {
        With(g => g.Add(Sku("APP")), () => { });
        Fake.For<Manifest>().Generate().Main.Sku.ShouldNotBe("APP"); // With cleared it

        With(g => { g.Add(Sku("APP")); g.Remove<Item>(); }, () =>
            Fake.For<Manifest>().Generate().Main.Sku.ShouldNotBe("APP"));
    }

    [Test]
    public void A_later_registration_for_the_same_type_replaces_the_earlier_one() =>
        With(g => { g.Add(Sku("FIRST")); g.Add(Sku("SECOND")); }, () =>
            Fake.For<Manifest>().Generate().Main.Sku.ShouldBe("SECOND"));

    [Test]
    public void A_generators_own_registration_wins_over_the_app_wide_one() =>
        With(g => g.Add(Sku("APP")), () =>
        {
            var manifest = Fake.For<Manifest>().Use(Sku("OWN")).Generate();

            manifest.Main.Sku.ShouldBe("OWN");
            manifest.Items.ShouldAllBe(i => i.Sku == "OWN");
        });

    [Test]
    public void A_rule_for_the_member_wins_over_the_app_wide_generator() =>
        With(g => g.Add(Sku("APP")), () =>
        {
            var manifest = Fake.For<Manifest>().With(m => m.Main, Sku("MEMBER")).Generate();

            manifest.Main.Sku.ShouldBe("MEMBER");
            manifest.Items.ShouldAllBe(i => i.Sku == "APP");
        });

    [Test]
    public void The_generated_object_itself_is_never_replaced() =>
        With(g => g.Add(Sku("APP")), () => Fake.For<Item>().Generate().Sku.ShouldNotBe("APP"));

    [Test]
    public void Generators_that_are_already_compiled_keep_what_they_had()
    {
        var generator = Fake.For<Manifest>().Seed(1);
        generator.Generate().Main.Sku.ShouldNotBe("APP"); // compiles on first use

        With(g => g.Add(Sku("APP")), () =>
        {
            generator.Generate().Main.Sku.ShouldNotBe("APP"); // like Fake.Subtypes: applies to generators compiled afterwards
            generator.Seed(1).Generate().Main.Sku.ShouldBe("APP"); // a derived generator compiles again
        });
    }

    [Test]
    public void An_interface_can_use_the_generator_of_a_subtype() =>
        With(g => g.Add<IShape, Circle>(Fake.For<Circle>().With(c => c.Radius, 4.5)), () =>
            Fake.For<Manifest>().Generate().Badge.ShouldBeOfType<Circle>().Radius.ShouldBe(4.5));

    [Test]
    public void Arguments_use_app_wide_generators_too() =>
        With(g => g.Add(Sku("APP")), () =>
            ((Item)new ArgumentGenerator([new FakeArgument("item", typeof(Item))]).Seed(1).Row(0)[0]!).Sku.ShouldBe("APP"));

    [Test]
    public void Generated_and_reflection_models_produce_identical_data() =>
        With(g => g.Add(Fake.Generated<Item>().With(i => i.Sku, f => "APP-" + f.Random.Int(100, 999))), () =>
        {
            var generated = ManifestFakes.Manifest.Seed(5).ReferenceDate(Reference).Generate(15);
            var reflected = new ObjectGenerator<Manifest>(new ReflectionModel<Manifest>()).Seed(5).ReferenceDate(Reference).Generate(15);

            generated.ShouldAllBe(m => m.Main.Sku.StartsWith("APP-"));
            reflected.Select(Describe).ShouldBe(generated.Select(Describe));
        });

    [Test]
    public void Invalid_registrations_are_rejected_with_a_helpful_message()
    {
        Should.Throw<ArgumentNullException>(() => Fake.Generators.Add<Item>(null!));
        Should.Throw<ArgumentNullException>(() => Fake.Generators.Add<IShape, Circle>(null!));
        Should.Throw<ArgumentException>(() => Fake.Generators.Add(Fake.For<int>())).Message.ShouldContain("not a nested object");
        Fake.Generators.Clear();
    }

    private static string Describe(Manifest m) =>
        $"{m.Title}|{m.Main.Sku}|{m.Main.Count}|{string.Join(",", m.Items.Select(i => i.Sku + i.Count))}";
}
