using NodaTime;
using Phoney.NodaTime;

namespace Phoney.NodaTime.Tests;

/// <summary>The relative <c>Instant</c>, <c>LocalDate</c>, <c>LocalDateTime</c> and <c>ZonedDateTime</c> values are the core date values, converted.</summary>
public sealed class NodaTimeRelativeValueTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeZone PlusTwo = DateTimeZone.ForOffset(Offset.FromHours(2));

    private static Faker Faker(long seed) => new(seed: seed) { ReferenceDate = Reference };

    private static LocalDateTime Utc(DateTimeOffset value) => LocalDateTime.FromDateTime(value.UtcDateTime);

    /// <summary>Each NodaTime value with the core value it must equal for the same seed.</summary>
    private static readonly Dictionary<string, (Func<NodaTimeModule, object> Noda, Func<Faker, object> Core)> Cases = new(StringComparer.Ordinal)
    {
        ["Instant.Anytime"] = (n => n.Instant.Anytime(), f => Instant.FromDateTimeOffset(f.Date.Anytime())),
        ["LocalDate.Future"] = (n => n.LocalDate.Future(2), f => Utc(f.Date.Future(2)).Date),
        ["LocalDate.Recent"] = (n => n.LocalDate.Recent(3), f => Utc(f.Date.Recent(3)).Date),
        ["LocalDate.Soon"] = (n => n.LocalDate.Soon(3), f => Utc(f.Date.Soon(3)).Date),
        ["LocalDate.Between"] = (n => n.LocalDate.Between(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31)),
            f => LocalDate.FromDateOnly(f.Date.Between(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)))),
        ["LocalDateTime.Past"] = (n => n.LocalDateTime.Past(2), f => Utc(f.Date.Past(2))),
        ["LocalDateTime.Recent"] = (n => n.LocalDateTime.Recent(3), f => Utc(f.Date.Recent(3))),
        ["LocalDateTime.Soon"] = (n => n.LocalDateTime.Soon(3), f => Utc(f.Date.Soon(3))),
        ["LocalDateTime.Between"] = (n => n.LocalDateTime.Between(new LocalDateTime(2024, 1, 1, 0, 0), new LocalDateTime(2024, 1, 2, 0, 0)),
            f => Utc(f.Date.Between(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)))),
        ["ZonedDateTime.Future"] = (n => n.ZonedDateTime.Future(2, PlusTwo), f => Instant.FromDateTimeOffset(f.Date.Future(2)).InZone(PlusTwo)),
        ["ZonedDateTime.Recent"] = (n => n.ZonedDateTime.Recent(3), f => Instant.FromDateTimeOffset(f.Date.Recent(3)).InUtc()),
        ["ZonedDateTime.Soon"] = (n => n.ZonedDateTime.Soon(3, PlusTwo), f => Instant.FromDateTimeOffset(f.Date.Soon(3)).InZone(PlusTwo)),
    };

    public static IEnumerable<string> CaseNames() => Cases.Keys.Order(StringComparer.Ordinal);

    [Test]
    [MethodDataSource(nameof(CaseNames))]
    public void A_value_is_the_core_date_value_converted(string name)
    {
        var (noda, core) = Cases[name];

        for (var seed = 0; seed < 20; seed++)
            noda(Faker(seed).Noda()).ShouldBe(core(Faker(seed)), name);
    }

    [Test]
    public void Relative_values_are_on_the_right_side_of_the_reference_date()
    {
        var noda = Faker(5).Noda();
        var today = new LocalDate(2025, 6, 1);
        var now = new LocalDateTime(2025, 6, 1, 0, 0);

        for (var i = 0; i < 100; i++)
        {
            noda.LocalDate.Future().ShouldBeInRange(today, today.PlusYears(1));
            noda.LocalDate.Recent(5).ShouldBeInRange(today.PlusDays(-5), today);
            noda.LocalDate.Soon(5).ShouldBeInRange(today, today.PlusDays(5));
            noda.LocalDateTime.Past().ShouldBeInRange(now.PlusYears(-1), now);
            noda.LocalDateTime.Recent(2).ShouldBeInRange(now.PlusDays(-2), now);
            noda.LocalDateTime.Soon(2).ShouldBeInRange(now, now.PlusDays(2));
            noda.ZonedDateTime.Future(1, PlusTwo).Zone.ShouldBe(PlusTwo);
            noda.ZonedDateTime.Soon().Zone.ShouldBe(DateTimeZone.Utc); // UTC unless a zone is given
            noda.ZonedDateTime.Recent().ToInstant().ShouldBeLessThanOrEqualTo(Instant.FromDateTimeOffset(Reference));
        }
    }

    [Test]
    public void Between_rejects_a_range_that_ends_before_it_starts()
    {
        var noda = Faker(1).Noda();

        Should.Throw<ArgumentException>(() => noda.LocalDate.Between(new LocalDate(2024, 2, 1), new LocalDate(2024, 1, 1)));
        Should.Throw<ArgumentException>(() => noda.LocalDateTime.Between(new LocalDateTime(2024, 2, 1, 0, 0), new LocalDateTime(2024, 1, 1, 0, 0)));
    }
}
