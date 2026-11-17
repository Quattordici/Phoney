using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>Invalid objects for a lifecycle: each case is made like a valid object and then breaks one stamp, so validation of the history can be tested.</summary>
public sealed class LifecycleInvalidTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Carriage> Model(bool generated) =>
        (generated ? LifecycleStampFakes.Carriage : new ObjectGenerator<Carriage>(new ReflectionModel<Carriage>())).Seed(53).ReferenceDate(Now);

    /// <summary>Placed → Paid (4) or Cancelled (1); Paid → Shipped; Shipped → Delivered.</summary>
    private static LifecycleBuilder<T, ParcelState> Shop<T>(LifecycleBuilder<T, ParcelState> l) => l
        .From(ParcelState.Placed).To(ParcelState.Paid, 4).To(ParcelState.Cancelled, 1)
        .From(ParcelState.Paid).To(ParcelState.Shipped)
        .From(ParcelState.Shipped).To(ParcelState.Delivered);

    private static List<ConstraintCase<Carriage>> StatusCases(bool generated) =>
        [.. Model(generated).Lifecycle(c => c.Status, Shop).InvalidCases().Where(c => c.Member == "Status")];

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Invalid_cases_break_the_stamps_three_ways(bool generated)
    {
        StatusCases(generated).Select(c => c.Description).ShouldBe(
        [
            "PaidAt is earlier than the object's creation",
            "PlacedAt is missing although the object entered Placed",
            "PaidAt is set although the object never entered Paid",
        ]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_stamp_out_of_order_comes_before_the_creation_of_an_object_that_entered_its_state(bool generated)
    {
        var c = StatusCases(generated)[0].Value;

        c.Status.ShouldBe(ParcelState.Paid);
        c.PaidAt!.Value.ShouldBeLessThan(c.CreatedAt);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_missing_stamp_is_null_although_the_object_is_in_its_state(bool generated)
    {
        var c = StatusCases(generated)[1].Value;

        c.Status.ShouldBe(ParcelState.Placed);
        c.PlacedAt.ShouldBeNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_stamp_of_an_unreached_state_is_set_on_an_object_that_never_left_the_start(bool generated)
    {
        var c = StatusCases(generated)[2].Value;

        c.Status.ShouldBe(ParcelState.Placed);
        c.PaidAt.ShouldNotBeNull();
        c.PaidAt.Value.ShouldBeGreaterThanOrEqualTo(c.CreatedAt);
    }

    [Test]
    public void Generated_and_reflection_models_give_the_same_cases()
    {
        static object Values(Carriage c) => (c.Id, c.Status, c.CreatedAt, c.PlacedAt, c.PaidAt, c.ShippedAt, c.DeliveredOn, c.CancelledDate, c.UpdatedAt, c.Note);

        StatusCases(true).Select(c => (c.Description, Values(c.Value))).ShouldBe(StatusCases(false).Select(c => (c.Description, Values(c.Value))));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Invalid_on_the_status_breaks_every_object_one_way_and_keeps_its_lifecycle(bool generated)
    {
        var carriages = Model(generated).Lifecycle(c => c.Status, Shop).Invalid(c => c.Status).Generate(200);

        foreach (var c in carriages)
        {
            var outOfOrder = c.PaidAt < c.CreatedAt;
            var missing = c.PlacedAt is null;
            var unreached = c.Status == ParcelState.Placed && c.PaidAt is not null;
            (outOfOrder || missing || unreached).ShouldBeTrue();
        }

        carriages.Count(c => c.PaidAt < c.CreatedAt).ShouldBeGreaterThan(0);
        carriages.Count(c => c.PlacedAt is null).ShouldBeGreaterThan(0);
        carriages.Count(c => c.Status == ParcelState.Placed && c.PaidAt is not null).ShouldBeGreaterThan(0);
    }

    [Test]
    public void A_stamp_with_a_rule_of_its_own_is_not_broken()
    {
        var cases = LifecycleStampFakes.Carriage.Seed(1).ReferenceDate(Now).Lifecycle(c => c.Status, Shop).With(c => c.PaidAt, (DateTimeOffset?)null)
            .InvalidCases().Where(c => c.Member == "Status").Select(c => c.Description);

        cases.ShouldNotContain(d => d.StartsWith("PaidAt", StringComparison.Ordinal));
    }

    [Test]
    public void Invalid_on_a_lifecycle_without_stamps_says_what_to_add()
    {
        var generator = LifecycleFakes.Freight.Lifecycle(f => f.Status, Shop).Invalid(f => f.Status);

        var error = Should.Throw<InvalidOperationException>(() => generator.Generate());

        error.Message.ShouldContain("Freight.Status");
        error.Message.ShouldContain("Stamp(");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Invalid_on_a_path_lifecycle_breaks_the_stamps_of_each_element(bool generated)
    {
        var carriages = Model(generated).Lifecycle(c => c.Lines.Each().Status, Shop).Invalid(c => c.Lines.Each().Status).Generate(30);

        var lines = carriages.SelectMany(c => c.Lines).ToList();
        lines.ShouldNotBeEmpty();
        foreach (var line in lines)
        {
            // CarriageLine has one stamp, PaidAt: a line is walked into Paid (its stamp out of order or missing) or kept in Placed with the stamp set.
            if (line.Status != ParcelState.Paid)
                (line.Status == ParcelState.Placed && line.PaidAt is not null).ShouldBeTrue($"{line.Status} {line.PaidAt}");
        }

        lines.Count(l => l.Status == ParcelState.Paid && l.PaidAt is null).ShouldBeGreaterThan(0);
        lines.Count(l => l.Status == ParcelState.Placed).ShouldBeGreaterThan(0);
    }
}
