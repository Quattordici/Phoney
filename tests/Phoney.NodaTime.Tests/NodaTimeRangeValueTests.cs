using NodaTime;
using Phoney.NodaTime;

namespace Phoney.NodaTime.Tests;

/// <summary><c>Offset</c>, <c>Interval</c>, <c>DateInterval</c>, <c>YearMonth</c> and <c>AnnualDate</c> values.</summary>
public sealed class NodaTimeRangeValueTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Instant Now = Instant.FromDateTimeOffset(Reference);

    private static Faker Faker(long seed = 1) => new(seed: seed) { ReferenceDate = Reference };

    [Test]
    public void Offsets_are_real_world_offsets_in_quarter_hours()
    {
        var noda = Faker().Noda();

        for (var i = 0; i < 200; i++)
        {
            var offset = noda.Offset();
            offset.ShouldBeInRange(Offset.FromHours(-12), Offset.FromHours(14));
            (offset.Seconds % 900).ShouldBe(0);
        }
    }

    [Test]
    public void An_interval_lies_within_the_past_years_and_starts_before_it_ends()
    {
        var noda = Faker().Noda();

        for (var i = 0; i < 100; i++)
        {
            var interval = noda.Interval();
            interval.HasStart.ShouldBeTrue();
            interval.HasEnd.ShouldBeTrue();
            interval.Start.ShouldBeGreaterThanOrEqualTo(Now - Duration.FromDays(366));
            interval.End.ShouldBeLessThanOrEqualTo(Now);
            interval.Start.ShouldBeLessThanOrEqualTo(interval.End);
        }
    }

    [Test]
    public void An_interval_between_two_instants_stays_inside_them()
    {
        var noda = Faker().Noda();
        var from = Now - Duration.FromDays(10);
        var to = Now + Duration.FromDays(10);

        for (var i = 0; i < 100; i++)
        {
            var interval = noda.Interval(from, to);
            interval.Start.ShouldBeGreaterThanOrEqualTo(from);
            interval.End.ShouldBeLessThanOrEqualTo(to);
        }

        Should.Throw<ArgumentException>(() => noda.Interval(to, from));
    }

    [Test]
    public void A_date_interval_lies_within_the_past_years_and_within_the_given_dates()
    {
        var noda = Faker().Noda();
        var today = new LocalDate(2025, 6, 1);

        for (var i = 0; i < 100; i++)
        {
            var interval = noda.DateInterval();
            interval.Start.ShouldBeLessThanOrEqualTo(interval.End);
            interval.End.ShouldBeLessThanOrEqualTo(today);
            interval.Start.ShouldBeGreaterThanOrEqualTo(today.PlusYears(-1).PlusDays(-1));
        }

        var between = noda.DateInterval(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 31));
        between.Start.ShouldBeGreaterThanOrEqualTo(new LocalDate(2024, 1, 1));
        between.End.ShouldBeLessThanOrEqualTo(new LocalDate(2024, 1, 31));
        Should.Throw<ArgumentException>(() => noda.DateInterval(new LocalDate(2024, 2, 1), new LocalDate(2024, 1, 1)));
    }

    [Test]
    public void A_year_month_is_in_the_past_years_or_between_two_months()
    {
        var noda = Faker().Noda();

        for (var i = 0; i < 100; i++)
        {
            noda.YearMonth().ShouldBeInRange(new YearMonth(2024, 6), new YearMonth(2025, 6));
            noda.YearMonth(new YearMonth(2020, 11), new YearMonth(2021, 2)).ShouldBeInRange(new YearMonth(2020, 11), new YearMonth(2021, 2));
        }

        Should.Throw<ArgumentException>(() => noda.YearMonth(new YearMonth(2021, 2), new YearMonth(2020, 11)));
    }

    [Test]
    public void An_annual_date_is_a_real_day_and_february_29_can_occur()
    {
        var noda = Faker(3).Noda();
        var dates = Enumerable.Range(0, 5000).Select(_ => noda.AnnualDate()).ToList();

        dates.ShouldAllBe(d => d.Month >= 1 && d.Month <= 12 && d.Day >= 1 && d.Day <= 31);
        dates.Select(d => d.Month).Distinct().Count().ShouldBe(12);
        dates.ShouldContain(d => d.Month == 2 && d.Day == 29);
        dates.Any(d => (d.Month == 4 || d.Month == 6 || d.Month == 9 || d.Month == 11) && d.Day > 30).ShouldBeFalse();
        dates.Any(d => d.Month == 2 && d.Day > 29).ShouldBeFalse();
    }

    [Test]
    public void The_same_seed_gives_the_same_values()
    {
        string Take(long seed)
        {
            var noda = Faker(seed).Noda();
            return string.Join("|", noda.Offset(), noda.Interval(), noda.DateInterval(), noda.YearMonth(), noda.AnnualDate());
        }

        Take(9).ShouldBe(Take(9));
        Take(9).ShouldNotBe(Take(10));
    }
}
