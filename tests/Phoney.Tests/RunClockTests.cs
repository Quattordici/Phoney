using System.Reflection;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>
/// Which "now" a generator's Faker computes relative dates from: an explicit reference date, else the clock, else (once seeded) the default seeded date.
/// One table of cases, run against every place that builds a Faker for a run.
/// </summary>
public sealed class RunClockTests
{
    private static readonly DateTimeOffset Explicit = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2031, 3, 15, 12, 0, 0, TimeSpan.Zero);

    // CreatedAt of row 0 of Seed(1), by the reference date it is relative to.
    private static readonly Dictionary<DateTimeOffset, string> FirstRowOf = new()
    {
        [Faker.DefaultSeededReferenceDate] = "2024-12-06T08:51:15.4910000Z",
        [Now] = "2031-02-17T22:32:06.1020000Z",
        [Explicit] = "2019-12-06T10:32:06.1020000Z",
    };

    private sealed class Stamped
    {
        public DateTimeOffset Seen { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    private static void Row(DateTime createdAt) { }

    private static ArgumentGenerator Arguments() =>
        Fake.Arguments(typeof(RunClockTests).GetMethod(nameof(Row), BindingFlags.NonPublic | BindingFlags.Static)!);

    private static ObjectGenerator<Stamped> Objects() =>
        Fake.For<Stamped>().With(x => x.Seen, (Faker faker) => faker.ReferenceDate);

    /// <summary>The configurations that matter: (seeded, explicit date, clock) and the date they must give.</summary>
    public static IEnumerable<(bool Seeded, bool Date, bool Clock, DateTimeOffset Expected)> Cases()
    {
        yield return (true, false, false, Faker.DefaultSeededReferenceDate);
        yield return (true, false, true, Now);
        yield return (true, true, false, Explicit);
        yield return (true, true, true, Explicit);
        yield return (false, false, true, Now);
        yield return (false, true, false, Explicit);
        yield return (false, true, true, Explicit);
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public void An_object_generator_uses_the_documented_order(bool seeded, bool date, bool clock, DateTimeOffset expected)
    {
        var generator = Objects();
        if (seeded)
            generator = generator.Seed(1);
        if (date)
            generator = generator.ReferenceDate(Explicit);
        if (clock)
            generator = generator.TimeProvider(new ManualTimeProvider(Now));

        generator.Generate().Seen.ShouldBe(expected);
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public void An_argument_generator_uses_the_documented_order(bool seeded, bool date, bool clock, DateTimeOffset expected)
    {
        if (!seeded)
            return; // unseeded rows differ from run to run; the unseeded orders are covered by the object generator and the DI factory

        var generator = Arguments().Seed(1);
        if (date)
            generator = generator.ReferenceDate(Explicit);
        if (clock)
            generator = generator.TimeProvider(new ManualTimeProvider(Now));

        var rows = generator.Generate(5);
        rows.ShouldBe(Arguments().Seed(1).ReferenceDate(expected).Generate(5), new RowComparer());
        ((DateTime)rows[0]![0]!).ToString("O").ShouldBe(FirstRowOf[expected]); // the exact value of row 0 for seed 1, so a change in both paths shows too
    }

    [Test]
    public void A_nested_generator_inherits_the_parent_date_unless_it_has_its_own()
    {
        var inner = Fake.For<Stamped>().With(x => x.Seen, (Faker faker) => faker.ReferenceDate);
        var outer = Fake.For<Holder>().ReferenceDate(Explicit).With(x => x.Inner, inner);
        outer.Generate().Inner.Seen.ShouldBe(Explicit);

        var own = Fake.For<Holder>().ReferenceDate(Explicit).With(x => x.Inner, inner.ReferenceDate(Now));
        own.Generate().Inner.Seen.ShouldBe(Now);
    }

    [Test]
    public void A_clock_of_a_nested_generator_wins_over_the_parent_date()
    {
        var inner = Fake.For<Stamped>().With(x => x.Seen, (Faker faker) => faker.ReferenceDate).TimeProvider(new ManualTimeProvider(Now));
        Fake.For<Holder>().ReferenceDate(Explicit).With(x => x.Inner, inner).Generate().Inner.Seen.ShouldBe(Now);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Holder
    {
        public Stamped Inner { get; set; } = new();
    }

    private sealed class RowComparer : IEqualityComparer<object?[]>
    {
        public bool Equals(object?[]? x, object?[]? y) => x!.SequenceEqual(y!);

        public int GetHashCode(object?[] obj) => obj.Length;
    }
}
