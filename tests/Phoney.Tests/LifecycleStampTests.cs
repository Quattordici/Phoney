using Phoney.Generation;

// These tests pin what the runtime throws for lifecycles written wrong, which LifecycleAnalyzer also reports at compile time.
#pragma warning disable PHONEY013, PHONEY014

namespace Phoney.Tests;

public sealed class Carriage
{
    public int Id { get; set; }
    public ParcelState Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PlacedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateOnly? DeliveredOn { get; set; }
    public DateTimeOffset? CancelledDate { get; set; }
    public DateTimeOffset? DisputedAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Note { get; set; } = "";
    public List<CarriageLine> Lines { get; set; } = [];
}

public sealed class CarriageLine
{
    public string Sku { get; set; } = "";
    public ParcelState Status { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? Packed { get; set; }
}

/// <summary>A stamp of a state not every object reaches, on a member that can't be <see langword="null"/>.</summary>
public sealed class StrictCarriage
{
    public ParcelState Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset PaidAt { get; set; }
}

[FakeFor<Carriage>]
[FakeFor<StrictCarriage>]
public static partial class LifecycleStampFakes;

/// <summary>A lifecycle's stamps: the date members that say when an object entered a state, which agree with its status and its creation time.</summary>
public sealed class LifecycleStampTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Carriage> Model(bool generated) =>
        (generated ? LifecycleStampFakes.Carriage : new ObjectGenerator<Carriage>(new ReflectionModel<Carriage>())).Seed(23).ReferenceDate(Now);

    /// <summary>Placed → Paid (4) or Cancelled (1); Paid → Shipped; Shipped → Delivered.</summary>
    private static LifecycleBuilder<T, ParcelState> Shop<T>(LifecycleBuilder<T, ParcelState> l) => l
        .From(ParcelState.Placed).To(ParcelState.Paid, 4).To(ParcelState.Cancelled, 1)
        .From(ParcelState.Paid).To(ParcelState.Shipped)
        .From(ParcelState.Shipped).To(ParcelState.Delivered);

    private static readonly ParcelState[] Order = [ParcelState.Placed, ParcelState.Paid, ParcelState.Shipped, ParcelState.Delivered];

