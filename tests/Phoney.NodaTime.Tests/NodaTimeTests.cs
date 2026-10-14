using NodaTime;
using Phoney.Generation;
using Phoney.NodaTime;

namespace Phoney.NodaTime.Tests;

// Generated through the reflection model: no [FakeFor] reaches it.
public sealed class Subscription
{
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
    public ZonedDateTime ExpiresAt { get; set; }
    public LocalDate BirthDate { get; set; }
    public int Age { get; set; }
    public LocalDateTime? StartDate { get; set; }
    public LocalTime OpensAt { get; set; }
    public Duration Length { get; set; }
    public List<Instant> Timestamps { get; set; } = [];
}

public sealed class Booking
{
    public string FirstName { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public Instant? UpdatedAt { get; set; }
    public OffsetDateTime DueDate { get; set; }
    public LocalDate DateOfBirth { get; set; }
    public int Age { get; set; }
    public LocalTime Time { get; set; }
    public Duration Duration { get; set; }
    public Instant[] Events { get; set; } = [];
}

public sealed class Unnamed
{
    public Instant Foo { get; set; }
}

[FakeFor<Booking>]
public static partial class TestData;

public sealed class NodaTimeTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Instant Now = Instant.FromDateTimeOffset(Reference);

    [Test]
    public void Values_are_relative_to_the_reference_date()
    {
        var faker = new Faker(seed: 1) { ReferenceDate = Reference };
        var noda = faker.Noda();

        noda.Instant.Past().ShouldBeInRange(Now - Duration.FromDays(366), Now);
        noda.Instant.Future(2).ShouldBeInRange(Now, Now + Duration.FromDays(2 * 366));
        noda.Instant.Recent(3).ShouldBeInRange(Now - Duration.FromDays(3), Now);
        noda.Instant.Soon().ShouldBeInRange(Now, Now + Duration.FromDays(1));
        noda.LocalDate.Birthdate(30, 30).Year.ShouldBeInRange(1994, 1995);
        noda.LocalDate.Past().ShouldBeLessThanOrEqualTo(new LocalDate(2025, 6, 1));
        noda.LocalDateTime.Future().ShouldBeGreaterThan(new LocalDateTime(2025, 6, 1, 0, 0));
        var zone = DateTimeZoneProviders.Tzdb["Europe/Stockholm"];
        noda.ZonedDateTime.Past(zone: zone).Zone.ShouldBe(zone);
        noda.TimeZone().ShouldNotBeNull();
        noda.Duration(Duration.FromHours(1), Duration.FromHours(2)).ShouldBeInRange(Duration.FromHours(1), Duration.FromHours(2));
        noda.Period(1, 5).Days.ShouldBeInRange(1, 5);
        _ = noda.TimeOfDay();
    }

    [Test]
    public void Values_match_the_built_in_equivalents_for_a_seed()
    {
        new Faker(seed: 4).Noda().Instant.Past().ShouldBe(Instant.FromDateTimeOffset(new Faker(seed: 4).Date.Past()));
        new Faker(seed: 4).Noda().LocalDate.Birthdate().ShouldBe(LocalDate.FromDateOnly(new Faker(seed: 4).Date.Birthdate()));
        new Faker(seed: 4).Noda().TimeZone().ShouldBe(new Faker(seed: 4).Noda().TimeZone());
    }

    [Test]
    public void Arguments_are_validated()
    {
        var noda = new Faker().Noda();
        Should.Throw<ArgumentException>(() => noda.Instant.Between(Now, Now - Duration.FromDays(1)));
        Should.Throw<ArgumentException>(() => noda.Duration(Duration.FromHours(2), Duration.FromHours(1)));
        Should.Throw<ArgumentException>(() => noda.Period(5, 1));
    }

    [Test]
    public void Objects_get_NodaTime_members_that_follow_the_conventions()
    {
        var items = Fake.For<Subscription>().Seed(2).ReferenceDate(Reference).Generate(20);

        foreach (var s in items)
        {
            s.CreatedAt.ShouldBeLessThanOrEqualTo(s.UpdatedAt);
            s.UpdatedAt.ShouldBeLessThanOrEqualTo(Now);
            s.ExpiresAt.ToInstant().ShouldBeGreaterThan(Now);
            s.Age.ShouldBe(Period.Between(s.BirthDate, new LocalDate(2025, 6, 1), PeriodUnits.Years).Years);
            s.Age.ShouldBeInRange(18, 80);
            s.StartDate.ShouldNotBeNull();
            s.Length.ShouldNotBe(Duration.Zero);
            s.Timestamps.ShouldNotBeEmpty();
            s.Timestamps.ShouldAllBe(t => t != default);
        }
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data()
    {
        var generated = TestData.Booking.Seed(7).ReferenceDate(Reference).Generate(25);
        var reflected = new ObjectGenerator<Booking>(new ReflectionModel<Booking>()).Seed(7).ReferenceDate(Reference).Generate(25);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
        generated.ShouldAllBe(b => b.CreatedAt != default && b.DueDate.ToInstant() > Now && b.Events.Length > 0);

        static string Describe(Booking b) =>
            $"{b.FirstName}|{b.CreatedAt}|{b.UpdatedAt}|{b.DueDate}|{b.DateOfBirth}|{b.Age}|{b.Time}|{b.Duration}|{string.Join(",", b.Events)}";
    }

    [Test]
    public void Populate_keeps_set_values_and_fills_the_rest()
    {
        var created = Instant.FromUtc(2020, 1, 1, 0, 0);
        var booking = TestData.Booking.Seed(1).Populate(new Booking { CreatedAt = created });

        booking.CreatedAt.ShouldBe(created);
        booking.DueDate.ShouldNotBe(default);
    }

    [Test]
    public void Strict_mode_counts_NodaTime_members()
    {
#pragma warning disable PHONEY004 // the runtime check is tested here
        Should.Throw<InvalidOperationException>(() => Fake.For<Unnamed>().Strict().Generate()).Message.ShouldContain("Foo");
#pragma warning restore PHONEY004
        Fake.For<Subscription>().Strict().Ignore(s => s.Length).Ignore(s => s.OpensAt).Generate().CreatedAt.ShouldNotBe(default);
    }
}
