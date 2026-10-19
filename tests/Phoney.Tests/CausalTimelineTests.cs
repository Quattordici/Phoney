using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Tenant
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Bill> Bills { get; set; } = [];
}

public sealed class Bill
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Settlement> Settlements { get; set; } = [];
}

public sealed class Settlement
{
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[FakeFor<Tenant>]
public static partial class TimelineFakes;

/// <summary><c>WithCausalTimelines</c>: a nested object is created after the object that contains it, so a graph's dates tell one story.</summary>
public sealed class CausalTimelineTests
{
    [Test]
    public void Without_it_children_can_be_older_than_their_parent()
    {
        var tenants = Fake.For<Tenant>().Seed(1).Generate(200);

        tenants.SelectMany(s => s.Bills.Select(i => i.CreatedAt >= s.CreatedAt)).ShouldContain(false);
    }

    [Test]
    public void Children_are_created_after_their_parent_at_every_level()
    {
        var tenants = Fake.For<Tenant>().Seed(1).WithCausalTimelines().Generate(200);

        tenants.SelectMany(s => s.Bills).ShouldNotBeEmpty();
        foreach (var tenant in tenants)
        {
            foreach (var bill in tenant.Bills)
            {
                bill.CreatedAt.ShouldBeGreaterThanOrEqualTo(tenant.CreatedAt);
                bill.UpdatedAt.ShouldBeGreaterThanOrEqualTo(bill.CreatedAt);
                foreach (var settlement in bill.Settlements)
                    settlement.CreatedAt.UtcDateTime.ShouldBeGreaterThanOrEqualTo(bill.CreatedAt);
            }
        }
    }

    [Test]
    public void Dates_stay_before_the_reference_date()
    {
        var reference = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tenants = Fake.For<Tenant>().Seed(2).ReferenceDate(reference).WithCausalTimelines().Generate(100);

        tenants.SelectMany(s => s.Bills.SelectMany(i => i.Settlements)).ShouldAllBe(p => p.CreatedAt <= reference);
    }

    [Test]
    public void Draws_do_not_move_so_other_members_keep_their_values()
    {
        var plain = Fake.For<Tenant>().Seed(3).Generate(50);
        var causal = Fake.For<Tenant>().Seed(3).WithCausalTimelines().Generate(50);

        causal.Select(s => (s.Id, s.CreatedAt)).ShouldBe(plain.Select(s => (s.Id, s.CreatedAt)));
        causal.SelectMany(s => s.Bills.Select(i => i.Id)).ShouldBe(plain.SelectMany(s => s.Bills.Select(i => i.Id)));
        causal.SelectMany(s => s.Bills.SelectMany(i => i.Settlements.Select(p => p.Amount)))
            .ShouldBe(plain.SelectMany(s => s.Bills.SelectMany(i => i.Settlements.Select(p => p.Amount))));
    }

    [Test]
    public void Reflection_and_generated_models_agree()
    {
        var reflection = new ObjectGenerator<Tenant>(new ReflectionModel<Tenant>()).Seed(4).WithCausalTimelines().Generate(30);
        var generated = TimelineFakes.Tenant.Seed(4).WithCausalTimelines().Generate(30);

        Dates(generated).ShouldBe(Dates(reflection));

        static IEnumerable<DateTime> Dates(List<Tenant> tenants) =>
            tenants.SelectMany(s => s.Bills.SelectMany(i => i.Settlements.Select(p => p.CreatedAt.UtcDateTime).Prepend(i.UpdatedAt).Prepend(i.CreatedAt)).Prepend(s.CreatedAt));
    }

    [Test]
    public void It_can_be_turned_off_again()
    {
        var plain = Fake.For<Tenant>().Seed(5).Generate(20);
        var off = Fake.For<Tenant>().Seed(5).WithCausalTimelines().WithCausalTimelines(false).Generate(20);

        off.SelectMany(s => s.Bills.Select(i => i.CreatedAt)).ShouldBe(plain.SelectMany(s => s.Bills.Select(i => i.CreatedAt)));
    }
}
