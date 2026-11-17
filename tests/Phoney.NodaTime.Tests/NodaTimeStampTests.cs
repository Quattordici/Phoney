using NodaTime;
using Phoney.Generation;

namespace Phoney.NodaTime.Tests;

public enum DeliveryState
{
    Ordered,
    Paid,
    Shipped,
}

public sealed class Delivery
{
    public DeliveryState Status { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant? PaidAt { get; set; }
    public ZonedDateTime? ShippedAt { get; set; }
    public LocalDate? ShippedOn { get; set; }
    public LocalDateTime? PaidDate { get; set; }
    public OffsetDateTime UpdatedAt { get; set; }
}

[FakeFor<Delivery>]
public static partial class StampData;

/// <summary>A lifecycle's stamps on NodaTime members get the time the object entered the state, in UTC, through both models.</summary>
public sealed class NodaTimeStampTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Delivery> Model(bool generated) =>
        (generated ? StampData.Delivery : new ObjectGenerator<Delivery>(new ReflectionModel<Delivery>())).Seed(8).ReferenceDate(Reference);

    private static LifecycleBuilder<Delivery, DeliveryState> Lifecycle(LifecycleBuilder<Delivery, DeliveryState> l) => l
        .From(DeliveryState.Ordered).To(DeliveryState.Paid)
        .From(DeliveryState.Paid).To(DeliveryState.Shipped);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void NodaTime_stamps_agree_with_the_status(bool generated)
    {
        var deliveries = Model(generated).Lifecycle(d => d.Status, Lifecycle).Generate(500);

        foreach (var d in deliveries)
        {
            d.PaidAt.HasValue.ShouldBe(d.Status != DeliveryState.Ordered);
            d.ShippedAt.HasValue.ShouldBe(d.Status == DeliveryState.Shipped);
            if (d.PaidAt is { } paid)
            {
                paid.ShouldBeGreaterThan(d.CreatedAt);
                d.PaidDate.ShouldBe(paid.InUtc().LocalDateTime);
            }

            if (d.ShippedAt is { } shipped)
            {
                shipped.Zone.ShouldBe(DateTimeZone.Utc);
                d.ShippedOn.ShouldBe(shipped.Date);
            }

            d.UpdatedAt.ToInstant().ShouldBe(d.ShippedAt?.ToInstant() ?? d.PaidAt ?? d.CreatedAt);
        }

        deliveries.Select(d => d.Status).Distinct().Count().ShouldBe(3);
    }

    [Test]
    public void Generated_and_reflection_models_give_the_same_stamps()
    {
        static object Values(Delivery d) => (d.Status, d.CreatedAt, d.PaidAt, d.ShippedAt, d.ShippedOn, d.PaidDate, d.UpdatedAt);

        Model(true).Lifecycle(d => d.Status, Lifecycle).Generate(100).Select(Values)
            .ShouldBe(Model(false).Lifecycle(d => d.Status, Lifecycle).Generate(100).Select(Values));
    }
}
