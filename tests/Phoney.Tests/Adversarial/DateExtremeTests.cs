namespace Phoney.Tests.Adversarial;

/// <summary>Dates at the ends of the calendar, spans of billions of years and offsets that are not UTC.</summary>
public sealed class DateExtremeTests
{
    private static Faker At(DateTimeOffset reference) => new("en", seed: 1) { ReferenceDate = reference };

    [Test]
    public void The_whole_calendar_is_a_valid_range()
    {
        var faker = At(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        for (var i = 0; i < 500; i++)
        {
            faker.Date.Between(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Offset.ShouldBe(TimeSpan.Zero);
            faker.Date.Between(DateOnly.MinValue, DateOnly.MaxValue).ShouldBeInRange(DateOnly.MinValue, DateOnly.MaxValue);
        }
    }

    [Test]
    public void Ranges_given_in_far_apart_offsets_compare_as_instants_and_come_back_in_utc()
    {
        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(14)); // 2019-12-31 10:00 UTC
        var to = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(-14)); // 2020-01-01 14:00 UTC
        var faker = At(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        for (var i = 0; i < 200; i++)
        {
            var value = faker.Date.Between(from, to);
            value.Offset.ShouldBe(TimeSpan.Zero);
            value.ShouldBeInRange(from, to);
        }
    }

    [Test]
    public void Spans_of_int_max_years_stop_at_the_ends_of_the_calendar()
    {
        var faker = At(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        faker.Date.Past(int.MaxValue).ShouldBeLessThanOrEqualTo(faker.ReferenceDate);
        faker.Date.Future(int.MaxValue).ShouldBeGreaterThanOrEqualTo(faker.ReferenceDate);
        faker.Date.Recent(int.MaxValue).ShouldBeLessThanOrEqualTo(faker.ReferenceDate);
        faker.Date.Soon(int.MaxValue).ShouldBeGreaterThanOrEqualTo(faker.ReferenceDate);
        faker.Date.Birthdate(0, 100_000).ShouldBeLessThanOrEqualTo(new DateOnly(2025, 1, 1));
        faker.Date.Betweens(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 100_000).ShouldBeInOrder();
    }

    [Test]
    public void At_the_last_moment_of_the_calendar_past_dates_work_and_future_ones_explain_themselves()
    {
        var faker = At(DateTimeOffset.MaxValue);

        faker.Date.Past().ShouldBeLessThanOrEqualTo(DateTimeOffset.MaxValue);
        faker.Date.Anytime().ShouldBeLessThanOrEqualTo(DateTimeOffset.MaxValue);
        faker.Person.Profile().Age.ShouldBeGreaterThanOrEqualTo(0);
        Should.Throw<InvalidOperationException>(() => faker.Date.Future()).Message.ShouldContain("end of the calendar");
    }

    [Test]
    public void At_the_first_moment_of_the_calendar_future_dates_work_and_past_ones_explain_themselves()
    {
        var faker = At(DateTimeOffset.MinValue);

        faker.Date.Future().ShouldBeGreaterThanOrEqualTo(DateTimeOffset.MinValue);
        faker.Date.Anytime().ShouldBeGreaterThanOrEqualTo(DateTimeOffset.MinValue);
        Should.Throw<InvalidOperationException>(() => faker.Date.Past()).Message.ShouldContain("end of the calendar");
        Should.Throw<InvalidOperationException>(() => faker.Date.Birthdate()).Message.ShouldContain("minAge");
    }

    [Test]
    public void A_reference_date_in_another_offset_still_gives_utc_values()
    {
        var faker = At(new DateTimeOffset(2020, 6, 1, 12, 0, 0, TimeSpan.FromHours(5)));

        faker.Date.Past().Offset.ShouldBe(TimeSpan.Zero);
        faker.Date.Future().Offset.ShouldBe(TimeSpan.Zero);
        faker.Date.Soon().Offset.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void Time_ordered_ids_at_the_end_of_the_calendar_are_still_well_formed()
    {
        var faker = At(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        faker.String.UuidV7(DateTimeOffset.MaxValue)[14].ShouldBe('7'); // the version digit (Guid.Version needs .NET 9)
        faker.String.Ulid(DateTimeOffset.MaxValue).Length.ShouldBe(26);
    }
}
