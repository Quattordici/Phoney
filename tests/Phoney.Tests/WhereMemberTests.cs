namespace Phoney.Tests;

/// <summary><c>Where(x =&gt; x.Member, condition)</c> generates just that member again until the condition holds.</summary>
public sealed class WhereMemberTests
{
    private static ObjectGenerator<Ticket> Make() => Fake.For<Ticket>().Seed(11);

    [Test]
    public void The_member_is_generated_again_until_it_satisfies_the_condition()
    {
        var tickets = Make().Where(t => t.Priority, p => p >= 5).Generate(60);

        tickets.Select(t => t.Priority).Distinct().Order().ShouldBe([5, 6]);
        tickets.ShouldAllBe(t => t.Code.Length > 0 && t.Price > 0); // the other members are generated as usual
    }

    [Test]
    public void Results_are_reproducible_and_do_not_depend_on_how_they_are_generated()
    {
        string Describe(Ticket t) => $"{t.Priority}|{t.Code}|{t.Price}";

        var sequential = Make().Where(t => t.Code, c => c.Length > 7).Generate(30).Select(Describe).ToList();
        var parallel = Make().Where(t => t.Code, c => c.Length > 7).GenerateParallel(30).Select(Describe).ToList();

        parallel.ShouldBe(sequential);
        Make().Where(t => t.Code, c => c.Length > 7).Generate(30).Select(Describe).ShouldBe(sequential);
        sequential.ShouldAllBe(d => d.Split('|')[1].Length > 7);
    }

    [Test]
    public void It_filters_what_a_rule_for_the_member_produces_whichever_call_comes_first()
    {
        var after = Make().With(t => t.Priority, f => f.Random.Int(1, 100)).Where(t => t.Priority, p => p % 10 == 0).Generate(20);
        var before = Make().Where(t => t.Priority, p => p % 10 == 0).With(t => t.Priority, f => f.Random.Int(1, 100)).Generate(20);

        after.ShouldAllBe(t => t.Priority % 10 == 0);
        before.Select(t => t.Priority).ShouldBe(after.Select(t => t.Priority));
    }

    [Test]
    public void Several_conditions_on_one_member_must_all_hold_and_the_smallest_limit_applies()
    {
        Make().Where(t => t.Priority, p => p >= 3).Where(t => t.Priority, p => p <= 4).Generate(40)
            .Select(t => t.Priority).Distinct().Order().ShouldBe([3, 4]);

        var impossible = Make().Where(t => t.Priority, p => p > 6, 500).Where(t => t.Priority, p => p > 6, 9);
        Should.Throw<InvalidOperationException>(() => impossible.Generate()).Message.ShouldContain("9 attempts");
    }

    [Test]
    public void A_condition_nothing_can_satisfy_names_the_member_and_says_what_to_do()
    {
        var generator = Make().Where(t => t.Priority, p => p > 6, 20);

        var message = Should.Throw<InvalidOperationException>(() => generator.Generate()).Message;

        message.ShouldContain("Ticket.Priority");
        message.ShouldContain("20 attempts");
        message.ShouldContain("With");
    }

    [Test]
    public void It_combines_with_Unique_so_values_are_both_accepted_and_distinct()
    {
        var tickets = Make().Unique(t => t.Code).Where(t => t.Code, c => c.Length > 5).Generate(40);

        tickets.Select(t => t.Code).Distinct().Count().ShouldBe(40);
        tickets.ShouldAllBe(t => t.Code.Length > 5);
    }

    [Test]
    public void Members_that_are_not_generated_cannot_be_filtered()
    {
        var ignored = Make().Ignore(t => t.Priority).Where(t => t.Priority, p => p > 0);
        var ignoredLater = Make().Where(t => t.Priority, p => p > 0).Ignore(t => t.Priority);
        var dependent = Make().With(t => t.Code, (_, t) => "P" + t.Priority).Where(t => t.Code, c => c.Length > 1);

        Should.Throw<InvalidOperationException>(() => ignored.Generate()).Message.ShouldContain("Ticket.Priority");
        Should.Throw<InvalidOperationException>(() => ignoredLater.Generate()).Message.ShouldContain("ignored");
        Should.Throw<InvalidOperationException>(() => dependent.Generate()).Message.ShouldContain("depends on the object");
    }

