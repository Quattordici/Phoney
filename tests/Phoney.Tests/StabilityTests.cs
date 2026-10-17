using System.Globalization;
using Phoney.Modules;

namespace Phoney.Tests;

public sealed class Measurement
{
    [System.ComponentModel.DataAnnotations.Range(0, double.PositiveInfinity)]
    public double Distance { get; set; }

    [System.ComponentModel.DataAnnotations.Range(double.MinValue, double.MaxValue)]
    public double Offset { get; set; }
}

public sealed class UniqueName
{
    public string Name { get; set; } = "";
    public bool Flag { get; set; }
}

/// <summary>Arguments outside what a method can honour fail with an argument exception that names the parameter, and values at the edge of a type's range stay in range.</summary>
public sealed class StabilityTests
{
    private static Faker Make(int seed = 1) => new("en", seed);

    [Test]
    [Arguments(double.NaN, 1d)]
    [Arguments(0d, double.NaN)]
    [Arguments(0d, double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity, 0d)]
    public void A_double_range_needs_finite_bounds(double min, double max)
    {
        var faker = Make();
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Random.Double(min, max));
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Number.Double(min, max));
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Number.Double(min, max, 2));
    }

    [Test]
    public void The_whole_double_range_gives_finite_values_inside_it()
    {
        var faker = Make();
        for (var i = 0; i < 200; i++)
        {
            var value = faker.Number.Double(double.MinValue, double.MaxValue);
            double.IsFinite(value).ShouldBeTrue();
            faker.Random.Double(double.MinValue, double.MaxValue).ShouldBeLessThan(double.MaxValue);
            faker.Number.Single(float.MinValue, float.MaxValue).ShouldBeLessThan(float.MaxValue);
        }
    }

    [Test]
    public void A_double_drawn_from_a_range_is_below_its_maximum()
    {
        // 1 and the next double: the only value below the maximum is the minimum.
        var max = Math.BitIncrement(1.0);
        for (var seed = 0; seed < 200; seed++)
            new Faker("en", seed).Number.Double(1.0, max).ShouldBe(1.0);
    }

    [Test]
    public void Ordinary_double_ranges_keep_their_values()
    {
        // Pinned before wide ranges were supported.
        Make(3).Number.Double().ShouldBe(BitConverter.Int64BitsToDouble(Pinned.Double01));
        Make(4).Number.Double(-50, 50).ShouldBe(BitConverter.Int64BitsToDouble(Pinned.DoubleMinus50To50));
        Make(5).Random.Double(10, 20).ShouldBe(BitConverter.Int64BitsToDouble(Pinned.Random10To20));
    }

    [Test]
    public void Range_annotations_without_a_limit_give_finite_values()
    {
        foreach (var measurement in Fake.For<Measurement>().Seed(1).Generate(100))
        {
            double.IsFinite(measurement.Distance).ShouldBeTrue();
            measurement.Distance.ShouldBeGreaterThanOrEqualTo(0);
            double.IsFinite(measurement.Offset).ShouldBeTrue();
        }
    }

    [Test]
    public void The_whole_decimal_range_does_not_overflow()
    {
        var faker = Make();
        for (var i = 0; i < 200; i++)
            faker.Random.Decimal(decimal.MinValue, decimal.MaxValue).ShouldBeInRange(decimal.MinValue, decimal.MaxValue);
        Make(2).Random.Decimal(0, 10).ShouldBe(Pinned.RandomDecimal0To10);
    }

    [Test]
    public void A_probability_must_be_a_number()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Random.Bool(double.NaN)).ParamName.ShouldBe("probability");
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Helpers.Maybe(() => "x", double.NaN)).ParamName.ShouldBe("probability");
    }

    [Test]
    public void A_weight_must_be_finite()
    {
        Should.Throw<ArgumentException>(() => Make().Random.Weighted(("a", double.PositiveInfinity), ("b", 1d))).ParamName.ShouldBe("items");
        Make(3).Random.Weighted(("a", 1d), ("b", 3d)).ShouldBe(Pinned.WeightedPick);
    }

    [Test]
    public void Past_and_future_years_can_not_be_negative()
    {
        var faker = Make();
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Date.Past(-1, 2)).ParamName.ShouldBe("minYears");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Date.Future(-1, 2)).ParamName.ShouldBe("minYears");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Date.Past(0)).ParamName.ShouldBe("years");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Date.Recent(0)).ParamName.ShouldBe("days");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Date.Birthdate(-1, 10)).ParamName.ShouldBe("minAge");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Person.Profile(minAge: -1)).ParamName.ShouldBe("minAge");
    }

    [Test]
    public void Dates_far_from_the_reference_stop_at_the_ends_of_the_calendar()
    {
        var faker = Make();
        var past = faker.Date.Past(100_000);
        past.ShouldBeLessThan(faker.ReferenceDate);
        faker.Date.Future(100_000).ShouldBeGreaterThan(faker.ReferenceDate);
        faker.Date.Recent(int.MaxValue).ShouldBeLessThan(faker.ReferenceDate);
        faker.Date.Soon(int.MaxValue).ShouldBeGreaterThan(faker.ReferenceDate);
        faker.Date.Birthdate(0, 100_000).ShouldBeLessThanOrEqualTo(DateOnly.FromDateTime(faker.ReferenceDate.UtcDateTime));
    }

    [Test]
    public void A_reference_date_at_the_end_of_the_calendar_still_gives_dates()
    {
        var first = new Faker("en", 1) { ReferenceDate = DateTimeOffset.MinValue };
        first.Date.Future().ShouldBeGreaterThan(DateTimeOffset.MinValue);
        first.Date.Soon().ShouldBeGreaterThan(DateTimeOffset.MinValue);
        first.Date.Anytime().ShouldBeGreaterThanOrEqualTo(DateTimeOffset.MinValue);

        var last = new Faker("en", 1) { ReferenceDate = DateTimeOffset.MaxValue };
        last.Date.Past().ShouldBeLessThan(DateTimeOffset.MaxValue);
        last.Date.Recent().ShouldBeLessThan(DateTimeOffset.MaxValue);
        last.Date.Anytime().ShouldBeLessThanOrEqualTo(DateTimeOffset.MaxValue);
        last.Date.Birthdate().ShouldBeLessThan(DateOnly.MaxValue);
    }

    [Test]
    public void A_date_that_can_not_exist_is_an_error_that_says_why()
    {
        var first = new Faker("en", 1) { ReferenceDate = DateTimeOffset.MinValue };
        Should.Throw<InvalidOperationException>(() => first.Date.Past()).Message.ShouldContain("before");
        Should.Throw<InvalidOperationException>(() => first.Date.Recent()).Message.ShouldContain("before");
        Should.Throw<InvalidOperationException>(() => first.Date.Birthdate()).Message.ShouldContain("18");

        var last = new Faker("en", 1) { ReferenceDate = DateTimeOffset.MaxValue };
        Should.Throw<InvalidOperationException>(() => last.Date.Future()).Message.ShouldContain("after");
        Should.Throw<InvalidOperationException>(() => last.Date.Soon()).Message.ShouldContain("after");
    }

    [Test]
    public void Ordinary_dates_keep_their_values()
    {
        // Pinned before the ends of the calendar were handled.
        var faker = Make(7);
        faker.Date.Past().ToUnixTimeMilliseconds().ShouldBe(Pinned.Past);
        faker.Date.Future(2, 5).ToUnixTimeMilliseconds().ShouldBe(Pinned.Future);
        faker.Date.Birthdate().ShouldBe(Pinned.Birthdate);
        faker.Date.Anytime().ToUnixTimeMilliseconds().ShouldBe(Pinned.Anytime);
    }

    [Test]
    public void Negative_lengths_and_counts_are_argument_errors()
    {
        var faker = Make();
        Action[] calls =
        [
            () => faker.String.Alpha(-1),
            () => faker.String.AlphaNumeric(-1),
            () => faker.String.Numeric(-1),
            () => faker.String.Binary(-1),
            () => faker.String.Octal(-1),
            () => faker.String.Hexadecimal(-1),
            () => faker.String.Sample(-1),
            () => faker.String.Symbol(-1),
            () => faker.String.NanoId(-1),
            () => faker.String.FromCharacters("ab", -1),
            () => faker.Internet.Password(-1),
            () => faker.Finance.AccountNumber(-1),
            () => faker.Airline.FlightNumber(-1),
            () => faker.Git.CommitSha(-1),
            () => faker.System.FileName(-1),
            () => faker.Lorem.Words(-1),
            () => faker.Lorem.Sentence(-1),
            () => faker.Lorem.Sentences(-1),
            () => faker.Lorem.Paragraph(-1),
            () => faker.Lorem.Paragraphs(-1),
            () => faker.Lorem.Lines(-1),
            () => faker.Lorem.Slug(-1),
            () => faker.Word.Words(-1),
            () => faker.Helpers.Multiple(() => 1, -1),
            () => faker.Helpers.Multiple(() => 1, -2, -1),
            () => faker.Date.Betweens(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, -1),
            () => faker.Image.Url(-1),
            () => faker.Image.Url(height: -1),
            () => faker.Image.DataUri(-1),
            () => faker.Image.DataUri(height: -1),
        ];

        for (var i = 0; i < calls.Length; i++)
            Should.Throw<ArgumentOutOfRangeException>(calls[i], $"call {i}");
    }

    [Test]
    public void Zero_lengths_and_counts_still_give_empty_results()
    {
        var faker = Make();
        faker.String.Alpha(0).ShouldBe("");
        faker.String.Hexadecimal(0).ShouldBe("0x");
        faker.Internet.Password(0).ShouldBe("");
        faker.Lorem.Words(0).ShouldBe("");
        faker.Lorem.Sentence(0).ShouldBe(".");
        faker.Word.Words(0).ShouldBe("");
        faker.Helpers.Multiple(() => 1, 0).ShouldBeEmpty();
        faker.System.FileName(0).ShouldNotContain('.');
    }

    [Test]
    public void Unique_needs_at_least_one_attempt()
    {
        var none = 0; // a constant is already reported by PHONEY011
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.For<UniqueName>().Unique(u => u.Name, none)).ParamName.ShouldBe("maxAttempts");
    }

    [Test]
    public void A_unique_member_that_runs_out_of_values_is_named_in_the_error()
    {
        var generator = Fake.For<UniqueName>().Seed(1).Unique(u => u.Flag, 10);

        var exception = Should.Throw<InvalidOperationException>(() => generator.Generate(5));

        exception.Message.ShouldContain("UniqueName.Flag");
        exception.Message.ShouldContain("10 attempts");
    }

    [Test]
    public void Json_property_names_with_spaces_around_them_are_read()
    {
        var feed = DataFeed.FromJson("""[{" Name ":"Ada","Age":36},{" Name ":"Grace","Age":85}]""");

        feed.Columns.ShouldBe(["Name", "Age"]);
        feed.Value(1, 0).ShouldBe("Grace");
    }

    [Test]
    public void A_json_item_that_repeats_a_property_instead_of_another_is_refused()
    {
        var exception = Should.Throw<InvalidOperationException>(() => DataFeed.FromJson("""[{"a":1,"b":2},{"a":1,"a":2}]"""));

        exception.Message.ShouldContain("Item 2");
    }

    [Test]
    public void Template_numbers_beyond_an_int_are_kept_to_the_int_range()
    {
        // A double cast to int is platform dependent outside the int range; the template saturates it explicitly.
        var faker = Make();
        int.Parse(faker.Parse("{{number.int(99999999999)}}"), CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(0);
        faker.Parse("{{number.int({\"min\":99999999999,\"max\":99999999999})}}").ShouldBe("2147483647");
    }

    [Test]
    public void A_locale_without_card_formats_still_makes_card_numbers()
    {
        var faker = new Faker("base", 1);

        var number = faker.Finance.CreditCardNumber();

        HelpersModule.LuhnCheck(number.Replace("-", "", StringComparison.Ordinal)).ShouldBeTrue();
        Enum.IsDefined(faker.Finance.CreditCardIssuer()).ShouldBeTrue();
        for (var seed = 0; seed < 50; seed++)
            new Faker("base", seed).Finance.CreditCardNumber(CardIssuer.AmericanExpress)[..2].ShouldBeOneOf("34", "37");
    }
}

/// <summary>Values the fixes must not move, recorded before them.</summary>
file static class Pinned
{
    public const long Double01 = 4604395936282356945L;
    public const long DoubleMinus50To50 = -4596020048919526534L;
    public const long Random10To20 = 4623442728463617696L;
    public const decimal RandomDecimal0To10 = 1.021791132303950m;
    public const string WeightedPick = "b";
    public const long Past = 1726221109050L;
    public const long Future = 1825157781145L;
    public const long Anytime = 1766033395713L;
    public static readonly DateOnly Birthdate = new(1996, 11, 24);
}
