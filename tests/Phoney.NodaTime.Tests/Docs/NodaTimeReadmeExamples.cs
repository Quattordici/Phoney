using NodaTime;

namespace Phoney.NodaTime.Tests.Docs;

// snippet: noda-subscription
public class Subscription
{
    public Instant CreatedAt { get; set; }      // in the past
    public Instant UpdatedAt { get; set; }      // after CreatedAt
    public ZonedDateTime ExpiresAt { get; set; } // in the future
    public LocalDate BirthDate { get; set; }    // matches Age
    public int Age { get; set; }
}
// end snippet

/// <summary>The NodaTime examples of the main README and the package README.</summary>
public sealed class NodaTimeReadmeExamples
{
    [Test]
    public void Values()
    {
        // snippet: noda-values
        var faker = new Faker("sv", seed: 42);
        Instant created = faker.Noda().Instant.Past();
        LocalDate birthday = faker.Noda().LocalDate.Birthdate(18, 65);
        ZonedDateTime meeting = faker.Noda().ZonedDateTime.Soon(7, DateTimeZoneProviders.Tzdb["Europe/Stockholm"]);
        DateTimeZone zone = faker.Noda().TimeZone();
        // end snippet

        var now = Instant.FromDateTimeOffset(faker.ReferenceDate);
        created.ShouldBeLessThan(now);
        (now.InUtc().Year - birthday.Year).ShouldBeInRange(18, 66);
        meeting.Zone.Id.ShouldBe("Europe/Stockholm");
        meeting.ToInstant().ShouldBeGreaterThan(now);
        DateTimeZoneProviders.Tzdb.Ids.ShouldContain(zone.Id);
    }

    [Test]
    public void Objects_with_NodaTime_members()
    {
        // snippet: noda-subscription-use
        var subscriptions = Fake.Many<Subscription>(10);
        // end snippet

        subscriptions.Count.ShouldBe(10);
        subscriptions.ShouldAllBe(s => s.UpdatedAt >= s.CreatedAt && s.ExpiresAt.ToInstant() > s.CreatedAt);
    }

    [Test]
    public void Main_readme()
    {
        var faker = new Faker(seed: 1);

        // snippet: readme-noda
        Instant created = faker.Noda().Instant.Past();
        LocalDate birthday = faker.Noda().LocalDate.Birthdate();
        Interval stay = faker.Noda().Interval();         // also DateInterval, YearMonth, AnnualDate, Offset
        var subscriptions = Fake.Many<Subscription>(10); // Instant CreatedAt, LocalDate BirthDate… follow the conventions
        // end snippet

        stay.Start.ShouldBeLessThanOrEqualTo(stay.End);
        birthday.ShouldBeLessThan(LocalDate.FromDateOnly(DateOnly.FromDateTime(faker.ReferenceDate.UtcDateTime)));
        created.ShouldBeLessThan(Instant.FromDateTimeOffset(faker.ReferenceDate));
        subscriptions.Count.ShouldBe(10);
    }
}
