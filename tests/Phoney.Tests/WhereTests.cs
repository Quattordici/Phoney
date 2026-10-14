using System.ComponentModel.DataAnnotations;

namespace Phoney.Tests;

public sealed class Ticket
{
    [Range(1, 6)] public int Priority { get; set; }
    public string Code { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed class Parcel
{
    public string Label { get; set; } = "";
    public Ticket Ticket { get; set; } = null!;
    public List<Ticket> Extras { get; set; } = [];
}

/// <summary><c>Where</c> keeps only objects that satisfy a condition, by generating again.</summary>
public sealed class WhereTests
{
    private static ObjectGenerator<Ticket> Make() => Fake.For<Ticket>().Seed(7);

    private static string Describe(Ticket t) => $"{t.Priority}|{t.Code}|{t.Price}";

    [Test]
    public void Only_objects_that_satisfy_the_condition_are_returned()
    {
        var tickets = Make().Where(t => t.Priority >= 5).Generate(50);

        tickets.Count.ShouldBe(50);
        tickets.ShouldAllBe(t => t.Priority >= 5);
        tickets.Select(t => t.Priority).Distinct().Order().ShouldBe([5, 6]);
    }

    [Test]
    public void An_object_that_already_satisfies_the_condition_is_the_one_Generate_gives_without_it()
    {
        var plain = Make().Generate(60);
        var filtered = Make().Where(t => t.Priority >= 3).Generate(60);

        var kept = 0;
        for (var i = 0; i < plain.Count; i++)
        {
            if (plain[i].Priority < 3)
                continue;
            Describe(filtered[i]).ShouldBe(Describe(plain[i]));
            kept++;
        }

        kept.ShouldBeGreaterThan(10);
    }

    [Test]
    public void Results_depend_on_the_seed_and_position_only()
    {
        // Every generator instance continues its own sequence, so each run starts from a fresh one.
        var sequential = Make().Where(t => t.Priority >= 5).Generate(40).Select(Describe).ToList();
        var parallel = Make().Where(t => t.Priority >= 5).GenerateParallel(40).Select(Describe).ToList();
        var batches = Make().Where(t => t.Priority >= 5);
        var split = batches.Generate(15).Concat(batches.Generate(25)).Select(Describe).ToList();
        var streamed = Make().Where(t => t.Priority >= 5).Stream().Take(40).Select(Describe).ToList();

        parallel.ShouldBe(sequential);
        split.ShouldBe(sequential);
        streamed.ShouldBe(sequential);
        sequential.Distinct().Count().ShouldBeGreaterThan(30); // retries draw new values, they don't repeat the same object
    }

    [Test]
    public void The_condition_sees_the_finished_object_including_dependent_rules_and_AfterCreate()
    {
        var tickets = Make()
            .With(t => t.Code, (_, t) => "P" + t.Priority)
            .AfterCreate((_, t) => t.Price = t.Priority * 10)
            .Where(t => t.Code == "P6" && t.Price == 60m)
            .Generate(10);

        tickets.ShouldAllBe(t => t.Priority == 6);
    }

    [Test]
    public void Several_conditions_must_all_hold()
    {
        var tickets = Make().Where(t => t.Priority >= 3).Where(t => t.Priority <= 4).Generate(40);

        tickets.Select(t => t.Priority).Distinct().Order().ShouldBe([3, 4]);
    }

    [Test]
    public void A_condition_nothing_can_satisfy_fails_with_a_message_that_says_what_to_do()
    {
        var generator = Make().Where(t => t.Priority > 6, maxAttempts: 25);

        var error = Should.Throw<InvalidOperationException>(() => generator.Generate());

        error.Message.ShouldContain("Ticket");
        error.Message.ShouldContain("25 attempts");
        error.Message.ShouldContain("With");
    }

    [Test]
    public void The_attempt_limit_of_the_strictest_condition_applies()
    {
        var attempts = 0;
        var generator = Make().Where(t => t.Priority > 0).Where(t => ++attempts < 0, maxAttempts: 7).Where(t => true, maxAttempts: 500);

        Should.Throw<InvalidOperationException>(() => generator.Generate()).Message.ShouldContain("7 attempts");
        attempts.ShouldBe(7);
    }

    [Test]
    public void Arguments_are_validated()
    {
        var none = 0; // not a constant, which PHONEY011 would report
        Should.Throw<ArgumentNullException>(() => Make().Where(null!));
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Where(_ => true, maxAttempts: none));
    }

    [Test]
    public void Generators_stay_immutable()
    {
        var all = Make();
        var filtered = all.Where(t => t.Priority == 6);

        filtered.Generate(10).ShouldAllBe(t => t.Priority == 6);
        all.Generate(100).Select(t => t.Priority).Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Test]
    public void A_generator_with_a_condition_filters_the_objects_it_creates_as_a_nested_generator()
    {
        var parcels = Fake.For<Parcel>().Seed(3)
            .With(p => p.Ticket, Make().Where(t => t.Priority == 1))
            .Generate(30);

        parcels.ShouldAllBe(p => p.Ticket.Priority == 1);
    }

    [Test]
    public void A_type_generator_with_a_condition_filters_every_nested_object_of_the_type()
    {
        var parcels = Fake.For<Parcel>().Seed(3)
            .WithGeneratorFor(Make().Where(t => t.Priority >= 5))
            .Generate(30);

        parcels.ShouldAllBe(p => p.Ticket.Priority >= 5);
        parcels.SelectMany(p => p.Extras).ShouldNotBeEmpty();
        parcels.SelectMany(p => p.Extras).ShouldAllBe(t => t.Priority >= 5);
    }

    [Test]
    public void Populate_in_All_mode_tries_again_until_the_object_satisfies_the_condition()
    {
        var generator = Make().Where(t => t.Priority >= 5);

        var filled = generator.Populate(new Ticket(), PopulateMode.All);
        var many = generator.Populate(Enumerable.Range(0, 20).Select(_ => new Ticket()).ToList(), PopulateMode.All);

        filled.Priority.ShouldBeGreaterThanOrEqualTo(5);
        many.ShouldAllBe(t => t.Priority >= 5);
    }

    [Test]
    public void Populate_in_MissingOnly_mode_is_rejected_before_anything_is_touched()
    {
        var generator = Make().Where(t => t.Priority >= 5);
        var ticket = new Ticket { Priority = 2 };

        var error = Should.Throw<InvalidOperationException>(() => generator.Populate(ticket));

        error.Message.ShouldContain("PopulateMode.All");
        ticket.Code.ShouldBe(""); // kept values can't be generated again, so nothing was filled in
    }

    [Test]
    public void Invalid_and_boundary_cases_are_not_filtered_because_they_are_made_to_break_things()
    {
        var generator = Make().Where(t => t.Priority is >= 1 and <= 6);

        generator.InvalidCases().Select(c => c.Value.Priority).ShouldContain(0);
        generator.InvalidCases().Select(c => c.Value.Priority).ShouldContain(7);
    }

    [Test]
    public void Generated_models_filter_the_same_way()
    {
        var members = TestFakes.Member.Seed(5).Where(m => m.Memberships.Count == 3).Generate(20);

        members.ShouldAllBe(m => m.Memberships.Count == 3);
        TestFakes.Member.Seed(5).Where(m => m.Memberships.Count == 3).Generate(20).Select(m => m.Email)
            .ShouldBe(members.Select(m => m.Email));
    }
}
