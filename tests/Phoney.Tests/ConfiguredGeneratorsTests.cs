using Phoney.Generation;

namespace Phoney.Tests;

// Types only these tests use: Fake.Generators is process-wide, so no other test may generate them while it is changed.
public sealed class Hamper
{
    public string Title { get; set; } = "";
    public Part Main { get; set; } = null!;
    public List<Part> Parts { get; set; } = [];
}

public sealed class Part
{
    public string Code { get; set; } = "";
    public int Count { get; set; }
    public string Language { get; set; } = "";
    public DateTimeOffset Now { get; set; }
}

public sealed class LoosePart
{
    public string Code { get; set; } = "";
}

[FakeFor<Hamper>]
public static partial class HamperFakes;

/// <summary>
/// A type-wide generator registered with <c>Fake.Generators.Configure</c> is used for every object of its type: at the root of the facade
/// (<c>One</c>, <c>Many</c>, <c>For</c>, <c>ForGenerated</c>, <c>Populate</c>), nested, and for parameters.
/// </summary>
[NotInParallel]
public sealed class ConfiguredGeneratorsTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static void Configured(Action test, Func<ObjectGenerator<Part>, ObjectGenerator<Part>>? configure = null)
    {
        try
        {
            Fake.Generators.Configure(configure ?? (g => g.With(p => p.Code, "CFG")));
            test();
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }

    public static IEnumerable<Func<(string EntryPoint, Func<Part> Make)>> EntryPoints()
    {
        yield return () => ("Fake.One", Fake.One<Part>);
        yield return () => ("Fake.One(Type)", () => (Part)Fake.One(typeof(Part)));
        yield return () => ("Fake.Many", () => Fake.Many<Part>(3)[2]);
        yield return () => ("Fake.Many(min, max)", () => Fake.Many<Part>(2, 4)[1]);
        yield return () => ("Fake.For", () => Fake.For<Part>().Generate());
        yield return () => ("Fake.ForGenerated", () => Fake.ForGenerated<Part>().Generate());
        yield return () => ("Fake.Populate", () => Fake.Populate(new Part()));
        yield return () => ("nested member", () => Fake.One<Hamper>().Main);
        yield return () => ("collection element", () => Fake.For<Hamper>().CollectionSize(1, 1).Generate().Parts[0]);
        yield return () => ("generated nested member", () => HamperFakes.Hamper.Generate().Main);
        yield return () => ("reflection nested member", () => new ObjectGenerator<Hamper>(new ReflectionModel<Hamper>()).Generate().Main);
        yield return () => ("parameter", () => (Part)new ArgumentGenerator([new FakeArgument("part", typeof(Part))]).Seed(1).Row(0)[0]!);
    }

    [Test]
    [MethodDataSource(nameof(EntryPoints))]
    public void Every_entry_point_uses_the_configured_generator(string entryPoint, Func<Part> make) =>
        Configured(() => make().Code.ShouldBe("CFG", entryPoint));

    [Test]
    [MethodDataSource(nameof(EntryPoints))]
    public void Without_a_configuration_nothing_changes(string entryPoint, Func<Part> make) =>
        make().Code.ShouldNotBe("CFG", entryPoint);

    [Test]
    public void Rules_at_the_call_site_come_after_the_configured_ones() =>
        Configured(() =>
        {
            var part = Fake.For<Part>().With(p => p.Code, "OWN").Generate();

            part.Code.ShouldBe("OWN");
            part.Count.ShouldBe(7);
        }, g => g.With(p => p.Code, "CFG").With(p => p.Count, 7));

    [Test]
    public void A_seeded_facade_seeds_configured_objects_reproducibly() =>
        Configured(() =>
        {
            Part First()
            {
                using var _ = Fake.Seeded(42);
                return Fake.One<Part>();
            }

            var (a, b) = (First(), First());
            a.Code.ShouldStartWith("CFG-");
            (a.Code, a.Count, a.Now).ShouldBe((b.Code, b.Count, b.Now));
        }, g => g.With(p => p.Code, f => "CFG-" + f.Random.Int(1000, 9999)));

    [Test]
    public void The_configured_locale_and_reference_date_win_over_the_facade() =>
        Configured(() =>
        {
            using var _ = Fake.Seeded(1, "de");

            var part = Fake.One<Part>();
            part.Language.ShouldBe("sv");
            part.Now.ShouldBe(Reference);
        }, g => g.Locale("sv").ReferenceDate(Reference).With(p => p.Language, f => f.Locale).With(p => p.Now, f => f.ReferenceDate));

    [Test]
    public void The_facade_locale_applies_when_the_configuration_sets_none() =>
        Configured(() =>
        {
            using var _ = Fake.Seeded(1, "de");

            Fake.One<Part>().Language.ShouldBe("de");
        }, g => g.With(p => p.Language, f => f.Locale));

    [Test]
    public void Each_facade_call_starts_with_fresh_unique_values() =>
        Configured(() =>
        {
            Fake.One<Part>().Code.ShouldBe("ONLY");
            Fake.One<Part>().Code.ShouldBe("ONLY"); // a shared set would have run out of values
        }, g => g.WithOneOf(p => p.Code, ["ONLY"]).Unique(p => p.Code, maxAttempts: 3));

    [Test]
    public void The_delegate_runs_on_the_plain_generator_so_configuring_twice_does_not_stack() =>
        Configured(() =>
        {
            Fake.Generators.Configure<Part>(g => g.With(p => p.Count, 3));

            var part = Fake.One<Part>();
            part.Count.ShouldBe(3);
            part.Code.ShouldNotBe("CFG");
        });

    [Test]
    public void Configure_and_Add_share_one_slot_per_type_and_the_last_one_wins()
    {
        Configured(() =>
        {
            Fake.Generators.Add(Fake.For<Part>().With(p => p.Code, "ADD"));

            Fake.One<Hamper>().Main.Code.ShouldBe("ADD");
            Fake.One<Part>().Code.ShouldNotBe("ADD"); // Add keeps its meaning: nested objects only
            Fake.One<Part>().Code.ShouldNotBe("CFG");
        });

        try
        {
            Fake.Generators.Add(Fake.For<Part>().With(p => p.Code, "ADD"));
            Fake.Generators.Configure<Part>(g => g.With(p => p.Code, "CFG"));

            Fake.One<Part>().Code.ShouldBe("CFG");
            Fake.One<Hamper>().Main.Code.ShouldBe("CFG");
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }

    [Test]
    public void Remove_undoes_a_configuration() =>
        Configured(() =>
        {
            Fake.One<Part>().Code.ShouldBe("CFG");
            Fake.Generators.Remove<Part>();
            Fake.One<Part>().Code.ShouldNotBe("CFG");
            Fake.One<Hamper>().Main.Code.ShouldNotBe("CFG");
        });

    [Test]
    public void A_type_without_a_generated_model_is_configured_through_reflection()
    {
        try
        {
            Fake.Generators.Configure<LoosePart>(g => g.With(p => p.Code, "LOOSE"));

            Fake.One<LoosePart>().Code.ShouldBe("LOOSE");
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data() =>
        Configured(() =>
        {
            var generated = HamperFakes.Hamper.Seed(5).ReferenceDate(Reference).Generate(15);
            var reflected = new ObjectGenerator<Hamper>(new ReflectionModel<Hamper>()).Seed(5).ReferenceDate(Reference).Generate(15);

            generated.ShouldAllBe(c => c.Main.Code.StartsWith("CFG-"));
            reflected.Select(Describe).ShouldBe(generated.Select(Describe));
        }, g => g.With(p => p.Code, f => "CFG-" + f.Random.Int(100, 999)));

    [Test]
    public void Types_without_a_model_of_their_own_are_refused_at_registration()
    {
        Should.Throw<ArgumentNullException>(() => Fake.Generators.Configure<Part>(null!));
        Should.Throw<ArgumentException>(() => Fake.Generators.Configure<int>(g => g)).Message.ShouldContain("not a nested object");
        Should.Throw<ArgumentException>(() => Fake.Generators.Configure<IShape>(g => g)).Message.ShouldContain("Add<IShape, ");
    }

    [Test]
    [Arguments("Sequence")]
    [Arguments("Seed")]
    [Arguments("null")]
    public void Configurations_that_would_misbehave_are_refused_when_applied(string kind) =>
        Configured(() =>
        {
            var error = Should.Throw<InvalidOperationException>(() => Fake.One<Part>());
            error.Message.ShouldContain("Fake.Generators.Configure<Part>");
            Should.Throw<InvalidOperationException>(() => Fake.One<Hamper>()).Message.ShouldBe(error.Message);
        }, kind switch
        {
            "Sequence" => g => g.Sequence(p => p.Count, i => (int)i),
            "Seed" => g => g.Seed(3),
            _ => _ => null!,
        });

    private static string Describe(Hamper c) =>
        $"{c.Title}|{c.Main.Code}|{c.Main.Count}|{string.Join(",", c.Parts.Select(p => p.Code + p.Count))}";
}
