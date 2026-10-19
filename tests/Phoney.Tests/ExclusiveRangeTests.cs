using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary><c>[Range]</c> with <c>MinimumIsExclusive</c>/<c>MaximumIsExclusive</c>: the excluded limits are never generated.</summary>
public sealed class OpenWindow
{
    [Range(1, 3, MinimumIsExclusive = true, MaximumIsExclusive = true)] public int OnlyTwo { get; set; }
    [Range(0, 10, MinimumIsExclusive = true)] public int AboveZero { get; set; }
    [Range(0.0, 1.0, MinimumIsExclusive = true, MaximumIsExclusive = true)] public double Share { get; set; }
    [Range(0.0, 0.01, MaximumIsExclusive = true)] public double Tiny { get; set; }
    [Range(typeof(decimal), "0", "0.01", MinimumIsExclusive = true, MaximumIsExclusive = true)] public decimal Fraction { get; set; }
    [Range(typeof(DateTime), "2020-01-01", "2020-01-01T00:00:00.002", MinimumIsExclusive = true, MaximumIsExclusive = true)] public DateTime Moment { get; set; }
    [Range(typeof(DateOnly), "2020-01-01", "2020-01-03", MinimumIsExclusive = true, MaximumIsExclusive = true)] public DateOnly Day { get; set; }
}

/// <summary>A <c>[Range]</c> narrower than two decimals.</summary>
public sealed class Narrow
{
    [Range(typeof(decimal), "0.001", "0.009")] public decimal Rate { get; set; }
}

[FakeFor<OpenWindow>]
[FakeFor<Narrow>]
public static partial class ExclusiveRangeFakes;

public sealed class ExclusiveRangeTests
{
    private static ObjectGenerator<OpenWindow> Make(bool generated) =>
        (generated ? ExclusiveRangeFakes.OpenWindow : new ObjectGenerator<OpenWindow>(new ReflectionModel<OpenWindow>())).Seed(5);

    private static string[] Failing(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return [.. results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Excluded_limits_are_never_generated(bool generated)
    {
        var windows = Make(generated).Generate(500);

        windows.ShouldAllBe(w => Failing(w).Length == 0);
        windows.ShouldAllBe(w => w.OnlyTwo == 2);
        windows.ShouldAllBe(w => w.Day == new DateOnly(2020, 1, 2));
        windows.ShouldAllBe(w => w.Moment == new DateTime(2020, 1, 1, 0, 0, 0, 1, DateTimeKind.Utc));
        windows.Select(w => w.AboveZero).Distinct().Count().ShouldBe(10); // 1 to 10, all of them
    }

    [Test]
    public void Generated_and_reflection_models_agree()
    {
        var generated = Make(true).Generate(50);
        var reflected = Make(false).Generate(50);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));

        static string Describe(OpenWindow w) => $"{w.OnlyTwo} {w.AboveZero} {w.Share:R} {w.Tiny:R} {w.Fraction} {w.Moment:O} {w.Day}";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Boundary_cases_are_the_nearest_values_inside(bool generated)
    {
        var cases = Make(generated).BoundaryCases();

        cases.ShouldNotBeEmpty();
        foreach (var c in cases)
            Failing(c.Value).ShouldBeEmpty(c.Description);
        var onlyTwo = cases.Where(c => c.Member == nameof(OpenWindow.OnlyTwo)).Select(c => c.Value.OnlyTwo);
        onlyTwo.ShouldBe([2, 2]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Invalid_cases_include_the_excluded_limits(bool generated)
    {
        var cases = Make(generated).InvalidCases();

        foreach (var c in cases)
            Failing(c.Value).ShouldBe([c.Member], c.Description);
        cases.Where(c => c.Member == nameof(OpenWindow.OnlyTwo)).Select(c => c.Value.OnlyTwo).ShouldBe([1, 3]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Decimal_ranges_narrower_than_two_decimals_get_more_decimals(bool generated)
    {
        var generator = generated ? ExclusiveRangeFakes.Narrow : new ObjectGenerator<Narrow>(new ReflectionModel<Narrow>());

        generator.Seed(1).Generate(200).ShouldAllBe(n => n.Rate >= 0.001m && n.Rate <= 0.009m);
    }
}
