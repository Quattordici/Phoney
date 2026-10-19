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

/// <summary>Exclusive limits that a <see cref="float"/> can't hold exactly: the validator compares as <see cref="double"/> or, with a float operand, as <see cref="float"/>.</summary>
public sealed class FloatWindow
{
    [Range(0.1, 1.0, MinimumIsExclusive = true, MaximumIsExclusive = true)] public float AsDouble { get; set; }
    [Range(typeof(float), "0.1", "0.7", MinimumIsExclusive = true, MaximumIsExclusive = true)] public float AsFloat { get; set; }
    [Range(0.1, 0.7)] public float Inclusive { get; set; }
    [Range(typeof(decimal), "1", "2", MinimumIsExclusive = true)] public decimal AboveOne { get; set; }
}

/// <summary>A <see cref="Half"/> next to an excluded limit: halves near 100 are 0.0625 apart, so rounding can land on it. (<c>RangeAttribute</c> can't validate a <see cref="Half"/>, so the tests compare.)</summary>
public sealed class HalfWindow
{
    [Range(0, 100, MaximumIsExclusive = true)] public Half Percent { get; set; }
    [Range(0, 1, MinimumIsExclusive = true, MaximumIsExclusive = true)] public Half Share { get; set; }
}

/// <summary><c>[Range]</c>s narrower than two decimals, and limits a <see cref="double"/> can't tell apart.</summary>
public sealed class Narrow
{
    [Range(typeof(decimal), "0.001", "0.009")] public decimal Rate { get; set; }
    [Range(typeof(decimal), "0.0000000000000000001", "0.0000000000000000009")] public decimal Tiny { get; set; }
    [Range(typeof(decimal), "0", "0.000000000000000001", MinimumIsExclusive = true, MaximumIsExclusive = true)] public decimal Open { get; set; }
    [Range(typeof(decimal), "0.1000000000000000001", "0.1000000000000000009")] public decimal Precise { get; set; }
}

/// <summary>Excluded limits that leave no value of the member's type.</summary>
public sealed class NoWholeNumber
{
    [Range(1, 2, MinimumIsExclusive = true, MaximumIsExclusive = true)] public int Count { get; set; }
}

/// <inheritdoc cref="NoWholeNumber"/>
public sealed class NoDay
{
    [Range(typeof(DateOnly), "2020-01-01", "2020-01-02", MinimumIsExclusive = true, MaximumIsExclusive = true)] public DateOnly Day { get; set; }
}

/// <inheritdoc cref="NoWholeNumber"/>
public sealed class NoMoment
{
    [Range(typeof(DateTime), "2020-01-01", "2020-01-01", MinimumIsExclusive = true)] public DateTime At { get; set; }
}

/// <inheritdoc cref="NoWholeNumber"/>
public sealed class NoFraction
{
    [Range(1.0, 1.0, MaximumIsExclusive = true)] public double Share { get; set; }
}

/// <inheritdoc cref="NoWholeNumber"/>
public sealed class NoDecimal
{
    [Range(typeof(decimal), "1", "1", MinimumIsExclusive = true)] public decimal Amount { get; set; }
}

[FakeFor<OpenWindow>]
[FakeFor<Narrow>]
[FakeFor<FloatWindow>]
[FakeFor<HalfWindow>]
[FakeFor<NoWholeNumber>]
[FakeFor<NoDay>]
[FakeFor<NoMoment>]
[FakeFor<NoFraction>]
[FakeFor<NoDecimal>]
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

    private static ObjectGenerator<FloatWindow> Floats(bool generated) =>
        (generated ? ExclusiveRangeFakes.FloatWindow : new ObjectGenerator<FloatWindow>(new ReflectionModel<FloatWindow>())).Seed(3);

    private static ObjectGenerator<HalfWindow> Halves(bool generated) =>
        (generated ? ExclusiveRangeFakes.HalfWindow : new ObjectGenerator<HalfWindow>(new ReflectionModel<HalfWindow>())).Seed(3);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Float_cases_hold_whether_the_validator_compares_as_double_or_float(bool generated)
    {
        var generator = Floats(generated);

        foreach (var c in generator.BoundaryCases())
            Failing(c.Value).ShouldBeEmpty(c.Description);
        foreach (var c in generator.InvalidCases())
            Failing(c.Value).ShouldBe([c.Member], c.Description);
        generator.Generate(500).ShouldAllBe(f => Failing(f).Length == 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void The_boundary_next_to_an_excluded_decimal_is_the_nearest_decimal(bool generated) =>
        Floats(generated).BoundaryCases().Where(c => c.Member == nameof(FloatWindow.AboveOne)).Select(c => c.Value.AboveOne)
            .ShouldContain(1.0000000000000000000000000001m);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Halves_never_round_onto_an_excluded_limit(bool generated)
    {
        var halves = Halves(generated).Generate(10_000);

        halves.ShouldAllBe(h => h.Percent >= (Half)0 && h.Percent < (Half)100);
        halves.ShouldAllBe(h => h.Share > (Half)0 && h.Share < (Half)1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Half_boundary_and_invalid_cases_sit_on_the_right_side(bool generated)
    {
        var generator = Halves(generated);

        generator.BoundaryCases().Where(c => c.Member == nameof(HalfWindow.Share)).Select(c => c.Value.Share)
            .ShouldBe([Half.BitIncrement((Half)0), Half.BitDecrement((Half)1)]);
        generator.InvalidCases().Where(c => c.Member == nameof(HalfWindow.Share)).Select(c => c.Value.Share).ShouldBe([(Half)0, (Half)1]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Decimal_ranges_narrower_than_two_decimals_get_more_decimals(bool generated)
    {
        var generator = generated ? ExclusiveRangeFakes.Narrow : new ObjectGenerator<Narrow>(new ReflectionModel<Narrow>());

        var values = generator.Seed(1).Generate(200);

        values.ShouldAllBe(n => Failing(n).Length == 0);
        values.ShouldAllBe(n => n.Rate >= 0.001m && n.Rate <= 0.009m);
        values.ShouldAllBe(n => n.Tiny >= 0.0000000000000000001m && n.Tiny <= 0.0000000000000000009m);
        values.ShouldAllBe(n => n.Open > 0m && n.Open < 0.000000000000000001m);
        values.ShouldAllBe(n => n.Precise >= 0.1000000000000000001m && n.Precise <= 0.1000000000000000009m);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Excluded_limits_that_leave_no_value_say_so(bool generated)
    {
        Action[] runs =
        [
            () => (generated ? ExclusiveRangeFakes.NoWholeNumber : new ObjectGenerator<NoWholeNumber>(new ReflectionModel<NoWholeNumber>())).Generate(1),
            () => (generated ? ExclusiveRangeFakes.NoDay : new ObjectGenerator<NoDay>(new ReflectionModel<NoDay>())).Generate(1),
            () => (generated ? ExclusiveRangeFakes.NoMoment : new ObjectGenerator<NoMoment>(new ReflectionModel<NoMoment>())).Generate(1),
            () => (generated ? ExclusiveRangeFakes.NoFraction : new ObjectGenerator<NoFraction>(new ReflectionModel<NoFraction>())).Generate(1),
            () => (generated ? ExclusiveRangeFakes.NoDecimal : new ObjectGenerator<NoDecimal>(new ReflectionModel<NoDecimal>())).Generate(1),
        ];

        foreach (var run in runs)
        {
            var message = Should.Throw<InvalidOperationException>(run).Message;
            message.ShouldContain("[Range(");
            message.ShouldContain(".With(");
        }
    }
}
