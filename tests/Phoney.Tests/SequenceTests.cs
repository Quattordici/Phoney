namespace Phoney.Tests;

/// <summary><c>Sequence</c> and <c>Cycle</c> give a member a value that depends on the object's position in the generator's sequence.</summary>
public sealed class SequenceTests
{
    private static ObjectGenerator<Ticket> Make() => Fake.For<Ticket>().Seed(21);

    [Test]
    public void Sequence_numbers_the_objects_from_zero_and_continues_over_calls()
    {
        var generator = Make().Sequence(t => t.Code, i => $"C{i + 1:D3}");

        generator.Generate(3).Select(t => t.Code).ShouldBe(["C001", "C002", "C003"]);
        generator.Generate().Code.ShouldBe("C004");
        generator.Stream().Take(2).Select(t => t.Code).ShouldBe(["C005", "C006"]);
    }

    [Test]
    public void The_value_of_item_n_does_not_depend_on_how_the_items_are_generated()
    {
        var sequential = Make().Sequence(t => t.Code, i => "N" + i).Generate(25).Select(t => t.Code).ToList();
        var parallel = Make().Sequence(t => t.Code, i => "N" + i).GenerateParallel(25).Select(t => t.Code).ToList();
        var batches = Make().Sequence(t => t.Code, i => "N" + i);
        var split = batches.Generate(10).Concat(batches.Generate(15)).Select(t => t.Code).ToList();

        parallel.ShouldBe(sequential);
        split.ShouldBe(sequential);
        sequential.ShouldBe(Enumerable.Range(0, 25).Select(i => "N" + i));
    }

    [Test]
    public void The_seed_does_not_change_the_sequence_but_changes_the_other_members()
    {
        var one = Fake.For<Ticket>().Seed(1).Sequence(t => t.Code, i => "N" + i).Generate(5);
        var two = Fake.For<Ticket>().Seed(2).Sequence(t => t.Code, i => "N" + i).Generate(5);

        one.Select(t => t.Code).ShouldBe(two.Select(t => t.Code));
        one.Select(t => t.Price).ShouldNotBe(two.Select(t => t.Price));
    }

    [Test]
    public void Cycle_repeats_the_values_in_order()
    {
        Make().Cycle(t => t.Priority, [3, 1, 2]).Generate(7).Select(t => t.Priority).ShouldBe([3, 1, 2, 3, 1, 2, 3]);
    }

    [Test]
    public void Objects_that_a_condition_rejects_are_generated_again_at_their_position_so_the_numbers_stay_contiguous()
    {
        var tickets = Make().Sequence(t => t.Code, i => "N" + i).Where(t => t.Priority >= 4).Generate(12);

        tickets.Select(t => t.Code).ShouldBe(Enumerable.Range(0, 12).Select(i => "N" + i));
    }

    [Test]
    public void A_later_rule_for_the_member_replaces_it_and_the_other_way_round()
    {
#pragma warning disable PHONEY008 // replacing the earlier rule is what this test is about
        Make().Sequence(t => t.Code, i => "N" + i).With(t => t.Code, "fixed").Generate(2).ShouldAllBe(t => t.Code == "fixed");
        Make().With(t => t.Code, "fixed").Sequence(t => t.Code, i => "N" + i).Generate(2).Select(t => t.Code).ShouldBe(["N0", "N1"]);
#pragma warning restore PHONEY008
    }

    [Test]
    public void It_applies_to_members_that_populate_fills_in_and_to_objects_made_by_CreateWith()
    {
        var generator = Make().Sequence(t => t.Code, i => "N" + i);

        generator.Populate([new Ticket(), new Ticket { Code = "kept" }, new Ticket()]).Select(t => t.Code).ShouldBe(["N0", "kept", "N2"]);
        Make().CreateWith(_ => new Ticket { Code = "factory" }).Sequence(t => t.Code, i => "N" + i).Generate(2).Select(t => t.Code).ShouldBe(["N0", "N1"]);
    }

    [Test]
    public void Strict_mode_counts_it_as_a_rule()
    {
        Make().Strict().Sequence(t => t.Code, i => "N" + i).With(t => t.Priority, 1).With(t => t.Price, 1m).Generate(2).Count.ShouldBe(2);
    }

    [Test]
    public void Generated_models_get_the_same_values()
    {
        var members = TestFakes.Member.Seed(4)
            .Sequence(m => m.Username, i => "user" + i)
            .Cycle(m => m.Gender, [Gender.Female, Gender.Male])
            .Generate(4);

        members.Select(m => m.Username).ShouldBe(["user0", "user1", "user2", "user3"]);
        members.Select(m => m.Gender).ShouldBe([Gender.Female, Gender.Male, Gender.Female, Gender.Male]);
    }

    [Test]
    public void Arguments_are_validated()
    {
        Should.Throw<ArgumentNullException>(() => Make().Sequence<string>(t => t.Code, null!));
        Should.Throw<ArgumentNullException>(() => Make().Cycle(t => t.Priority, null!));
        Should.Throw<ArgumentException>(() => Make().Cycle(t => t.Priority, []));
    }

    [Test]
    public void Members_of_nested_objects_are_rejected_because_the_position_belongs_to_the_generated_object()
    {
#pragma warning disable PHONEY007 // the analyzer reports these calls at compile time, and the test pins what happens at runtime
        Should.Throw<ArgumentException>(() => Fake.For<Parcel>().Sequence(p => p.Ticket.Code, i => "N" + i)).Message.ShouldContain("directly");
        Should.Throw<ArgumentException>(() => Fake.For<Parcel>().Cycle(p => p.Ticket.Priority, [1, 2])).Message.ShouldContain("directly");
#pragma warning restore PHONEY007
    }

    [Test]
    public void A_generator_with_sequences_cannot_be_nested_because_every_nested_object_would_be_number_zero()
    {
        var numbered = Make().Sequence(t => t.Code, i => "N" + i);

        Should.Throw<ArgumentException>(() => Fake.For<Parcel>().With(p => p.Ticket, numbered)).Message.ShouldContain("Sequence");
        Should.Throw<ArgumentException>(() => Fake.For<Parcel>().WithGeneratorFor(numbered)).Message.ShouldContain("Sequence");
        Should.Throw<ArgumentException>(() => Fake.Generators.Add(numbered)).Message.ShouldContain("Sequence");
#pragma warning disable PHONEY008 // the replaced Cycle is the point: a generator without positional rules can be nested
        Fake.For<Parcel>().Seed(1).WithGeneratorFor(Make().Cycle(t => t.Priority, [1]).With(t => t.Priority, 2)).Generate(2).ShouldAllBe(p => p.Ticket.Priority == 2); // the rule was replaced
#pragma warning restore PHONEY008
    }

    [Test]
    public void Invalid_and_boundary_cases_still_work_for_a_generator_with_sequences()
    {
        Make().Sequence(t => t.Code, i => "N" + i).InvalidCases().ShouldNotBeEmpty();
    }
}