    [Test]
    public void It_cannot_be_combined_with_CreateWith_because_the_factory_decides_the_values()
    {
        var generator = Make().CreateWith(_ => new Ticket { Code = "fixed" }).Where(t => t.Priority, p => p > 0);

        Should.Throw<InvalidOperationException>(() => generator.Generate()).Message.ShouldContain("CreateWith");
    }

    [Test]
    public void Members_of_nested_objects_are_rejected_because_the_condition_belongs_to_the_generated_object()
    {
#pragma warning disable PHONEY007 // the analyzer reports this call at compile time, and the test pins what happens at runtime
        Should.Throw<ArgumentException>(() => Fake.For<Parcel>().Where(p => p.Ticket.Priority, v => v > 1)).Message.ShouldContain("directly");
#pragma warning restore PHONEY007
    }

    [Test]
    public void Arguments_are_validated()
    {
        var none = 0; // not a constant, which PHONEY011 would report
        Should.Throw<ArgumentNullException>(() => Make().Where(t => t.Priority, null!));
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Where(t => t.Priority, _ => true, none));
    }

    [Test]
    public void A_nested_generator_filters_its_own_members()
    {
        var parcels = Fake.For<Parcel>().Seed(2).With(p => p.Ticket, Make().Where(t => t.Priority, p => p == 1)).Generate(20);

        parcels.ShouldAllBe(p => p.Ticket.Priority == 1);
    }

    [Test]
    public void Populate_filters_the_values_it_generates_and_keeps_the_others()
    {
        var generator = Make().Where(t => t.Priority, p => p >= 5);

        generator.Populate(new Ticket { Priority = 2 }).Priority.ShouldBe(2); // kept as it is
        generator.Populate(Enumerable.Range(0, 20).Select(_ => new Ticket()).ToList()).ShouldAllBe(t => t.Priority >= 5);
    }

    [Test]
    public void Invalid_and_boundary_cases_are_not_filtered_because_they_are_made_to_break_the_member()
    {
        var generator = Make().Where(t => t.Priority, p => p is >= 1 and <= 6);

        generator.InvalidCases().Select(c => c.Value.Priority).ShouldContain(0);
        generator.InvalidCases().Select(c => c.Value.Priority).ShouldContain(7);
    }

    [Test]
    public void It_can_be_combined_with_a_condition_on_the_whole_object()
    {
        var tickets = Make().Where(t => t.Priority, p => p >= 4).Where(t => t.Price > 500m).Generate(20);

        tickets.ShouldAllBe(t => t.Priority >= 4 && t.Price > 500m);
    }

    [Test]
    public void Generated_models_filter_the_same_way()
    {
        var members = TestFakes.Member.Seed(5).Where(m => m.Tags, t => t.Length >= 2).Generate(20);

        members.ShouldAllBe(m => m.Tags.Length >= 2);
        TestFakes.Member.Seed(5).Where(m => m.Tags, t => t.Length >= 2).Generate(20).Select(m => m.Email).ShouldBe(members.Select(m => m.Email));
    }

    [Test]
    public void Members_generated_from_the_objects_identity_have_one_value_and_cannot_be_drawn_again()
    {
        var reflection = Fake.For<Counterparty>().Where(c => c.City, city => city.Length > 3);
        var generated = TestFakes.Member.Where(m => m.FirstName, n => n.Length > 3);

        Should.Throw<InvalidOperationException>(() => reflection.Generate()).Message.ShouldContain("identity");
        Should.Throw<InvalidOperationException>(() => generated.Generate()).Message.ShouldContain("Member.FirstName");

        // With a rule of its own the member is just a value again.
        TestFakes.Member.Seed(1).With(m => m.FirstName, f => f.Person.FirstName()).Where(m => m.FirstName, n => n.Length > 5).Generate(10)
            .ShouldAllBe(m => m.FirstName.Length > 5);
    }
}
