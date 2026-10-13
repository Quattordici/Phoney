using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>A <see cref="TimeProvider"/> as the source of "now" for relative dates.</summary>
public sealed class TimeProviderTests
{
    private static readonly DateTimeOffset Now = new(2031, 3, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Faker_computes_relative_dates_from_the_time_provider()
    {
        var clock = new ManualTimeProvider(Now);
        var faker = new Faker("en", seed: 1) { TimeProvider = clock };

        faker.ReferenceDate.ShouldBe(Now);
        faker.Date.Past().ShouldBeInRange(Now.AddYears(-1), Now);
        var birthdate = faker.Date.Birthdate(18, 20);
        birthdate.ShouldBeInRange(DateOnly.FromDateTime(Now.AddYears(-21).UtcDateTime), DateOnly.FromDateTime(Now.AddYears(-18).UtcDateTime));

        clock.Advance(TimeSpan.FromDays(400));
        faker.ReferenceDate.ShouldBe(Now.AddDays(400));
    }

    [Test]
    public void Explicit_reference_date_wins_over_the_time_provider_which_wins_over_the_seeded_default()
    {
        var explicitDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(Now);

        new Faker(seed: 1) { TimeProvider = clock, ReferenceDate = explicitDate }.ReferenceDate.ShouldBe(explicitDate);
        new Faker(seed: 1) { TimeProvider = clock }.ReferenceDate.ShouldBe(Now);
        new Faker { TimeProvider = clock }.Seed(2).ReferenceDate.ShouldBe(Now);
        new Faker(seed: 1).ReferenceDate.ShouldBe(Faker.DefaultSeededReferenceDate);
        new Faker { TimeProvider = TimeProvider.System }.ReferenceDate.ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Test]
    public void Generators_use_the_time_provider_for_the_timeline_and_ages()
    {
        var clock = new ManualTimeProvider(Now);
        var customers = Fake.For<Customer>().Seed(3).TimeProvider(clock).Generate(20);

        customers.ShouldAllBe(c => c.CreatedAt <= Now.UtcDateTime && c.CreatedAt > Now.AddYears(-2).UtcDateTime);
        customers.ShouldAllBe(c => c.Age == Identity.AgeAt(c.DateOfBirth, DateOnly.FromDateTime(Now.UtcDateTime)));

        // An explicit reference date still wins.
        var explicitDate = new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Fake.For<Customer>().Seed(3).TimeProvider(clock).ReferenceDate(explicitDate).Generate()
            .CreatedAt.ShouldBeLessThanOrEqualTo(explicitDate.UtcDateTime);
    }

    [Test]
    public void Same_seed_and_time_gives_the_same_objects()
    {
        var a = Fake.For<Customer>().Seed(5).TimeProvider(new ManualTimeProvider(Now)).Generate(5);
        var b = Fake.For<Customer>().Seed(5).ReferenceDate(Now).Generate(5);

        b.Select(c => (c.Email, c.CreatedAt, c.DateOfBirth)).ShouldBe(a.Select(c => (c.Email, c.CreatedAt, c.DateOfBirth)));
    }

    [Test]
    public void Argument_generators_use_the_time_provider()
    {
        var row = new ArgumentGenerator([new FakeArgument("createdAt", typeof(DateTimeOffset))])
            .Seed(1).TimeProvider(new ManualTimeProvider(Now)).Generate();

        ((DateTimeOffset)row[0]!).ShouldBeInRange(Now.AddYears(-2), Now);
    }

    [Test]
    [NotInParallel] // changes the process-wide facade clock
    public void The_facade_uses_its_time_provider()
    {
        try
        {
            Fake.TimeProvider = new ManualTimeProvider(Now);
            Fake.Faker.ReferenceDate.ShouldBe(Now);
            Fake.One<Customer>().CreatedAt.ShouldBeLessThanOrEqualTo(Now.UtcDateTime);
        }
        finally
        {
            Fake.TimeProvider = null;
        }

        Fake.Faker.ReferenceDate.ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    /// <summary>A clock that only moves when told to.</summary>
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
