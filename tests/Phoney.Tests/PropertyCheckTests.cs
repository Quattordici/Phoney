using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Cart
{
    public string Customer { get; set; } = "";
    [Range(0, 1000)] public int Discount { get; set; }
    public List<CartLine> Lines { get; set; } = [];
    public CartLine? Featured { get; set; }
}

public sealed class CartLine
{
    public string Sku { get; set; } = "";
    [Range(1, 50)] public int Quantity { get; set; }
    [Range(0.01, 999.99)] public decimal Price { get; set; }
}

public sealed record CheckedPoint(int X, int Y);

public sealed class CheckedCatalog
{
    public Dictionary<string, CartLine> BySku { get; set; } = [];
}

public sealed record CheckedFolder
{
    public string Name { get; set; } = "";
    public CheckedFile? Readme { get; set; }
}

public sealed record CheckedFile
{
    public string Name { get; set; } = "";
    public CheckedFolder? Parent { get; set; }
}

[FakeFor<Cart>]
public static partial class CheckFakes;

/// <summary><c>Check</c> runs a property over generated objects and shrinks a failing object to a simplest one the generator can make.</summary>
public sealed class PropertyCheckTests
{
    private static ObjectGenerator<Cart> Baskets(bool generated = false) =>
        (generated ? CheckFakes.Cart : new ObjectGenerator<Cart>(new ReflectionModel<Cart>())).Seed(42).CollectionSize(0, 8);

    private static FakeCheckException<Cart> Falsify(ObjectGenerator<Cart> generator, Func<Cart, bool> property, int count = 200, int maxShrinks = 2000) =>
        Should.Throw<FakeCheckException<Cart>>(() => generator.Check(property, count, maxShrinks));

    [Test]
    public void A_property_that_holds_for_every_object_passes()
    {
        var seen = 0;
        Baskets().Check(b =>
        {
            seen++;
            return b.Discount is >= 0 and <= 1000;
        });

        seen.ShouldBe(100);
    }

    [Test]
    public void A_failing_property_throws_with_a_counterexample_that_still_fails()
    {
        var failure = Falsify(Baskets(), b => b.Discount < 500);

        failure.Counterexample.Discount.ShouldBeGreaterThanOrEqualTo(500);
        failure.Original.Discount.ShouldBeGreaterThanOrEqualTo(500);
        failure.Seed.ShouldBe(42);
        failure.Tested.ShouldBe(failure.Index + 1);
        failure.Message.ShouldContain("seed 42");
    }

    [Test]
    public void A_number_shrinks_to_the_smallest_value_that_fails()
    {
        var failure = Falsify(Baskets(), b => b.Discount < 500);

        failure.Counterexample.Discount.ShouldBe(500);
        failure.ShrinkSteps.ShouldBeGreaterThan(0);
    }

    [Test]
    public void A_collection_shrinks_to_the_fewest_elements_that_fail_and_its_elements_to_their_simplest()
    {
        var failure = Falsify(Baskets(), b => b.Lines.Count < 3);

        failure.Counterexample.Lines.Count.ShouldBe(3);
        failure.Counterexample.Lines.ShouldAllBe(l => l.Quantity == 1);
        failure.Counterexample.Discount.ShouldBe(0);
    }

    [Test]
    public void Shrinking_never_leaves_the_rules_and_annotations_of_the_generator()
    {
        var failure = Falsify(Baskets().With(b => b.Customer, "ACME"), b => b.Lines.Sum(l => l.Quantity) < 40);

        var shrunk = failure.Counterexample;
        shrunk.Customer.ShouldBe("ACME");
        shrunk.Lines.ShouldAllBe(l => l.Quantity >= 1 && l.Quantity <= 50 && l.Price >= 0.01m && l.Price <= 999.99m);
        shrunk.Lines.Sum(l => l.Quantity).ShouldBe(40);
        shrunk.Lines.Count.ShouldBeLessThanOrEqualTo(failure.Original.Lines.Count);
    }

    [Test]
    public void The_same_seed_gives_the_same_counterexample()
    {
        var first = Falsify(Baskets(), b => b.Lines.Sum(l => l.Price) < 1500);
        var second = Falsify(Baskets(), b => b.Lines.Sum(l => l.Price) < 1500);

        Describe(second.Counterexample).ShouldBe(Describe(first.Counterexample));
        second.Index.ShouldBe(first.Index);
        second.ShrinkSteps.ShouldBe(first.ShrinkSteps);
    }

    [Test]
    public void Generated_and_reflection_models_find_and_shrink_to_the_same_counterexample()
    {
        var reflection = Falsify(Baskets(), b => b.Lines.Sum(l => l.Quantity) < 60);
        var generated = Falsify(Baskets(generated: true), b => b.Lines.Sum(l => l.Quantity) < 60);

        Describe(generated.Original).ShouldBe(Describe(reflection.Original));
        Describe(generated.Counterexample).ShouldBe(Describe(reflection.Counterexample));
    }

