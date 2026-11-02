using Phoney.Generation;

namespace Phoney.Tests;

// Types only these tests use: Fake.Defaults is process-wide, so no other test may depend on them while it is changed.
public sealed class Kennel
{
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? Nickname { get; set; }
    public Hound Lead { get; set; } = null!;
    public List<Hound> Hounds { get; set; } = [];
}

public sealed class Hound
{
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Kennel? Home { get; set; }
}

public sealed record Leash(string Color, int Length = 7);

public sealed class Oddity
{
    public string Zork { get; set; } = "";
}

[FakeFor<Kennel>]
public static partial class KennelFakes;

/// <summary>
/// <c>Fake.Defaults</c> fills the generation settings nothing more specific set: a generator's own setting, a <c>Configure</c> registration and an attribute
/// option win over it, and it wins over the built-in defaults, at the root, in nested objects and for parameters, through both models.
/// </summary>
[NotInParallel]
public sealed class GenerationDefaultsTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static void WithDefaults(Action<FakeDefaults> set, Action test)
    {
        try
        {
            set(Fake.Defaults);
            test();
        }
        finally
        {
            Fake.Defaults.Reset();
            Fake.Generators.Clear();
        }
    }

    [Test]
    public void The_built_in_defaults_are_unchanged()
    {
        var defaults = Fake.Defaults;
        defaults.NullProbability.ShouldBe(0);
        defaults.CollectionSize.ShouldBe((1, 3));
        defaults.MaxDepth.ShouldBe(4);
        defaults.Strict.ShouldBeFalse();
        defaults.BackReferences.ShouldBeFalse();
        defaults.CausalTimelines.ShouldBeFalse();
        defaults.ParameterDefaults.ShouldBeFalse();
        defaults.NameConventions.ShouldBeTrue();
    }

    [Test]
    public void Without_a_change_seeded_data_is_the_same_as_before() =>
        KennelFakes.Kennel.Seed(3).ReferenceDate(Reference).Generate(5).Select(Describe)
            .ShouldBe(new ObjectGenerator<Kennel>(new ReflectionModel<Kennel>()).Seed(3).ReferenceDate(Reference).Generate(5).Select(Describe));

    [Test]
    public void NullProbability_reaches_the_facade() =>
        WithDefaults(d => d.NullProbability = 1, () => Fake.Many<Kennel>(5).ShouldAllBe(k => k.Nickname == null));

    [Test]
    public void CollectionSize_reaches_nested_collections() =>
        WithDefaults(d => d.CollectionSize = (4, 4), () => Fake.One<Kennel>().Hounds.Count.ShouldBe(4));

    [Test]
    public void MaxDepth_reaches_nested_objects() =>
        WithDefaults(d => d.MaxDepth = 0, () => Fake.One<Kennel>().Lead.ShouldBeNull());

    [Test]
    public void Strict_reaches_the_facade() =>
        WithDefaults(d => d.Strict = true, () => Should.Throw<InvalidOperationException>(() => Fake.One<Oddity>()).Message.ShouldContain("Zork"));

    [Test]
    public void BackReferences_reach_the_facade() =>
        WithDefaults(d => d.BackReferences = true, () =>
        {
            var kennel = Fake.For<Kennel>().With(k => k.Lead.Home, (Kennel?)null).Generate();
            kennel.Lead.Home.ShouldBeSameAs(kennel);
        });

    [Test]
    public void CausalTimelines_reach_the_facade() =>
        WithDefaults(d => d.CausalTimelines = true, () =>
            Fake.For<Kennel>().Seed(1).Generate(20).ShouldAllBe(k => k.Hounds.All(h => h.CreatedAt >= k.CreatedAt)));

    [Test]
    public void ParameterDefaults_reach_the_facade() =>
        WithDefaults(d => d.ParameterDefaults = true, () => Fake.Many<Leash>(5).ShouldAllBe(l => l.Length == 7));

    [Test]
    public void NameConventions_reach_the_facade() =>
        WithDefaults(d => d.NameConventions = false, () =>
            Fake.For<Kennel>().Seed(2).Generate().Lead.Name.ShouldBe(Fake.For<Kennel>().Seed(2).WithNameConventions(false).Generate().Lead.Name));

    [Test]
    public void A_setting_on_the_generator_wins_over_the_defaults()
    {
        WithDefaults(d => d.CollectionSize = (4, 4), () => Fake.For<Kennel>().CollectionSize(2, 2).Generate().Hounds.Count.ShouldBe(2));
        WithDefaults(d => d.Strict = true, () => Fake.For<Oddity>().Strict(false).Generate().Zork.ShouldNotBeNull());
        WithDefaults(d => d.NullProbability = 1, () => Fake.For<Kennel>().NullProbability(0).Generate().Nickname.ShouldNotBeNull());
    }

    [Test]
    public void A_configured_type_wins_over_the_defaults() =>
        WithDefaults(d =>
        {
            d.CollectionSize = (4, 4);
            Fake.Generators.Configure<Kennel>(g => g.CollectionSize(1, 1));
        }, () => Fake.One<Kennel>().Hounds.Count.ShouldBe(1));

    [Test]
    public void A_change_refreshes_the_facade_but_not_a_generator_already_used()
    {
        var used = Fake.For<Kennel>();
        used.Generate();

        WithDefaults(d => d.CollectionSize = (5, 5), () =>
        {
            Fake.One<Kennel>().Hounds.Count.ShouldBe(5);
            used.Generate().Hounds.Count.ShouldBeLessThanOrEqualTo(3); // compiled before the change, like conventions and subtypes
        });
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data() =>
        WithDefaults(d =>
        {
            d.CollectionSize = (2, 5);
            d.NullProbability = 0.5;
            d.CausalTimelines = true;
        }, () =>
        {
            var generated = KennelFakes.Kennel.Seed(5).ReferenceDate(Reference).Generate(15);
            var reflected = new ObjectGenerator<Kennel>(new ReflectionModel<Kennel>()).Seed(5).ReferenceDate(Reference).Generate(15);

            generated.Select(k => k.Hounds.Count).ShouldAllBe(n => n >= 2);
            reflected.Select(Describe).ShouldBe(generated.Select(Describe));
        });

    [Test]
    public void Parameters_take_the_defaults_and_their_own_settings_win() =>
        WithDefaults(d =>
        {
            d.CollectionSize = (4, 4);
            d.NullProbability = 1;
        }, () =>
        {
            var row = Arguments().Seed(1).Row(0);
            row[0].ShouldBeOfType<Kennel>().Hounds.Count.ShouldBe(4);
            row[1].ShouldBeNull();
            Arguments().NullProbability(0).Seed(1).Row(0)[1].ShouldNotBeNull();
        });

    [Test]
    public void Attribute_options_fall_back_to_the_defaults_unless_set() =>
        WithDefaults(d => d.NullProbability = 1, () =>
        {
            Row(new FakeDataOptions())[1].ShouldBeNull();
            Row(new FakeDataOptions { NullProbability = 0 })[1].ShouldNotBeNull();
            Check(new FakeCheckOptions())[1].ShouldBeNull();
        });

    [Test]
    public void Invalid_defaults_are_refused_like_the_generator_methods()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Defaults.NullProbability = 1.5);
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Defaults.NullProbability = double.NaN);
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Defaults.CollectionSize = (3, 1));
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Defaults.CollectionSize = (-1, 2));
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Defaults.MaxDepth = -1);
        Fake.Defaults.CollectionSize.ShouldBe((1, 3));
    }

    private static readonly FakeArgument[] Parameters = [new("kennel", typeof(Kennel)), new("nickname", typeof(string), Nullable: true)];

    private static ArgumentGenerator Arguments() => new(Parameters);

    private static object?[] Row(FakeDataOptions options) => options.CreateGenerator(Parameters, "Defaults.Test", environmentSeed: null).Row(0);

    private static object?[] Check(FakeCheckOptions options)
    {
        object?[]? row = null;
        options.CreateGenerator(Parameters).Check(r => { row ??= r; return true; }, count: 1);
        return row!;
    }

    private static string Describe(Kennel k) =>
        $"{k.Title}|{k.CreatedAt:O}|{k.Nickname}|{k.Lead?.Name}|{string.Join(",", k.Hounds.Select(h => h.Name + h.CreatedAt.ToString("O")))}";
}
