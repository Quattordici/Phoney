using Phoney.Generation;

// These tests pin what the runtime throws for lifecycles written wrong, which LifecycleAnalyzer also reports at compile time.
#pragma warning disable PHONEY013, PHONEY014

namespace Phoney.Tests;

public enum ParcelState
{
    Placed,
    Paid,
    Cancelled,
    Shipped,
    Delivered,
    Refunded,
    Disputed,
}

public sealed class Freight
{
    public int Id { get; set; }
    public ParcelState Status { get; set; }
    public string Note { get; set; } = "";
    public List<FreightLine> Lines { get; set; } = [];
}

public sealed class FreightLine
{
    public string Sku { get; set; } = "";
    public ParcelState Status { get; set; }
}

public sealed class Article
{
    public string Title { get; set; } = "";
    public string Stage { get; set; } = "";
}

[FakeFor<Freight>]
public static partial class LifecycleFakes;

/// <summary><c>Lifecycle(o =&gt; o.Status, l =&gt; …)</c> walks each object from the start state through weighted transitions, so its status is always reachable.</summary>
public sealed class LifecycleTests
{
    private static ObjectGenerator<Freight> Generated() => LifecycleFakes.Freight.Seed(17);

    private static ObjectGenerator<Freight> Reflected() => new ObjectGenerator<Freight>(new ReflectionModel<Freight>()).Seed(17);

    private static ObjectGenerator<Freight> Model(bool generated) => generated ? Generated() : Reflected();

    /// <summary>Placed → Paid (4) or Cancelled (1); Paid → Shipped; Shipped → Delivered. Staying weighs 1 everywhere.</summary>
    private static LifecycleBuilder<T, ParcelState> Shop<T>(LifecycleBuilder<T, ParcelState> l) => l
        .From(ParcelState.Placed).To(ParcelState.Paid, 4).To(ParcelState.Cancelled, 1)
        .From(ParcelState.Paid).To(ParcelState.Shipped)
        .From(ParcelState.Shipped).To(ParcelState.Delivered);

    private static Dictionary<ParcelState, double> Shares(IEnumerable<Freight> parcels)
    {
        var list = parcels.ToList();
        return list.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count() / (double)list.Count);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Statuses_follow_the_weights_of_staying_and_moving_on(bool generated)
    {
        var shares = Shares(Model(generated).Lifecycle(p => p.Status, Shop).Generate(20_000));

        // Placed stays 1/6, is cancelled 1/6, is paid 4/6; a paid parcel stays or ships half the time each, and so on.
        shares[ParcelState.Placed].ShouldBe(1 / 6.0, tolerance: 0.015);
        shares[ParcelState.Cancelled].ShouldBe(1 / 6.0, tolerance: 0.015);
        shares[ParcelState.Paid].ShouldBe(2 / 6.0, tolerance: 0.015);
        shares[ParcelState.Shipped].ShouldBe(1 / 6.0, tolerance: 0.015);
        shares[ParcelState.Delivered].ShouldBe(1 / 6.0, tolerance: 0.015);
        shares.Keys.ShouldNotContain(ParcelState.Refunded);
    }

    [Test]
    public void Generated_and_reflection_models_walk_the_same_paths()
    {
        var generated = Generated().Lifecycle(p => p.Status, Shop).Generate(200);
        var reflected = Reflected().Lifecycle(p => p.Status, Shop).Generate(200);

        generated.Select(p => (p.Id, p.Status, p.Note)).ShouldBe(reflected.Select(p => (p.Id, p.Status, p.Note)));
    }

