using System.Diagnostics;
using Phoney.Generation;

namespace Phoney.Tests.Adversarial;

public sealed class ExplodingConstructor
{
    public ExplodingConstructor() => throw new InvalidTimeZoneException("constructor");

    public string Name { get; set; } = "";
}

public sealed class ExplodingSetter
{
    private readonly string _fuse = "";

    public string Name { get; set; } = "";

    public string Fuse
    {
        get => _fuse;
        set => throw new InvalidTimeZoneException("setter");
    }
}

public sealed class Ouroboros
{
    public string Name { get; set; } = "";
    public Ouroboros? Tail { get; set; }
    public List<Ouroboros> Children { get; set; } = [];
}

public sealed class Yin
{
    public string Name { get; set; } = "";
    public Yang? Yang { get; set; }
}

public sealed class Yang
{
    public string Name { get; set; } = "";
    public Yin? Yin { get; set; }
}

public enum Nothing;

public enum Single
{
    Only,
}

public enum Aliased
{
    First = 1,
#pragma warning disable CA1069 // the point of this enum: two names for one value
    Alias = 1,
#pragma warning restore CA1069
    Second = 2,
}

public sealed class Scarce
{
    public HashSet<bool> Flags { get; set; } = [];
    public Dictionary<bool, int> ByFlag { get; set; } = [];
    public Dictionary<Single, string> BySingle { get; set; } = [];
    public HashSet<byte> Bytes { get; set; } = [];
    public HashSet<Nothing> Nothings { get; set; } = [];
    public Nothing None { get; set; }
    public Aliased Aliased { get; set; }
}

public sealed class Matryoshka<T>
{
    public T? Inner { get; set; }
    public string Label { get; set; } = "";
}

public sealed class Odd
{
    public required string Required { get; set; }
    public string InitOnly { get; init; } = "";
    public int this[int index]
    {
        get => index;
        set { }
    }

    public static string Untouched { get; set; } = "untouched";
    public object? Anything { get; set; }
    public Action? Callback { get; set; }
    public Type? Type { get; set; }
    public Exception? Error { get; set; }
    public Task? Pending { get; set; }
    public Lazy<int>? Later { get; set; }
    public int[,,]? Cube { get; set; }
    public (int, int, int, int, int, int, int, int, int) Nine { get; set; }
}

[FakeFor<ExplodingConstructor>]
[FakeFor<ExplodingSetter>]
[FakeFor<Ouroboros>]
[FakeFor<Yin>]
[FakeFor<Yang>]
[FakeFor<Scarce>]
[FakeFor<Matryoshka<Matryoshka<Matryoshka<Matryoshka<int>>>>>]
[FakeFor<Odd>]
public static partial class HostileGraphFakes;

/// <summary>
/// Object graphs built to break generation, each in both models: user code that throws, types that contain themselves, collections asking for more distinct
/// values than a type has, and members that are not data at all.
/// </summary>
public sealed class HostileGraphTests
{
    private static IEnumerable<(string Model, ObjectGenerator<T> Generator)> Both<T>(ObjectGenerator<T> generated) =>
        [("generated", generated), ("reflection", new ObjectGenerator<T>(new ReflectionModel<T>()))];

