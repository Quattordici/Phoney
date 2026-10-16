namespace Phoney.Tests;

/// <summary><c>Generate(min, max)</c> and <c>Fake.Many&lt;T&gt;(min, max)</c>: a number of objects chosen from a range, from the seed.</summary>
public sealed class RandomCountTests
{
    private static ObjectGenerator<Ticket> Tickets(long seed) => Fake.For<Ticket>().Seed(seed);

    [Test]
    public void The_count_is_inside_the_range_and_reaches_both_ends()
    {
        var generator = Tickets(1);

        var counts = Enumerable.Range(0, 300).Select(_ => generator.Generate(2, 5).Count).ToList();

        counts.Min().ShouldBe(2);
        counts.Max().ShouldBe(5);
    }

    [Test]
    public void The_same_seed_gives_the_same_counts_and_objects()
    {
        string Run(long seed)
        {
            var generator = Tickets(seed);
            return string.Join("|", Enumerable.Range(0, 20).Select(_ => string.Join(",", generator.Generate(0, 6).Select(t => t.Code))));
        }

        Run(8).ShouldBe(Run(8));
        Run(8).ShouldNotBe(Run(9));
    }

    [Test]
    public void The_objects_are_those_of_a_fixed_count_so_the_range_does_not_move_item_n()
    {
        var ranged = Tickets(5).Generate(3, 12);
        var fixedCount = Tickets(5).Generate(ranged.Count);
        var minimum = Tickets(5).Generate(3);

        ranged.Select(t => t.Code).ShouldBe(fixedCount.Select(t => t.Code));
        ranged.Take(3).Select(t => t.Code).ShouldBe(minimum.Select(t => t.Code));
    }

    [Test]
    public void The_position_in_the_sequence_continues_over_calls()
    {
        var generator = Tickets(5);

        var first = generator.Generate(2, 6);
        var second = generator.Generate(2, 6);

        // The second call continues where the first stopped, like Generate(count) does.
        var all = Tickets(5).Generate(first.Count + second.Count);
        first.Concat(second).Select(t => t.Code).ShouldBe(all.Select(t => t.Code));
    }

    [Test]
    public void An_equal_minimum_and_maximum_is_that_exact_count_and_zero_is_an_empty_list()
    {
        Tickets(2).Generate(4, 4).Count.ShouldBe(4);
        Tickets(2).Generate(0, 0).ShouldBeEmpty();
    }

    [Test]
    public void Bad_ranges_are_refused_with_the_name_of_the_argument()
    {
        int negative = -1, five = 5, four = 4, three = 3; // not constants: PHONEY011 reports constant mistakes at compile time
        Should.Throw<ArgumentOutOfRangeException>(() => Tickets(1).Generate(negative, three)).ParamName.ShouldBe("min");
        Should.Throw<ArgumentOutOfRangeException>(() => Tickets(1).Generate(five, four)).ParamName.ShouldBe("max");
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Many<Ticket>(negative, three)).ParamName.ShouldBe("min");
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Many<Ticket>(five, four)).ParamName.ShouldBe("max");
    }

    [Test]
    public void Fake_Many_with_a_range_is_reproducible_inside_a_seeded_scope()
    {
        string Run()
        {
            using var _ = Fake.Seeded(77);
            return string.Join("|", Fake.Many<Ticket>(1, 8).Select(t => t.Code));
        }

        Run().ShouldBe(Run());
        using var scope = Fake.Seeded(77);
        Fake.Many<Ticket>(1, 8).Count.ShouldBeInRange(1, 8);
    }

    [Test]
    public void Parallel_calls_each_get_a_valid_count()
    {
        var generator = Tickets(3);

        var counts = Enumerable.Range(0, 200).AsParallel().Select(_ => generator.Generate(1, 4).Count).ToList();

        counts.ShouldAllBe(c => c >= 1 && c <= 4);
    }
}