    private static bool Reached(ParcelState status, ParcelState state) =>
        status == state || (Array.IndexOf(Order, status) >= Array.IndexOf(Order, state) && Array.IndexOf(Order, state) >= 0 && status != ParcelState.Cancelled);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Stamps_found_by_name_are_set_for_the_states_an_object_reached(bool generated)
    {
        var shipments = Model(generated).Lifecycle(s => s.Status, Shop).Generate(2_000);

        foreach (var s in shipments)
        {
            s.PaidAt.HasValue.ShouldBe(Reached(s.Status, ParcelState.Paid), $"{s.Status}");
            s.ShippedAt.HasValue.ShouldBe(Reached(s.Status, ParcelState.Shipped), $"{s.Status}");
            s.DeliveredOn.HasValue.ShouldBe(s.Status == ParcelState.Delivered);
            s.CancelledDate.HasValue.ShouldBe(s.Status == ParcelState.Cancelled);
        }

        shipments.Select(s => s.Status).Distinct().Count().ShouldBe(5);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Stamps_follow_the_creation_time_in_the_order_of_the_walk(bool generated)
    {
        foreach (var s in Model(generated).Lifecycle(s => s.Status, Shop).Generate(1_000))
        {
            s.PlacedAt.ShouldBe(s.CreatedAt); // the start state is entered when the object is created
            if (s.PaidAt is { } paid)
                paid.ShouldBeGreaterThan(s.CreatedAt);
            if (s.ShippedAt is { } shipped)
            {
                shipped.Kind.ShouldBe(DateTimeKind.Utc);
                shipped.ShouldBeGreaterThan(s.PaidAt!.Value.UtcDateTime);
            }

            if (s.DeliveredOn is { } delivered)
                delivered.ShouldBeGreaterThanOrEqualTo(DateOnly.FromDateTime(s.ShippedAt!.Value));
            s.UpdatedAt.ShouldBeLessThanOrEqualTo(Now);
        }
    }

    [Test]
    public void Generated_and_reflection_models_give_the_same_stamps()
    {
        static object Values(Carriage s) => (s.Id, s.Status, s.CreatedAt, s.PlacedAt, s.PaidAt, s.ShippedAt, s.DeliveredOn, s.CancelledDate, s.UpdatedAt, s.Note);

        Model(true).Lifecycle(s => s.Status, Shop).Generate(300).Select(Values)
            .ShouldBe(Model(false).Lifecycle(s => s.Status, Shop).Generate(300).Select(Values));
    }

    [Test]
    public void The_updated_member_is_the_time_of_the_last_transition()
    {
        foreach (var s in Model(false).Lifecycle(s => s.Status, Shop).Generate(500))
        {
            var last = new[] { s.CreatedAt, s.PaidAt, s.ShippedAt is { } shipped ? new DateTimeOffset(shipped) : null, s.CancelledDate }.Max();
            if (s.DeliveredOn is null)
                s.UpdatedAt.ShouldBe(last!.Value);
            else
                DateOnly.FromDateTime(s.UpdatedAt.UtcDateTime).ShouldBe(s.DeliveredOn.Value);
        }
    }

    [Test]
    public void Each_gap_is_drawn_from_the_range_its_transition_gives()
    {
        var shipments = Model(false)
            .Lifecycle(s => s.Status, l => l
                .From(ParcelState.Placed).Stays(0).To(ParcelState.Paid, after: (TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)))
                .From(ParcelState.Paid).Stays(0).To(ParcelState.Shipped, after: (TimeSpan.FromHours(2), TimeSpan.FromHours(2))))
            .With(s => s.CreatedAt, Now.AddDays(-30))
            .Generate(300);

        foreach (var s in shipments)
        {
            (s.PaidAt!.Value - s.CreatedAt).ShouldBeInRange(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
            (new DateTimeOffset(s.ShippedAt!.Value) - s.PaidAt.Value).ShouldBe(TimeSpan.FromHours(2));
        }

        shipments.Select(s => s.PaidAt).Distinct().Count().ShouldBeGreaterThan(100);
    }

    [Test]
    public void Without_a_range_a_gap_is_an_hour_to_a_week()
    {
        var shipments = Model(false)
            .Lifecycle(s => s.Status, l => l.From(ParcelState.Placed).Stays(0).To(ParcelState.Paid))
            .With(s => s.CreatedAt, Now.AddDays(-30))
            .Generate(1_000);

        shipments.ShouldAllBe(s => s.PaidAt!.Value - s.CreatedAt >= TimeSpan.FromHours(1) && s.PaidAt.Value - s.CreatedAt <= TimeSpan.FromDays(7));
    }

    [Test]
    public void Gaps_that_would_pass_the_reference_date_are_scaled_so_the_last_one_lands_on_it_at_the_latest()
    {
        var created = Now.AddDays(-3);
        var shipments = Model(false)
            .Lifecycle(s => s.Status, l => l
                .From(ParcelState.Placed).Stays(0).To(ParcelState.Paid, after: (TimeSpan.FromDays(2), TimeSpan.FromDays(2)))
                .From(ParcelState.Paid).Stays(0).To(ParcelState.Shipped, after: (TimeSpan.FromDays(4), TimeSpan.FromDays(4))))
            .With(s => s.CreatedAt, created)
            .Generate(5);

        // Six days of gaps in three days: both are halved, so the parcel is paid after a day and shipped on the reference date.
        shipments.ShouldAllBe(s => s.PaidAt == created.AddDays(1) && s.ShippedAt == Now.UtcDateTime && s.UpdatedAt == Now);
    }

    [Test]
    public void An_object_created_at_or_after_the_reference_date_moves_on_at_once()
    {
        var created = Now.AddDays(2);
        var shipments = Model(false)
            .Lifecycle(s => s.Status, l => l.From(ParcelState.Placed).Stays(0).To(ParcelState.Paid).From(ParcelState.Paid).Stays(0).To(ParcelState.Shipped))
            .With(s => s.CreatedAt, created)
            .Generate(5);

        shipments.ShouldAllBe(s => s.PaidAt == created && s.ShippedAt == created.UtcDateTime && s.UpdatedAt == created);
    }

    [Test]
    public void A_state_entered_twice_is_stamped_with_its_last_entry()
    {
        var shipments = Model(false)
            .Lifecycle(s => s.Status, l => l
                .From(ParcelState.Placed).Stays(0).To(ParcelState.Paid)
                .From(ParcelState.Paid).Stays(0).To(ParcelState.Disputed)
                .From(ParcelState.Disputed).Stays(0).To(ParcelState.Paid)
                .MaxTransitions(3))
            .Generate(100);

        // Placed → Paid → Disputed → Paid: the dispute lies between the two payments, and the stamp is the second one.
        shipments.ShouldAllBe(s => s.Status == ParcelState.Paid && s.PaidAt > s.DisputedAt && s.UpdatedAt == s.PaidAt);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Stamp_ties_a_member_with_another_name_to_a_state(bool generated)
    {
        var shipments = Model(generated)
            .Lifecycle(s => s.Status, l => Shop(l).Stamp(ParcelState.Paid, s => s.SettledAt))
            .Generate(500);

        shipments.ShouldAllBe(s => s.SettledAt == s.PaidAt);
        shipments.ShouldContain(s => s.SettledAt != null);
    }

    [Test]
    public void A_member_with_its_own_rule_keeps_it()
    {
        var fixedTime = Now.AddYears(-2);
        var shipments = Model(false)
            .Lifecycle(s => s.Status, l => Shop(l).Stamp(ParcelState.Paid, s => s.SettledAt))
            .With(s => s.PaidAt, fixedTime)
            .With(s => s.SettledAt, (DateTimeOffset?)null)
            .Generate(100);

        shipments.ShouldAllBe(s => s.PaidAt == fixedTime && s.SettledAt == null);
    }

    [Test]
    public void A_later_rule_for_the_status_drops_the_stamps_with_the_lifecycle()
    {
#pragma warning disable PHONEY008 // replacing the lifecycle is what is tested
        var shipments = Model(false).Lifecycle(s => s.Status, Shop).With(s => s.Status, ParcelState.Delivered).Generate(50);
#pragma warning restore PHONEY008

        // Without the lifecycle the stamps are ordinary dates again: no state decides which of them are null.
        shipments.ShouldAllBe(s => s.Status == ParcelState.Delivered);
        shipments.ShouldContain(s => s.CancelledDate != null);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_path_lifecycle_stamps_each_nested_object(bool generated)
    {
        var shipments = Model(generated)
            .CollectionSize(3, 3)
            .Lifecycle(s => s.Lines.Each().Status, l => l
                .From(ParcelState.Placed).To(ParcelState.Paid)
                .Stamp(ParcelState.Paid, s => s.Lines.Each().Packed))
            .Generate(200);

        var lines = shipments.SelectMany(s => s.Lines).ToList();
        lines.ShouldAllBe(l => (l.Status == ParcelState.Paid) == (l.PaidAt != null) && l.Packed == l.PaidAt);
        lines.Select(l => l.Status).Distinct().Count().ShouldBe(2);
    }

    [Test]
    public void A_Where_on_a_stamp_generates_the_whole_object_again()
    {
        var shipments = Model(false).Lifecycle(s => s.Status, Shop).Where(s => s.PaidAt, paid => paid != null).Generate(200);

        shipments.ShouldAllBe(s => s.PaidAt != null && s.Status != ParcelState.Placed && s.Status != ParcelState.Cancelled);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_stamp_that_cant_be_null_for_a_state_an_object_may_skip_is_refused(bool generated)
    {
        var strict = generated ? LifecycleStampFakes.StrictCarriage : new ObjectGenerator<StrictCarriage>(new ReflectionModel<StrictCarriage>());

        var error = Should.Throw<InvalidOperationException>(() => strict.Lifecycle(s => s.Status, Shop).Generate());

        error.Message.ShouldContain("StrictCarriage.PaidAt");
        error.Message.ShouldContain("nullable");
        error.Message.ShouldContain("Ignore");
    }

    [Test]
    public void A_stamp_that_cant_be_null_is_fine_for_a_state_every_object_reaches()
    {
        var strict = new ObjectGenerator<StrictCarriage>(new ReflectionModel<StrictCarriage>()).Seed(5).ReferenceDate(Now);

        strict.Lifecycle(s => s.Status, l => l.From(ParcelState.Placed).Stays(0).To(ParcelState.Paid).From(ParcelState.Paid).To(ParcelState.Shipped))
            .Generate(100)
            .ShouldAllBe(s => s.PaidAt > s.CreatedAt);
        strict.Lifecycle(s => s.Status, Shop).Ignore(s => s.PaidAt).Generate(10).ShouldAllBe(s => s.PaidAt == default);
    }

    [Test]
    public void The_simplest_counterexample_has_the_smallest_gaps()
    {
        var failure = Should.Throw<FakeCheckException<Carriage>>(() => Model(false)
            .Lifecycle(s => s.Status, l => l.From(ParcelState.Placed).Stays(0).To(ParcelState.Paid, after: (TimeSpan.FromMinutes(1), TimeSpan.FromDays(1))))
            .With(s => s.CreatedAt, Now.AddDays(-10))
            .Check(s => s.PaidAt is null));

        (failure.Counterexample.PaidAt!.Value - failure.Counterexample.CreatedAt).ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_rule_beats_Stamp_which_beats_the_feed_which_beats_a_stamp_found_by_name(bool generated)
    {
        var fed = new DateTimeOffset(2001, 2, 3, 0, 0, 0, TimeSpan.Zero);
        var ruled = fed.AddDays(1);
        var conventional = fed.AddDays(2);
        var paid = Model(generated).Lifecycle(s => s.Status, l => l.From(ParcelState.Placed).Stays(0).To(ParcelState.Paid).Stamp(ParcelState.Paid, s => s.SettledAt));
        var feed = DataFeed.FromCsv("PaidAt,SettledAt\n2001-02-03T00:00:00+00:00,2001-02-03T00:00:00+00:00");

        // A stamp found by name beats a custom convention; the feed beats it.
        paid.WithConvention<DateTimeOffset?>(n => n == "PaidAt", _ => conventional).Generate(20).ShouldAllBe(s => s.PaidAt != conventional && s.PaidAt > s.CreatedAt);
        paid.WithFeed(feed).Generate(5).ShouldAllBe(s => s.PaidAt == fed);

        // Stamp beats the feed, and a rule beats Stamp.
        paid.WithFeed(feed).Generate(5).ShouldAllBe(s => s.SettledAt != fed && s.SettledAt > s.CreatedAt);
        paid.With(s => s.SettledAt, ruled).Generate(5).ShouldAllBe(s => s.SettledAt == ruled);

        // Without name conventions only Stamp ties a member to a state.
        paid.WithNameConventions(false).Generate(20).ShouldAllBe(s => s.SettledAt != null && s.PaidAt != s.SettledAt);
    }

    [Test]
    public void A_stamp_for_a_state_the_lifecycle_doesnt_have_is_refused()
    {
        Should.Throw<ArgumentException>(() => Model(false).Lifecycle(s => s.Status, l => Shop(l).Stamp(ParcelState.Refunded, s => s.SettledAt)))
            .Message.ShouldContain("Refunded");
    }

    [Test]
    public void A_member_stamped_by_two_states_is_refused()
    {
        Should.Throw<ArgumentException>(() => Model(false).Lifecycle(s => s.Status, l => Shop(l).Stamp(ParcelState.Paid, s => s.SettledAt).Stamp(ParcelState.Shipped, s => s.SettledAt)))
            .Message.ShouldContain("SettledAt");
    }

    [Test]
    public void A_stamp_that_isnt_a_date_is_refused()
    {
        Should.Throw<ArgumentException>(() => Model(false).Lifecycle(s => s.Status, l => Shop(l).Stamp(ParcelState.Paid, s => s.Note)))
            .Message.ShouldContain("Note");
    }

    [Test]
    public void A_stamp_on_another_object_than_the_status_is_refused()
    {
        Should.Throw<ArgumentException>(() => Model(false).Lifecycle(s => s.Lines.Each().Status, l => l.From(ParcelState.Placed).To(ParcelState.Paid).Stamp(ParcelState.Paid, s => s.SettledAt)))
            .Message.ShouldContain("SettledAt");
    }

    [Test]
    public void A_gap_range_must_be_ordered_and_not_negative()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Model(false).Lifecycle(s => s.Status, l => l.From(ParcelState.Placed).To(ParcelState.Paid, after: (TimeSpan.FromHours(-1), TimeSpan.FromHours(1)))));
        Should.Throw<ArgumentOutOfRangeException>(() => Model(false).Lifecycle(s => s.Status, l => l.From(ParcelState.Placed).To(ParcelState.Paid, after: (TimeSpan.FromHours(2), TimeSpan.FromHours(1)))));
    }
}