    [Test]
    public void A_terminal_state_stays_and_a_state_that_stays_with_weight_zero_moves_on()
    {
        var shares = Shares(Reflected()
            .Lifecycle(p => p.Status, l => l
                .From(ParcelState.Placed).Stays(0).To(ParcelState.Paid).To(ParcelState.Cancelled)
                .From(ParcelState.Paid).Stays(0).To(ParcelState.Shipped))
            .Generate(5_000));

        shares.Keys.Order().ShouldBe([ParcelState.Cancelled, ParcelState.Shipped]);
        shares[ParcelState.Shipped].ShouldBe(0.5, tolerance: 0.03);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void EndsIn_gives_exactly_the_mix_of_end_states_asked_for(bool generated)
    {
        var parcels = Model(generated)
            .Lifecycle(p => p.Status, l => Shop(l).EndsIn(ParcelState.Delivered, 70).EndsIn(ParcelState.Cancelled, 10).EndsIn(ParcelState.Paid, 20))
            .Generate(20_000);
        var shares = Shares(parcels);

        shares[ParcelState.Delivered].ShouldBe(0.7, tolerance: 0.015);
        shares[ParcelState.Cancelled].ShouldBe(0.1, tolerance: 0.015);
        shares[ParcelState.Paid].ShouldBe(0.2, tolerance: 0.015);
        shares.Count.ShouldBe(3);
    }

    [Test]
    public void A_cycle_stops_after_MaxTransitions()
    {
        var parcels = Reflected()
            .Lifecycle(p => p.Status, l => l
                .From(ParcelState.Placed).Stays(0).To(ParcelState.Paid)
                .From(ParcelState.Paid).Stays(0).To(ParcelState.Disputed)
                .From(ParcelState.Disputed).Stays(0).To(ParcelState.Paid)
                .MaxTransitions(3))
            .Generate(100);

        // Nobody may stay, so every walk takes exactly three transitions: Placed → Paid → Disputed → Paid.
        parcels.ShouldAllBe(p => p.Status == ParcelState.Paid);
    }

    [Test]
    public void EndsIn_reaches_its_state_through_a_cycle_within_the_budget()
    {
        var parcels = Reflected()
            .Lifecycle(p => p.Status, l => l
                .From(ParcelState.Placed).To(ParcelState.Paid)
                .From(ParcelState.Paid).To(ParcelState.Disputed).To(ParcelState.Refunded)
                .From(ParcelState.Disputed).To(ParcelState.Paid)
                .EndsIn(ParcelState.Refunded, 1)
                .MaxTransitions(4))
            .Generate(500);

        parcels.ShouldAllBe(p => p.Status == ParcelState.Refunded);
    }

    [Test]
    public void Start_picks_the_start_state_when_the_first_From_is_not_it()
    {
        var parcels = Reflected()
            .Lifecycle(p => p.Status, l => l
                .From(ParcelState.Paid).Stays(0).To(ParcelState.Shipped)
                .From(ParcelState.Placed).Stays(0).To(ParcelState.Paid)
                .Start(ParcelState.Placed))
            .Generate(50);

        parcels.ShouldAllBe(p => p.Status == ParcelState.Shipped);
    }

    [Test]
    public void The_simplest_counterexample_is_an_object_in_its_start_state()
    {
        var failure = Should.Throw<FakeCheckException<Freight>>(() =>
            Reflected().Lifecycle(p => p.Status, Shop).Check(p => p.Status == ParcelState.Delivered));

        failure.Counterexample.Status.ShouldBe(ParcelState.Placed);
    }

    [Test]
    public void States_can_be_any_type_compared_by_value()
    {
        var articles = Fake.For<Article>()
            .Seed(3)
            .Lifecycle(a => a.Stage, l => l.From("draft").To("review").From("review").To("published").To("draft"))
            .Generate(500);

        articles.Select(a => a.Stage).Distinct().Order().ShouldBe(["draft", "published", "review"]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_member_view_is_the_same_rule(bool generated)
    {
        var flat = Model(generated).Lifecycle(p => p.Status, Shop).Generate(100);
        var view = Model(generated).Member(p => p.Status, m => m.Lifecycle(Shop)).Generate(100);

        view.Select(p => p.Status).ShouldBe(flat.Select(p => p.Status));
    }

    [Test]
    public void A_later_rule_for_the_status_replaces_the_lifecycle()
    {
#pragma warning disable PHONEY008 // replacing the lifecycle is what is tested
        Reflected().Lifecycle(p => p.Status, Shop).With(p => p.Status, ParcelState.Refunded).Generate(20)
            .ShouldAllBe(p => p.Status == ParcelState.Refunded);
#pragma warning restore PHONEY008
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_Where_on_the_status_generates_the_whole_object_again(bool generated)
    {
        var plain = Model(generated).Lifecycle(p => p.Status, Shop).Generate(300);
        var filtered = Model(generated).Lifecycle(p => p.Status, Shop).Where(p => p.Status, s => s != ParcelState.Placed).Generate(300);

        filtered.ShouldAllBe(p => p.Status != ParcelState.Placed);
        // An object the condition accepts on its first attempt is the object you get without the condition.
        for (var i = 0; i < plain.Count; i++)
        {
            if (plain[i].Status != ParcelState.Placed)
                filtered[i].Id.ShouldBe(plain[i].Id);
        }
    }

    [Test]
    public void A_Where_no_object_meets_names_the_member()
    {
        var error = Should.Throw<InvalidOperationException>(() =>
            Reflected().Lifecycle(p => p.Status, Shop).Where(p => p.Status, s => s == ParcelState.Refunded, maxAttempts: 20).Generate(5));

        error.Message.ShouldContain("Status");
        error.Message.ShouldContain("Where");
    }

    [Test]
    public void A_unique_status_generates_the_whole_object_again()
    {
        var parcels = Reflected()
            .Lifecycle(p => p.Status, Shop)
            .Unique(p => p.Status)
            .Generate(5);

        parcels.Select(p => p.Status).Distinct().Count().ShouldBe(5);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_lifecycle_on_a_path_walks_each_nested_object(bool generated)
    {
        var parcels = Model(generated)
            .CollectionSize(3, 3)
            .Lifecycle(p => p.Lines.Each().Status, l => l.From(ParcelState.Placed).Stays(0).To(ParcelState.Shipped).To(ParcelState.Cancelled))
            .Generate(200);

        var statuses = parcels.SelectMany(p => p.Lines).Select(l => l.Status).ToList();
        statuses.Distinct().Order().ShouldBe([ParcelState.Cancelled, ParcelState.Shipped]);
        parcels.Select(p => p.Status).Distinct().Count().ShouldBeGreaterThan(2); // the root's own status is untouched
    }

    [Test]
    public void The_same_seed_gives_the_same_objects()
    {
        Reflected().Lifecycle(p => p.Status, Shop).Generate(50).Select(p => p.Status)
            .ShouldBe(Reflected().Lifecycle(p => p.Status, Shop).Generate(50).Select(p => p.Status));
    }

    [Test]
    public void A_state_the_start_cant_reach_is_refused()
    {
        var error = Should.Throw<ArgumentException>(() => Reflected().Lifecycle(p => p.Status, l => l
            .From(ParcelState.Placed).To(ParcelState.Paid)
            .From(ParcelState.Shipped).To(ParcelState.Delivered)));

        error.Message.ShouldContain("Shipped");
        error.Message.ShouldContain("Placed");
    }

    [Test]
    public void A_lifecycle_without_transitions_is_refused()
    {
        Should.Throw<ArgumentException>(() => Reflected().Lifecycle(p => p.Status, l => l)).Message.ShouldContain(".From(");
    }

    [Test]
    public void A_duplicate_transition_is_refused()
    {
        Should.Throw<ArgumentException>(() => Reflected().Lifecycle(p => p.Status, l => l.From(ParcelState.Placed).To(ParcelState.Paid).To(ParcelState.Paid)))
            .Message.ShouldContain("twice");
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(-1.0)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public void A_weight_that_is_not_positive_and_finite_is_refused(double weight)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().Lifecycle(p => p.Status, l => l.From(ParcelState.Placed).To(ParcelState.Paid, weight)));
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().Lifecycle(p => p.Status, l => l.From(ParcelState.Placed).To(ParcelState.Paid).EndsIn(ParcelState.Paid, weight)));
    }

    [Test]
    public void A_negative_stay_weight_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().Lifecycle(p => p.Status, l => l.From(ParcelState.Placed).Stays(-1).To(ParcelState.Paid)));
    }

    [Test]
    public void An_undefined_enum_value_is_refused()
    {
        Should.Throw<ArgumentException>(() => Reflected().Lifecycle(p => p.Status, l => l.From(ParcelState.Placed).To((ParcelState)99)))
            .Message.ShouldContain("99");
    }

    [Test]
    public void To_and_Stays_need_a_From_first()
    {
        Should.Throw<InvalidOperationException>(() => Reflected().Lifecycle(p => p.Status, l => l.To(ParcelState.Paid))).Message.ShouldContain(".From(");
        Should.Throw<InvalidOperationException>(() => Reflected().Lifecycle(p => p.Status, l => l.Stays(2))).Message.ShouldContain(".From(");
    }

    [Test]
    public void An_end_state_the_walk_cant_reach_in_time_is_refused()
    {
        Should.Throw<ArgumentException>(() => Reflected().Lifecycle(p => p.Status, l => Shop(l).EndsIn(ParcelState.Delivered, 1).MaxTransitions(2)))
            .Message.ShouldContain("Delivered");
        Should.Throw<ArgumentException>(() => Reflected().Lifecycle(p => p.Status, l => Shop(l).EndsIn(ParcelState.Refunded, 1)))
            .Message.ShouldContain("Refunded");
    }

    [Test]
    public void MaxTransitions_must_be_at_least_one()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().Lifecycle(p => p.Status, l => Shop(l).MaxTransitions(0)));
    }

    [Test]
    public void Arguments_are_validated()
    {
        Should.Throw<ArgumentNullException>(() => Reflected().Lifecycle(p => p.Status, null!));
        Should.Throw<ArgumentNullException>(() => Reflected().Lifecycle((System.Linq.Expressions.Expression<Func<Freight, ParcelState>>)null!, Shop));
        Should.Throw<InvalidOperationException>(() => Reflected().Lifecycle(p => p.Status, _ => null!));
    }
}
