using Phoney.Generation;

namespace Phoney.Tests;

public enum Outcome
{
    Paid,
    Pending,
    Failed,
}

public sealed class Payment
{
    public Outcome Outcome { get; set; }
    public string Method { get; set; } = "";
    public int? Installments { get; set; }
    public int CustomerId { get; set; }
    public List<Receipt> Receipts { get; set; } = [];
    public Receipt Main { get; set; } = null!;
}

public sealed class Receipt
{
    public string Currency { get; set; } = "";
}

public sealed record Client(int Id, string Name, double Orders);

[FakeFor<Payment>]
public static partial class WeightedFakes;

/// <summary><c>WithOneOf</c> with weights: some choices are more likely than others.</summary>
public sealed class WeightedOneOfTests
{
    private static readonly int[] OneTwo = [1, 2];
    private static readonly string[] A = ["a"];
    private static readonly string[] AB = ["a", "b"];
    private static readonly double[] OneWeight = [1.0];

    private static ObjectGenerator<Payment> Generated() => WeightedFakes.Payment.Seed(4);

    private static ObjectGenerator<Payment> Reflected() => new ObjectGenerator<Payment>(new ReflectionModel<Payment>()).Seed(4);

    private static double Share<TValue>(IEnumerable<TValue> values, TValue wanted)
    {
        var list = values.ToList();
        return list.Count(v => EqualityComparer<TValue>.Default.Equals(v, wanted)) / (double)list.Count;
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Choices_come_up_in_proportion_to_their_weights(bool generated)
    {
        var payments = (generated ? Generated() : Reflected())
            .WithOneOf(p => p.Outcome, (Outcome.Paid, 70), (Outcome.Pending, 25), (Outcome.Failed, 5))
            .Generate(20_000);

        var outcomes = payments.Select(p => p.Outcome).ToList();
        Share(outcomes, Outcome.Paid).ShouldBe(0.70, tolerance: 0.015);
        Share(outcomes, Outcome.Pending).ShouldBe(0.25, tolerance: 0.015);
        Share(outcomes, Outcome.Failed).ShouldBe(0.05, tolerance: 0.01);
    }

    [Test]
    public void Weights_are_relative_so_they_need_not_add_up_to_anything()
    {
        var payments = Reflected().WithOneOf(p => p.Method, ("card", 0.6), ("cash", 0.3), ("invoice", 0.1)).Generate(20_000);
        var scaled = Reflected().WithOneOf(p => p.Method, ("card", 6000.0), ("cash", 3000.0), ("invoice", 1000.0)).Generate(20_000);

        Share(payments.Select(p => p.Method), "card").ShouldBe(0.6, tolerance: 0.015);
        scaled.Select(p => p.Method).ShouldBe(payments.Select(p => p.Method)); // same relative weights, same seed, same choices
    }

    [Test]
    public void Values_and_weights_can_come_from_two_lists()
    {
        string[] methods = ["card", "cash"];
        double[] weights = [9, 1];

        var payments = Reflected().WithOneOf(p => p.Method, methods, weights).Generate(10_000);

        Share(payments.Select(p => p.Method), "card").ShouldBe(0.9, tolerance: 0.015);
    }

    [Test]
    public void Items_of_a_source_can_be_picked_by_a_weight_of_their_own()
    {
        Client[] clients = [new(1, "Ada", 90), new(2, "Grace", 9), new(3, "Alan", 1)];

        var payments = Reflected().WithOneOf(p => p.CustomerId, clients, c => c.Id, c => c.Orders).Generate(10_000);

        Share(payments.Select(p => p.CustomerId), 1).ShouldBe(0.9, tolerance: 0.015);
        Share(payments.Select(p => p.CustomerId), 3).ShouldBe(0.01, tolerance: 0.007);
    }

    [Test]
    public void A_choice_with_weight_zero_is_never_picked()
    {
        var payments = Reflected().WithOneOf(p => p.Method, ("never", 0), ("always", 1), ("also never", 0)).Generate(2_000);

        payments.ShouldAllBe(p => p.Method == "always");
    }

    [Test]
    public void It_works_for_nullable_members_nested_members_and_collection_elements()
    {
        var payments = Reflected().NullProbability(0)
            .WithOneOf(p => p.Installments, (3, 1), (6, 1), (12, 2))
            .WithOneOf(p => p.Main.Currency, ("SEK", 3), ("EUR", 1))
            .WithOneOf(p => p.Receipts.Each().Currency, ("USD", 1), ("GBP", 1))
            .Generate(4_000);

        payments.ShouldAllBe(p => p.Installments == 3 || p.Installments == 6 || p.Installments == 12);
        Share(payments.Select(p => p.Installments), 12).ShouldBe(0.5, tolerance: 0.03);
        Share(payments.Select(p => p.Main.Currency), "SEK").ShouldBe(0.75, tolerance: 0.03);
        payments.SelectMany(p => p.Receipts).ShouldAllBe(r => r.Currency == "USD" || r.Currency == "GBP");
    }

    [Test]
    public void The_two_models_make_the_same_choices_and_a_seed_gives_the_same_ones()
    {
        ObjectGenerator<Payment> Ruled(ObjectGenerator<Payment> g) => g.WithOneOf(p => p.Outcome, (Outcome.Paid, 70), (Outcome.Pending, 25), (Outcome.Failed, 5));

        var generated = Ruled(Generated()).Generate(200).Select(p => p.Outcome);

        generated.ShouldBe(Ruled(Reflected()).Generate(200).Select(p => p.Outcome));
        generated.ShouldBe(Ruled(Generated()).Generate(200).Select(p => p.Outcome));
    }

    [Test]
    public void Item_n_is_the_same_however_it_is_generated()
    {
        ObjectGenerator<Payment> Ruled() => Generated().WithOneOf(p => p.Outcome, (Outcome.Paid, 70), (Outcome.Pending, 25), (Outcome.Failed, 5));

        Ruled().GenerateParallel(100).Select(p => p.Outcome).ShouldBe(Ruled().Generate(100).Select(p => p.Outcome));
    }

    [Test]
    public void The_weights_are_copied_when_the_rule_is_added()
    {
        double[] weights = [1, 0];
        string[] methods = ["first", "second"];
        var generator = Reflected().WithOneOf(p => p.Method, methods, weights);

        weights[0] = 0;
        weights[1] = 1;
        methods[0] = "changed";

        generator.Generate(200).ShouldAllBe(p => p.Method == "first");
    }

    [Test]
    public void Strict_mode_counts_it_as_a_rule_and_the_unweighted_overload_still_works()
    {
        Should.NotThrow(() => Reflected().Strict().WithOneOf(p => p.Method, ("a", 1), ("b", 2)).WithOneOf(p => p.CustomerId, OneTwo)
            .Ignore(p => p.Outcome).Ignore(p => p.Installments).Ignore(p => p.Main).Ignore(p => p.Receipts).Generate());
    }

    [Test]
    public void Arguments_are_checked_when_the_rule_is_added()
    {
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(p => p.Method, Array.Empty<(string, double)>())).Message.ShouldContain("at least one");
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(p => p.Method, ("a", -1), ("b", 1))).Message.ShouldContain("negative");
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(p => p.Method, ("a", double.NaN)));
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(p => p.Method, ("a", double.PositiveInfinity)));
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(p => p.Method, ("a", 0), ("b", 0))).Message.ShouldContain("greater than zero");
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(p => p.Method, AB, OneWeight)).Message.ShouldContain("2 values and 1 weights");
        Should.Throw<ArgumentNullException>(() => Reflected().WithOneOf(p => p.Method, (IReadOnlyList<string>)null!, OneWeight));
        Should.Throw<ArgumentNullException>(() => Reflected().WithOneOf(p => p.Method, A, (IReadOnlyList<double>)null!));
        Should.Throw<ArgumentNullException>(() => Reflected().WithOneOf(p => p.Method, ((string, double)[])null!));
        Should.Throw<ArgumentNullException>(() => Reflected().WithOneOf(p => p.CustomerId, new[] { new Client(1, "a", 1) }, c => c.Id, (Func<Client, double>)null!));
    }

    // --- the primitive

    [Test]
    public void WeightedIndex_with_running_totals_picks_by_weight_with_one_draw()
    {
        var random = new SeededRandom(3);
        var expected = new SeededRandom(3);
        double[] cumulative = [1, 4, 10]; // weights 1, 3, 6

        var picks = Enumerable.Range(0, 30_000).Select(_ => random.WeightedIndex(cumulative)).ToList();
        for (var i = 0; i < 30_000; i++)
            expected.Double();

        Share(picks, 0).ShouldBe(0.1, tolerance: 0.01);
        Share(picks, 1).ShouldBe(0.3, tolerance: 0.01);
        Share(picks, 2).ShouldBe(0.6, tolerance: 0.01);
        random.Long(0, long.MaxValue).ShouldBe(expected.Long(0, long.MaxValue)); // exactly one number drawn per pick
    }
}