    [Test]
    public void The_original_object_is_the_one_Generate_gives_at_that_position()
    {
        var failure = Falsify(Baskets(), b => b.Discount < 900);

        var plain = Baskets().Generate(failure.Index + 1)[^1];
        Describe(failure.Original).ShouldBe(Describe(plain));
    }

    [Test]
    public void An_unseeded_generator_reports_a_seed_that_replays_the_failure()
    {
        var failure = Should.Throw<FakeCheckException<Cart>>(() => Fake.For<Cart>().CollectionSize(0, 8).Check(b => b.Discount < 500, 500));

        var replay = Falsify(Fake.For<Cart>().CollectionSize(0, 8).Seed(failure.Seed), b => b.Discount < 500, 500);
        Describe(replay.Counterexample).ShouldBe(Describe(failure.Counterexample));
        failure.Message.ShouldContain($"PHONEY_SEED={failure.Seed}");
    }

    [Test]
    public void An_exception_from_the_property_fails_it_and_becomes_the_inner_exception()
    {
        var failure = Should.Throw<FakeCheckException<Cart>>(() => Baskets().Check(b => b.Discount.ShouldBeLessThan(500)));

        failure.Counterexample.Discount.ShouldBe(500);
        failure.InnerException.ShouldBeOfType<ShouldAssertException>();
        failure.Message.ShouldContain(failure.InnerException!.Message.Split('\n')[0].Trim());
    }

    [Test]
    public void A_bool_property_that_throws_for_a_simpler_object_keeps_its_counterexample()
    {
        // Max throws for a cart without lines, which shrinking tries; that candidate is skipped, not the end of the check.
        var failure = Falsify(Baskets(), b => b.Lines.Max(l => l.Quantity) < 40);

        failure.InnerException.ShouldBeNull();
        failure.Counterexample.Lines.Max(l => l.Quantity).ShouldBe(40);
        failure.Counterexample.Lines.Count.ShouldBeLessThanOrEqualTo(failure.Original.Lines.Count);
    }

    [Test]
    public void A_bool_property_that_throws_is_reported_with_its_exception()
    {
        var failure = Falsify(Baskets(), b => b.Lines.Max(l => l.Quantity) > 0);

        failure.InnerException.ShouldBeOfType<InvalidOperationException>();
        failure.Counterexample.Lines.ShouldBeEmpty();
    }

    [Test]
    public void Shrinking_keeps_to_the_kind_of_failure_it_started_with()
    {
        // Without its lines the cart throws another exception first; shrinking must not swap the failure it found for that one.
        static void Property(Cart b)
        {
            if (b.Lines.Count == 0)
                throw new ArgumentException("empty");
            if (b.Discount >= 500)
                throw new InvalidOperationException("too much");
        }

        var failure = Should.Throw<FakeCheckException<Cart>>(() => Baskets().Seed(1).Check(Property));

        failure.Original.Lines.Count.ShouldBeGreaterThan(0); // the seed finds the discount first
        failure.InnerException.ShouldBeOfType<InvalidOperationException>();
        failure.Counterexample.Lines.Count.ShouldBe(1);
        failure.Counterexample.Discount.ShouldBe(500);
    }

    [Test]
    public void Nested_generators_shrink_too()
    {
        var failure = Falsify(
            Baskets().With(b => b.Featured, Fake.For<CartLine>().With(l => l.Sku, f => f.Commerce.Isbn())),
            b => b.Featured is null || b.Featured.Quantity < 25);

        failure.Counterexample.Featured!.Quantity.ShouldBe(25);
    }

    [Test]
    public void Without_shrinking_the_counterexample_is_the_original()
    {
        var failure = Falsify(Baskets(), b => b.Discount < 500, maxShrinks: 0);

        failure.ShrinkSteps.ShouldBe(0);
        Describe(failure.Counterexample).ShouldBe(Describe(failure.Original));
    }

    [Test]
    public void The_message_describes_the_counterexample_and_how_to_replay_it()
    {
        var failure = Falsify(Baskets(), b => b.Lines.Count < 2);

        failure.Message.ShouldContain("Cart");
        failure.Message.ShouldContain("Lines = [");
        failure.Message.ShouldContain("Discount = 0");
        failure.Message.ShouldContain("Sku = \"");
        failure.Message.ShouldContain("returned false");
    }

    [Test]
    public async Task CheckAsync_awaits_the_property_and_shrinks_the_same_way()
    {
        var sync = Falsify(Baskets(), b => b.Discount < 500);
        var failure = await Should.ThrowAsync<FakeCheckException<Cart>>(() => Baskets().CheckAsync(async b =>
        {
            await Task.Yield();
            if (b.Discount >= 500)
                throw new InvalidOperationException("too much");
        }));

        Describe(failure.Counterexample).ShouldBe(Describe(sync.Counterexample));
    }

    [Test]
    public void Zero_objects_check_nothing()
    {
        Baskets().Check(_ => false, count: 0);
    }