    [Test]
    public void An_exception_from_a_constructor_reaches_the_caller_unwrapped_in_both_models()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.ExplodingConstructor))
            Should.Throw<InvalidTimeZoneException>(() => generator.Generate(), model).Message.ShouldBe("constructor");
    }

    [Test]
    public void An_exception_from_a_setter_reaches_the_caller_unwrapped_in_both_models()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.ExplodingSetter))
        {
            Should.Throw<InvalidTimeZoneException>(() => generator.Generate(), model).Message.ShouldBe("setter");
            Should.Throw<InvalidTimeZoneException>(() => generator.Populate(new ExplodingSetter()), model);
        }
    }

    [Test]
    public void Exceptions_from_rules_callbacks_and_conditions_reach_the_caller_unwrapped()
    {
        static bool Explode() => throw new InvalidTimeZoneException("condition");

        foreach (var (model, generator) in Both(HostileGraphFakes.Yin))
        {
            Should.Throw<InvalidTimeZoneException>(() => generator.With(x => x.Name, _ => throw new InvalidTimeZoneException("rule")).Generate(), model);
            Should.Throw<InvalidTimeZoneException>(() => generator.AfterCreate((_, _) => throw new InvalidTimeZoneException("after")).Generate(), model);
            Should.Throw<InvalidTimeZoneException>(() => generator.Where(_ => Explode()).Generate(), model);
            Should.Throw<InvalidTimeZoneException>(() => generator.Where(x => x.Name, _ => Explode()).Generate(), model);
        }
    }

    [Test]
    public void A_generator_still_works_after_user_code_threw_during_its_first_run()
    {
        var calls = 0;
        var generator = HostileGraphFakes.Yin.Seed(3).With(x => x.Name, _ => ++calls == 1 ? throw new InvalidTimeZoneException("once") : "fine");

        Should.Throw<InvalidTimeZoneException>(() => generator.Generate());

        generator.Generate(3).ShouldAllBe(y => y.Name == "fine");
    }

    [Test]
    public void Types_that_contain_themselves_end_in_both_models_whatever_the_depth_limit()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.Ouroboros))
        {
            var watch = Stopwatch.StartNew();
            var snake = generator.MaxDepth(int.MaxValue).CollectionSize(5, 5).Seed(1).Generate();

            snake.Tail.ShouldBeNull(model); // a type never contains itself, at any depth
            snake.Children.ShouldBeEmpty(model);
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public void Types_that_contain_each_other_are_cut_where_the_cycle_closes()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.Yin))
        {
            var yin = generator.MaxDepth(int.MaxValue).Seed(1).Generate();

            yin.Yang.ShouldNotBeNull(model);
            yin.Yang.Yin.ShouldBeNull(model);
        }
    }

    [Test]
    public void Populating_an_existing_cycle_fills_it_without_walking_round_forever()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.Ouroboros))
        {
            var snake = new Ouroboros();
            snake.Tail = snake;
            snake.Children.Add(snake);

            generator.Seed(1).Populate(snake).Name.ShouldNotBeNullOrWhiteSpace(model);
            snake.Tail.ShouldBeSameAs(snake);
            generator.WithBackReferences().Seed(1).Populate(snake).Tail.ShouldBeSameAs(snake, model); // kept: it is set
            generator.WithBackReferences().Seed(1).Populate(snake, PopulateMode.All).Tail.ShouldBeNull(model); // replaced: a type never contains itself, and the root has no ancestor
        }

        foreach (var (model, generator) in Both(HostileGraphFakes.Yin))
        {
            var yin = new Yin { Yang = new Yang() };
            yin.Yang.Yin = yin;

            generator.Seed(1).Populate(yin).Yang!.Name.ShouldNotBeNullOrWhiteSpace(model);
            yin.Yang.Yin.ShouldBeSameAs(yin);
        }
    }

    [Test]
    public void Collections_of_types_with_few_values_hold_what_exists_and_finish_quickly()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.Scarce))
        {
            var watch = Stopwatch.StartNew();
            var scarce = generator.CollectionSize(1_000, 1_000).Seed(1).Generate();

            scarce.Flags.Count.ShouldBe(2, model);
            scarce.ByFlag.Count.ShouldBe(2, model);
            scarce.BySingle.Keys.ToList().ShouldBe([Single.Only], model);
            scarce.Bytes.Count.ShouldBeLessThanOrEqualTo(256, model);
            scarce.Nothings.ShouldBe([default(Nothing)], model);
            scarce.None.ShouldBe(default, model);
            Enum.IsDefined(scarce.Aliased).ShouldBeTrue(model);
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public void Both_models_give_the_same_scarce_collections()
    {
        var (generated, reflected) = (Both(HostileGraphFakes.Scarce).First().Generator, Both(HostileGraphFakes.Scarce).Last().Generator);

        EveryLocaleTests.Describe(reflected.CollectionSize(50, 50).Seed(4).Generate(5))
            .ShouldBe(EveryLocaleTests.Describe(generated.CollectionSize(50, 50).Seed(4).Generate(5)));
    }

    [Test]
    public void A_unique_member_with_fewer_values_than_objects_says_which_member_ran_out()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.Scarce))
        {
            Should.Throw<InvalidOperationException>(() => generator.Unique(x => x.Aliased).Generate(3), model).Message.ShouldContain("Scarce.Aliased");
            Should.Throw<InvalidOperationException>(() => generator.Unique(x => x.None, maxAttempts: 5).Generate(2), model).Message.ShouldContain("Scarce.None");
        }
    }

    [Test]
    public void A_condition_nothing_satisfies_gives_up_with_a_message_instead_of_looping()
    {
        var watch = Stopwatch.StartNew();

        Should.Throw<InvalidOperationException>(() => HostileGraphFakes.Yin.Where(_ => false).Generate()).Message.ShouldContain("Where");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void Generic_types_nested_five_deep_are_generated_in_both_models()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.Matryoshka))
        {
            var doll = generator.Seed(2).Generate();

            doll.Inner!.Inner!.Inner!.Label.ShouldNotBeNullOrWhiteSpace(model);
        }
    }

    [Test]
    public void Members_that_are_not_data_are_left_empty_and_the_rest_filled_in_both_models()
    {
        foreach (var (model, generator) in Both(HostileGraphFakes.Odd))
        {
            var odd = generator.Seed(5).Generate();

            odd.Required.ShouldNotBeNullOrWhiteSpace(model);
            odd.InitOnly.ShouldNotBeNullOrWhiteSpace(model);
            (odd.Anything, odd.Callback, odd.Type, odd.Error, odd.Pending, odd.Later, odd.Cube).ShouldBe((null, null, null, null, null, null, null), model);
            odd.Nine.ShouldBe(default, model); // tuples are built up to seven parts
            Odd.Untouched.ShouldBe("untouched", model);
        }

        EveryLocaleTests.Describe(new ObjectGenerator<Odd>(new ReflectionModel<Odd>()).Seed(5).Generate(3))
            .ShouldBe(EveryLocaleTests.Describe(HostileGraphFakes.Odd.Seed(5).Generate(3)));
    }

    [Test]
    public void Bad_sizes_counts_and_probabilities_are_refused_when_the_rule_is_added()
    {
        // Variables, because PHONEY011 already reports these as constants at compile time; the runtime checks are for computed values.
        var (minusOne, one, five, tooLikely) = (-1, 1, 5, 1.5);
        var generator = HostileGraphFakes.Yin;

        Should.Throw<ArgumentOutOfRangeException>(() => generator.CollectionSize(minusOne, 3)).ParamName.ShouldBe("min");
        Should.Throw<ArgumentOutOfRangeException>(() => generator.CollectionSize(five, one)).ParamName.ShouldBe("max");
        Should.Throw<ArgumentOutOfRangeException>(() => generator.MaxDepth(minusOne)).ParamName.ShouldBe("depth");
        Should.Throw<ArgumentOutOfRangeException>(() => generator.NullProbability(tooLikely - tooLikely + double.NaN)).ParamName.ShouldBe("probability");
        Should.Throw<ArgumentOutOfRangeException>(() => generator.NullProbability(tooLikely)).ParamName.ShouldBe("probability");
        Should.Throw<ArgumentOutOfRangeException>(() => generator.Generate(minusOne)).ParamName.ShouldBe("count");
        Should.Throw<ArgumentOutOfRangeException>(() => generator.GenerateParallel(minusOne)).ParamName.ShouldBe("count");
        generator.Generate(0).ShouldBeEmpty();
    }

    [Test]
    public void Keys_of_any_length_give_an_object_and_a_null_key_is_refused()
    {
        var generator = HostileGraphFakes.Yin.Seed(1);

        generator.GenerateFor("").Name.ShouldNotBeNullOrWhiteSpace();
        generator.GenerateFor(new string('x', 1_000_000)).Name.ShouldNotBeNullOrWhiteSpace();
        generator.GenerateFor("😀\0￿").Name.ShouldBe(generator.GenerateFor("😀\0￿").Name);
        Should.Throw<ArgumentNullException>(() => generator.GenerateFor(null!));
    }
}
