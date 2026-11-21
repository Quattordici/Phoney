using Phoney.Generation;

// These tests pin what the runtime throws for lifecycles written wrong, which LifecycleAnalyzer also reports at compile time.
#pragma warning disable PHONEY013, PHONEY014

namespace Phoney.Tests;

/// <summary>The history behind generated objects: the transitions each lifecycle in the graph took, with their times, next to the very objects Generate makes.</summary>
public sealed class LifecycleHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Carriage> Model(bool generated) =>
        (generated ? LifecycleStampFakes.Carriage : new ObjectGenerator<Carriage>(new ReflectionModel<Carriage>())).Seed(31).ReferenceDate(Now);

    private static LifecycleBuilder<T, ParcelState> Shop<T>(LifecycleBuilder<T, ParcelState> l) => l
        .From(ParcelState.Placed).To(ParcelState.Paid, 4).To(ParcelState.Cancelled, 1)
        .From(ParcelState.Paid).To(ParcelState.Shipped)
        .From(ParcelState.Shipped).To(ParcelState.Delivered);

    private static object Values(Carriage c) =>
        (c.Id, c.Status, c.CreatedAt, c.PaidAt, c.ShippedAt, c.DeliveredOn, c.UpdatedAt, c.Note, string.Join(",", c.Lines.Select(l => $"{l.Sku}:{l.Status}:{l.PaidAt}")));

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_objects_of_a_history_are_the_objects_Generate_makes(bool generated)
    {
        var histories = Model(generated).Lifecycle(c => c.Status, Shop).Lifecycle(c => c.Lines.Each().Status, Shop).GenerateHistory(50);
        var objects = Model(generated).Lifecycle(c => c.Status, Shop).Lifecycle(c => c.Lines.Each().Status, Shop).Generate(50);

        histories.Select(h => Values(h.Object)).ShouldBe(objects.Select(Values));
    }

    [Test]
    public void Generated_and_reflection_models_give_the_same_histories()
    {
        static IEnumerable<object> Of(bool generated) => Model(generated).Lifecycle(c => c.Status, Shop).Lifecycle(c => c.Lines.Each().Status, Shop)
            .GenerateHistory(30).SelectMany(h => h.Transitions.Select(t => (object)(t.Member, t.Path, t.From, t.To, t.At)).Prepend(h.Created));

        Of(true).ShouldBe(Of(false));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_transitions_of_an_object_lead_from_its_start_to_its_status_at_the_times_of_its_stamps(bool generated)
    {
        foreach (var history in Model(generated).Lifecycle(c => c.Status, Shop).GenerateHistory(300))
        {
            var carriage = history.Object;
            var transitions = history.Of(c => c.Status);
            history.Created.ShouldBe(carriage.CreatedAt);
            history.Transitions.Count.ShouldBe(transitions.Count);

            var state = ParcelState.Placed;
            var at = history.Created;
            foreach (var t in transitions)
            {
                t.Member.ShouldBe("Status");
                t.Path.ShouldBe("Status");
                t.Owner.ShouldBeSameAs(carriage);
                t.From.ShouldBe(state);
                t.At.ShouldBeGreaterThanOrEqualTo(at);
                state = t.To;
                at = t.At;
            }

            state.ShouldBe(carriage.Status);
            carriage.UpdatedAt.ShouldBe(at);
            if (transitions.FirstOrDefault(t => t.To == ParcelState.Paid) is { } paid)
                carriage.PaidAt.ShouldBe(paid.At);
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Transitions_of_nested_objects_carry_their_path_and_owner_and_all_come_in_time_order(bool generated)
    {
        var histories = Model(generated).Lifecycle(c => c.Status, Shop).Lifecycle(c => c.Lines.Each().Status, Shop).GenerateHistory(100);

        histories.SelectMany(h => h.Of(c => c.Lines.Each().Status)).ShouldNotBeEmpty();
        foreach (var history in histories)
        {
            history.Transitions.Select(t => t.At).ShouldBeInOrder();
            var lines = history.Of(c => c.Lines.Each().Status);
            history.Transitions.Count.ShouldBe(history.Of(c => c.Status).Count + lines.Count);
            foreach (var t in lines)
            {
                var index = history.Object.Lines.FindIndex(l => ReferenceEquals(l, t.Owner));
                index.ShouldBeGreaterThanOrEqualTo(0);
                t.Path.ShouldBe($"Lines[{index}].Status");
                if (t.To == ParcelState.Paid)
                    ((CarriageLine)t.Owner).PaidAt.ShouldBe(t.At);
            }

            foreach (var line in history.Object.Lines)
                (lines.LastOrDefault(t => ReferenceEquals(t.Owner, line))?.To ?? ParcelState.Placed).ShouldBe(line.Status);
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Objects_from_a_generator_for_their_type_bring_their_transitions(bool generated)
    {
        var lines = new ObjectGenerator<CarriageLine>(new ReflectionModel<CarriageLine>()).Lifecycle(l => l.Status, Shop);
        var histories = Model(generated).Lifecycle(c => c.Status, Shop).WithGeneratorFor(lines).GenerateHistory(50);

        var nested = histories.SelectMany(h => h.Of(c => c.Lines.Each().Status)).ToList();
        nested.ShouldNotBeEmpty();
        nested.ShouldAllBe(t => t.Path.StartsWith("Lines[") && t.Path.EndsWith("].Status") && t.Owner is CarriageLine);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void An_object_made_again_for_a_condition_keeps_only_the_transitions_of_the_accepted_attempt(bool generated)
    {
        var histories = Model(generated).Lifecycle(c => c.Status, Shop).Where(c => c.Status == ParcelState.Delivered).GenerateHistory(30);

        histories.ShouldAllBe(h => h.Object.Status == ParcelState.Delivered);
        histories.ShouldAllBe(h => h.Of(c => c.Status).Select(t => t.To).SequenceEqual(new[] { ParcelState.Paid, ParcelState.Shipped, ParcelState.Delivered }));
    }

    [Test]
    public void A_keyed_generator_gives_the_history_of_its_keyed_object()
    {
        static ObjectGenerator<Carriage> Keyed() => LifecycleStampFakes.Carriage.Lifecycle(c => c.Status, Shop).Keyed("order-42");

        var history = Keyed().GenerateHistory()[0];
        Values(history.Object).ShouldBe(Values(Keyed().Generate()));
        history.Transitions.Select(t => t.At).ShouldBe(Keyed().GenerateHistory()[0].Transitions.Select(t => t.At));
    }

    [Test]
    public void A_generator_without_a_lifecycle_can_not_give_a_history()
    {
        var error = Should.Throw<InvalidOperationException>(() => LifecycleStampFakes.Carriage.GenerateHistory());

        error.Message.ShouldContain("Lifecycle(");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_generator_for_a_nested_type_brings_its_lifecycle_to_a_root_without_one(bool generated)
    {
        var lines = new ObjectGenerator<CarriageLine>(new ReflectionModel<CarriageLine>()).Lifecycle(l => l.Status, Shop);

        var histories = Model(generated).WithGeneratorFor(lines).GenerateHistory(20);

        histories.Select(h => Values(h.Object)).ShouldBe(Model(generated).WithGeneratorFor(lines).Generate(20).Select(Values));
        histories.SelectMany(h => h.Of(c => c.Lines.Each().Status)).ShouldNotBeEmpty();
    }

    [Test]
    public void A_generator_for_a_nested_type_without_a_lifecycle_gives_no_history()
    {
        var lines = new ObjectGenerator<CarriageLine>(new ReflectionModel<CarriageLine>()).With(l => l.Sku, "x");

        Should.Throw<InvalidOperationException>(() => Model(true).WithGeneratorFor(lines).GenerateHistory()).Message.ShouldContain("Fake.Generators");
    }

    [Test]
    [NotInParallel]
    public void A_type_wide_configuration_brings_its_lifecycle_to_a_root_without_one()
    {
        try
        {
            Fake.Generators.Configure<HistoryLine>(g => g.Lifecycle(l => l.Status, Shop));

            var histories = Fake.For<HistoryBasket>().Seed(5).ReferenceDate(Now).GenerateHistory(20);

            var transitions = histories.SelectMany(h => h.Of(b => b.Lines.Each().Status)).ToList();
            transitions.ShouldNotBeEmpty();
            transitions.ShouldAllBe(t => t.Owner is HistoryLine);
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }

    [Test]
    public void The_count_is_checked()
    {
        var generator = LifecycleStampFakes.Carriage.Lifecycle(c => c.Status, Shop);

        generator.GenerateHistory(0).ShouldBeEmpty();
        Should.Throw<ArgumentOutOfRangeException>(() => generator.GenerateHistory(-1));
    }

    [Test]
    public void Of_names_a_member_that_has_a_lifecycle()
    {
        var history = LifecycleStampFakes.Carriage.Seed(1).Lifecycle(c => c.Status, Shop).GenerateHistory()[0];

        history.Of(c => c.Status).ShouldNotBeNull();
        Should.Throw<ArgumentException>(() => history.Of(c => c.Note)).Message.ShouldContain("Carriage.Note");
        Should.Throw<ArgumentException>(() => history.Of(c => c.Lines.Each().Status)).Message.ShouldContain("Carriage.Lines.Status");
    }
}

/// <summary>Only <see cref="LifecycleHistoryTests"/> configures this type app-wide, so no other test sees the configuration.</summary>
public sealed class HistoryBasket
{
    public List<HistoryLine> Lines { get; set; } = [];
}

/// <summary>A line of <see cref="HistoryBasket"/>.</summary>
public sealed class HistoryLine
{
    public ParcelState Status { get; set; }
}