    [Test]
    public void Arguments_are_validated()
    {
        var negative = -1; // not a constant, which PHONEY011 would report
        Should.Throw<ArgumentNullException>(() => Baskets().Check((Func<Cart, bool>)null!));
        Should.Throw<ArgumentNullException>(() => Baskets().Check((Action<Cart>)null!));
        Should.Throw<ArgumentOutOfRangeException>(() => Baskets().Check(_ => true, count: negative)).ParamName.ShouldBe("count");
        Should.Throw<ArgumentOutOfRangeException>(() => Baskets().Check(_ => true, maxShrinks: negative)).ParamName.ShouldBe("maxShrinks");
    }

    [Test]
    public void Generating_after_a_check_is_unaffected()
    {
        var before = Baskets().Generate(5).Select(Describe).ToList();
        var generator = Baskets();
        Should.Throw<FakeCheckException<Cart>>(() => generator.Check(b => b.Discount < 500));

        generator.Generate(5).Select(Describe).ShouldBe(before); // the check doesn't move the generator's own sequence
    }

    [Test]
    public void Unique_members_are_checked_and_shrunk_without_counting_values_of_other_candidates()
    {
        var failure = Falsify(Baskets().Unique(b => b.Customer), b => b.Discount < 500);

        failure.Counterexample.Discount.ShouldBe(500);
        failure.Counterexample.Customer.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void A_counterexample_keeps_its_position_for_Sequence_rules()
    {
        var failure = Falsify(Baskets().Sequence(b => b.Customer, i => $"C{i}"), b => b.Discount < 900);

        failure.Counterexample.Customer.ShouldBe($"C{failure.Index}");
        failure.Original.Customer.ShouldBe($"C{failure.Index}");
    }

    [Test]
    public void A_counterexample_in_a_cycle_of_records_is_described_without_calling_their_ToString()
    {
        var failure = Should.Throw<FakeCheckException<CheckedFolder>>(() => Fake.For<CheckedFolder>().Seed(1).WithBackReferences().Check(f => f.Readme is null));

        failure.Message.ShouldContain("Parent = …");
    }

    [Test]
    public void A_record_without_readable_members_is_described_by_its_own_text()
    {
        var failure = Should.Throw<FakeCheckException<CheckedPoint>>(() => Fake.For<CheckedPoint>().Seed(3).Check(p => p.X < 0));

        failure.Message.ShouldContain("CheckedPoint { X = ");
    }

    [Test]
    public void A_tuple_counterexample_describes_each_of_its_elements()
    {
        var failure = Should.Throw<FakeCheckException<(CartLine Line, int Discount)>>(() =>
            Fake.For<(CartLine Line, int Discount)>().Seed(1).Check(t => t.Line.Quantity * 10 - t.Discount >= 0));

        failure.Message.ShouldContain("(CartLine { Sku = \"");
        failure.Message.ShouldNotContain("(CartLine,");
    }

    [Test]
    public void A_dictionary_counterexample_describes_its_values()
    {
        var failure = Should.Throw<FakeCheckException<CheckedCatalog>>(() => Fake.For<CheckedCatalog>().Seed(1).Check(c => c.BySku.Count < 1));

        failure.Message.ShouldContain("BySku = [\"");
        failure.Message.ShouldContain("\" = CartLine { Sku = \"");
    }

    [Test]
    public void A_recorded_tape_replays_the_same_object_and_runs_out_into_the_simplest_draw()
    {
        var recording = ChoiceTape.Record();
        var random = new SeededRandom(9) { Tape = recording };
        var drawn = new[] { random.NextUInt64(), random.NextUInt64() };
        recording.Draws().ShouldBe(drawn);
        drawn[0].ShouldBe(new SeededRandom(9).NextUInt64()); // recording doesn't change the draws

        var replaying = ChoiceTape.Replay([5, 6]);
        var replayed = new SeededRandom(1) { Tape = replaying };
        replayed.NextUInt64().ShouldBe(5UL);
        replayed.NextUInt64().ShouldBe(6UL);
        replayed.Int(10, 20).ShouldBe(10); // past the end: the simplest draw, the lowest value of any range
        replayed.Double().ShouldBe(0.0);
        replaying.Draws().ShouldBe([5UL, 6UL, ChoiceTape.Simplest, ChoiceTape.Simplest]);
    }

    [Test]
    public void A_replay_that_draws_far_past_its_tape_stops()
    {
        var random = new SeededRandom(1) { Tape = ChoiceTape.Replay([]) };

        Should.Throw<ChoiceTapeOverrunException>(() =>
        {
            for (var i = 0; i < 20_000; i++)
                random.NextUInt64();
        });
    }

    [Test]
    public void Shorter_tapes_are_simpler_and_tapes_of_one_length_compare_draw_by_draw()
    {
        TapeShrinker.Compare([9, 9], [1, 1, 1]).ShouldBeLessThan(0);
        TapeShrinker.Compare([1, 2], [1, 3]).ShouldBeLessThan(0);
        TapeShrinker.Compare([1, 3], [1, 3]).ShouldBe(0);
    }

    private static string Describe(Cart b) =>
        $"{b.Customer}|{b.Discount}|{string.Join(";", b.Lines.Select(l => $"{l.Sku},{l.Quantity},{l.Price}"))}|{b.Featured?.Sku},{b.Featured?.Quantity}";
}
