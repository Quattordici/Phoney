namespace Phoney.Tests;

// Only WithTypeTests registers an app-wide convention for this enum, so it cannot disturb other tests.
public enum LoyaltyTier
{
    Bronze,
    Silver,
    Gold,
}

public enum SupportPlan
{
    Basic,
    Premium,
}

public sealed class Subscriber
{
    public SupportPlan Plan { get; set; }
    public string Owner { get; set; } = "";
    public LoyaltyTier Tier { get; set; }
    public LoyaltyTier? Next { get; set; }
    public int? Seats { get; set; }
    public List<string> Tags { get; set; } = [];
    public Ticket Ticket { get; set; } = null!;
}

/// <summary><c>WithEveryMemberOfType</c> gives every member of a type a value, in the generated object and in nested objects.</summary>
public sealed class WithTypeTests
{
    static WithTypeTests() => Fake.Conventions.Add<SupportPlan>(_ => SupportPlan.Premium); // a type no other test uses

    private static ObjectGenerator<Subscriber> Make() => Fake.For<Subscriber>().Seed(1);

    [Test]
    public void Every_member_of_the_type_gets_the_value_also_in_nested_objects_and_whatever_its_name()
    {
        var accounts = Make().WithEveryMemberOfType("X").Generate(5);

        accounts.ShouldAllBe(a => a.Owner == "X" && a.Ticket.Code == "X");
        accounts.ShouldAllBe(a => a.Ticket.Priority >= 1); // other types are generated as usual
    }

    [Test]
    public void A_factory_draws_from_the_faker_and_is_reproducible()
    {
        var first = Make().WithEveryMemberOfType<string>(f => "S" + f.Random.Int(100, 999)).Generate(10);
        var second = Make().WithEveryMemberOfType<string>(f => "S" + f.Random.Int(100, 999)).Generate(10);

        first.Select(a => a.Owner).ShouldBe(second.Select(a => a.Owner));
        first.Select(a => a.Owner).Distinct().Count().ShouldBeGreaterThan(3);
        first.ShouldAllBe(a => a.Ticket.Code.StartsWith('S'));
    }

    [Test]
    public void The_nullable_form_of_the_type_is_matched_too()
    {
        var accounts = Make().WithEveryMemberOfType(LoyaltyTier.Gold).WithEveryMemberOfType(7).Generate(5);

        accounts.ShouldAllBe(a => a.Tier == LoyaltyTier.Gold && a.Next == LoyaltyTier.Gold && a.Seats == 7);
    }

    [Test]
    public void A_rule_for_the_member_wins_over_the_type_rule()
    {
        var accounts = Make().WithEveryMemberOfType("X").With(a => a.Owner, "Owner").Generate(3);

        accounts.ShouldAllBe(a => a.Owner == "Owner" && a.Ticket.Code == "X");
    }

    [Test]
    public void Elements_of_collections_are_not_members_so_they_keep_their_values()
    {
        var accounts = Make().WithEveryMemberOfType("X").Generate(5);

        accounts.SelectMany(a => a.Tags).ShouldNotBeEmpty();
        accounts.SelectMany(a => a.Tags).ShouldAllBe(t => t != "X");
    }

    [Test]
    public void Strict_mode_counts_a_type_rule_as_covering_the_members()
    {
        Fake.For<Ticket>().Strict().WithEveryMemberOfType("code").WithEveryMemberOfType(1).WithEveryMemberOfType(1m).Generate(2).ShouldAllBe(t => t.Code == "code");
    }

    [Test]
    public void VerifyRules_reports_a_type_nothing_in_the_graph_has_by_the_name_of_the_call()
    {
        var generator = Make().WithEveryMemberOfType(Guid.Empty).VerifyRules();

        Should.Throw<InvalidOperationException>(() => generator.Generate(3)).Message.ShouldContain("WithEveryMemberOfType<Guid>");
    }

    [Test]
    public void Generated_models_get_the_same_values()
    {
        var members = TestFakes.Member.Seed(3).WithEveryMemberOfType(5m).WithEveryMemberOfType("S").Generate(3);

        members.ShouldAllBe(m => m.Balance == 5m && m.Nickname == "S" && m.Address.City == "S");
    }

    [Test]
    public void The_factory_is_validated()
    {
        Should.Throw<ArgumentNullException>(() => Make().WithEveryMemberOfType<string>((Func<Faker, string>)null!));
    }

    [Test]
    public void An_app_wide_type_convention_applies_to_every_generator_and_the_generators_own_comes_first()
    {
        Make().Generate(3).ShouldAllBe(a => a.Plan == SupportPlan.Premium);
        Make().WithEveryMemberOfType(SupportPlan.Basic).Generate(3).ShouldAllBe(a => a.Plan == SupportPlan.Basic);
    }
}
