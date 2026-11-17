using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>A lifecycle on objects that already exist: <c>Populate</c> keeps a status that is set and walks the object into it, so the stamps it fills agree.</summary>
public sealed class LifecyclePopulateTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Carriage> Model(bool generated) =>
        (generated ? LifecycleStampFakes.Carriage : new ObjectGenerator<Carriage>(new ReflectionModel<Carriage>())).Seed(41).ReferenceDate(Now);

    /// <summary>Placed → Paid (4) or Cancelled (1); Paid → Shipped; Shipped → Delivered.</summary>
    private static LifecycleBuilder<T, ParcelState> Shop<T>(LifecycleBuilder<T, ParcelState> l) => l
        .From(ParcelState.Placed).To(ParcelState.Paid, 4).To(ParcelState.Cancelled, 1)
        .From(ParcelState.Paid).To(ParcelState.Shipped)
        .From(ParcelState.Shipped).To(ParcelState.Delivered);

    private static List<Carriage> Kept(ParcelState status, int count = 50) => [.. Enumerable.Range(0, count).Select(_ => new Carriage { Status = status })];

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_kept_status_is_where_the_walk_ends_so_the_stamps_on_the_way_are_filled(bool generated)
    {
        foreach (var c in Model(generated).Lifecycle(c => c.Status, Shop).Populate(Kept(ParcelState.Shipped)))
        {
            c.Status.ShouldBe(ParcelState.Shipped);
            c.PlacedAt.ShouldBe(c.CreatedAt);
            c.PaidAt!.Value.ShouldBeGreaterThan(c.CreatedAt);
            c.ShippedAt!.Value.ShouldBeGreaterThan(c.PaidAt.Value.UtcDateTime);
            c.DeliveredOn.ShouldBeNull();
            c.CancelledDate.ShouldBeNull();
            c.UpdatedAt.ShouldBe(new DateTimeOffset(c.ShippedAt.Value));
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_kept_status_off_the_main_path_is_reached_too(bool generated)
    {
        foreach (var c in Model(generated).Lifecycle(c => c.Status, Shop).Populate(Kept(ParcelState.Cancelled)))
        {
            c.Status.ShouldBe(ParcelState.Cancelled);
            c.CancelledDate!.Value.ShouldBeGreaterThan(c.CreatedAt);
            c.PaidAt.ShouldBeNull();
        }
    }

    [Test]
    public void Generated_and_reflection_models_populate_the_same_values()
    {
        static object Values(Carriage c) => (c.Id, c.Status, c.CreatedAt, c.PlacedAt, c.PaidAt, c.ShippedAt, c.DeliveredOn, c.CancelledDate, c.UpdatedAt, c.Note);

        Model(true).Lifecycle(c => c.Status, Shop).Populate(Kept(ParcelState.Delivered)).Select(Values)
            .ShouldBe(Model(false).Lifecycle(c => c.Status, Shop).Populate(Kept(ParcelState.Delivered)).Select(Values));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_kept_stamp_stays_as_it_is(bool generated)
    {
        var paid = new DateTimeOffset(2024, 3, 9, 10, 0, 0, TimeSpan.Zero);

        var c = Model(generated).Lifecycle(c => c.Status, Shop).Populate(new Carriage { Status = ParcelState.Shipped, PaidAt = paid });

        c.PaidAt.ShouldBe(paid);
        c.ShippedAt.ShouldNotBeNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_status_left_unset_is_drawn_as_when_generating(bool generated)
    {
        var populated = Model(generated).Lifecycle(c => c.Status, Shop).Populate(new Carriage());

        // Placed is the default value, so the status counts as unset and the lifecycle walks as usual.
        populated.PaidAt.HasValue.ShouldBe(populated.Status is ParcelState.Paid or ParcelState.Shipped or ParcelState.Delivered);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_kept_status_the_lifecycle_can_not_reach_in_time_throws(bool generated)
    {
        var generator = Model(generated).Lifecycle(c => c.Status, l => Shop(l).MaxTransitions(1));

        var error = Should.Throw<InvalidOperationException>(() => generator.Populate(new Carriage { Status = ParcelState.Shipped }));

        error.Message.ShouldContain("Carriage.Status");
        error.Message.ShouldContain("Shipped");
        error.Message.ShouldContain("MaxTransitions");
        error.Message.ShouldContain("PopulateMode.All");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_kept_status_that_is_not_a_state_of_the_lifecycle_throws(bool generated)
    {
        var generator = Model(generated).Lifecycle(c => c.Status, Shop);

        var error = Should.Throw<InvalidOperationException>(() => generator.Populate(new Carriage { Status = ParcelState.Refunded }));

        error.Message.ShouldContain("Refunded");
        error.Message.ShouldContain("not one of its states");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Populating_everything_draws_a_new_status(bool generated)
    {
        var populated = Model(generated).Lifecycle(c => c.Status, Shop).Populate(Kept(ParcelState.Refunded, 100), PopulateMode.All);

        populated.ShouldAllBe(c => c.Status != ParcelState.Refunded);
        populated.Select(c => c.Status).Distinct().Count().ShouldBeGreaterThan(1);
    }
}
