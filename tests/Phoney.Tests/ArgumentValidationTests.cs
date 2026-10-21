using System.Globalization;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>Edge-case arguments either work or fail with an argument exception that names the parameter, never with an arithmetic or internal error.</summary>
public sealed class ArgumentValidationTests
{
    [Test]
    [Arguments("0", "79228162514264337593543950335")]
    [Arguments("-79228162514264337593543950335", "1")]
    [Arguments("-79228162514264337593543950335", "79228162514264337593543950335")]
    [Arguments("100000000000000000000", "1000000000000000000000")]
    public void Decimals_cover_the_whole_decimal_range(string low, string high)
    {
        var (min, max) = (decimal.Parse(low, CultureInfo.InvariantCulture), decimal.Parse(high, CultureInfo.InvariantCulture));
        var faker = new Faker("en", 1);
        for (var i = 0; i < 200; i++)
            faker.Number.Decimal(min, max).ShouldBeInRange(min, max);
        faker.Finance.Amount(min, max).ShouldBeInRange(min, max);
        if (min >= 0)
            faker.Commerce.Price(min, max).ShouldBeInRange(min, max);
    }

    [Test]
    public void Empty_value_lists_name_the_rule_and_the_member()
    {
        var generator = Fake.For<ArgumentHolder>();
        Should.Throw<ArgumentException>(() => generator.WithOneOf(x => x.Name, Array.Empty<string>())).Message.ShouldStartWith("WithOneOf(x => x.Name) needs at least one value");
        Should.Throw<ArgumentException>(() => generator.WithOneOf(x => x.Name, Array.Empty<int>(), i => i.ToString(CultureInfo.InvariantCulture))).Message.ShouldStartWith("WithOneOf(x => x.Name) needs at least one item");
        Should.Throw<ArgumentException>(() => generator.Cycle(x => x.Name, Array.Empty<string>())).Message.ShouldStartWith("Cycle(x => x.Name) needs at least one value");
    }

    public sealed class ArgumentHolder
    {
        public string Name { get; set; } = "";
    }

    [Test]
    [Arguments(-5, -1, "min")]
    [Arguments(-1, 10, "min")]
    [Arguments(0, -1, "max")]
    public void Negative_prices_are_refused_instead_of_giving_zero(int min, int max, string parameter)
    {
        var faker = new Faker("en", 1);

        Should.Throw<ArgumentOutOfRangeException>(() => faker.Commerce.Price(min, max)).ParamName.ShouldBe(parameter);
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Commerce.PriceText("$", min, max)).ParamName.ShouldBe(parameter);
    }

    [Test]
    public void Coordinates_must_lie_on_the_globe()
    {
        var faker = new Faker("en", 1);

        Should.Throw<ArgumentOutOfRangeException>(() => faker.Location.Latitude(0, 100)).ParamName.ShouldBe("max");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Location.Latitude(-91, 0)).ParamName.ShouldBe("min");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Location.Longitude(0, 180.5)).ParamName.ShouldBe("max");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Location.Longitude(-200, 0)).ParamName.ShouldBe("min");
        faker.Location.Latitude(90, 90).ShouldBe(90);
        faker.Location.Longitude(-180, -180).ShouldBe(-180);
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("https://")]
    [Arguments("ht tp")]
    [Arguments("1http")]
    public void Url_schemes_must_be_valid(string protocol) =>
        Should.Throw<ArgumentException>(() => new Faker("en", 1).Internet.Url(protocol: protocol)).ParamName.ShouldBe("protocol");

    [Test]
    public void Url_accepts_any_valid_scheme()
    {
        var faker = new Faker("en", 1);

        faker.Internet.Url(protocol: "ftp").ShouldStartWith("ftp://");
        faker.Internet.Url(protocol: "git+ssh").ShouldStartWith("git+ssh://");
        Should.Throw<ArgumentNullException>(() => faker.Internet.Url(protocol: null!)).ParamName.ShouldBe("protocol");
    }

    [Test]
    public void A_missing_culture_or_path_is_reported()
    {
        Should.Throw<ArgumentNullException>(() => new Faker((CultureInfo)null!)).ParamName.ShouldBe("culture");
        Should.Throw<ArgumentNullException>(() => new Faker("en", 1).TryPick(null!)).ParamName.ShouldBe("path");
        new Faker(CultureInfo.InvariantCulture).Locale.ShouldBe(Locales.Default);
    }

    [Test]
    public void Prices_next_to_the_largest_decimal_do_not_overflow()
    {
        var (min, max) = (decimal.MaxValue - 1, decimal.MaxValue);
        for (var seed = 0; seed < 200; seed++)
            new Faker("en", seed).Commerce.Price(min, max).ShouldBeInRange(min, max);
    }

    [Test]
    public void Decimals_keep_the_places_the_magnitude_allows()
    {
        // 1e20 with 10 places needs 31 digits; a decimal holds 28–29, so the value keeps as many places as fit.
        var value = new Faker("en", 2).Number.Decimal(1e20m, 2e20m, decimals: 10);

        value.ShouldBe(110217911323039500000m);
        value.Scale.ShouldBe((byte)0);
    }

    [Test]
    public void Wide_ranges_do_not_change_the_values_of_ranges_that_already_worked()
    {
        // Pinned before wide ranges were supported.
        new Faker("en", 3).Number.Decimal(0, 1000, 2).ShouldBe(690.64m);
        new Faker("en", 4).Number.Decimal(-5, 5, 4).ShouldBe(-2.3657m);
        new Faker("en", 5).Commerce.Price().ShouldBe(289.15m);
        new Faker("en", 6).Number.Decimal(0, 100_000_000_000_000_000_000m, 0).ShouldBe(76703131424633500000m);
    }

    [Test]
    public void More_than_28_decimal_places_is_an_argument_error()
    {
        var faker = new Faker("en", 1);
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Number.Decimal(0, 1, 29)).ParamName.ShouldBe("decimals");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Finance.Amount(0, 1, 29)).ParamName.ShouldBe("decimals");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Commerce.Price(1, 2, 29)).ParamName.ShouldBe("decimals");
        faker.Number.Decimal(0, 1, 28).ShouldBeInRange(0, 1);
    }

    [Test]
    [Arguments(0, 10)]
    [Arguments(-1, 10)]
    [Arguments(2000, 10_000)]
    public void Birthdate_years_outside_1_to_9999_are_an_argument_error(int minYear, int maxYear)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => new Faker("en", 1).Date.BirthdateInYears(minYear, maxYear));
        exception.ParamName.ShouldBe(minYear < 1 ? "minYear" : "maxYear");
    }

    [Test]
    public void Birthdate_years_at_the_limits_work()
    {
        var date = new Faker("en", 1).Date.BirthdateInYears(1, 9999);
        date.Year.ShouldBeInRange(1, 9999);
    }

    [Test]
    public void An_undefined_card_issuer_is_an_argument_error()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => new Faker("en", 1).Finance.CreditCardNumber((CardIssuer)999));
        exception.ParamName.ShouldBe("issuer");
    }

    [Test]
    public void A_null_password_prefix_is_an_argument_error()
    {
        var exception = Should.Throw<ArgumentNullException>(() => new Faker("en", 1).Internet.Password(prefix: null!));
        exception.ParamName.ShouldBe("prefix");
    }

    [Test]
    public void Word_and_sentence_counts_can_be_a_range()
    {
        var faker = new Faker("en", 3);
        for (var i = 0; i < 100; i++)
        {
            faker.Lorem.Words(2, 5).Split(' ').Length.ShouldBeInRange(2, 5);
            faker.Lorem.Sentence(3, 6).Split(' ').Length.ShouldBeInRange(3, 6);
        }

        var drawn = new Faker("en", 3);
        new Faker("en", 3).Lorem.Words(2, 5).ShouldBe(drawn.Lorem.Words(drawn.Random.Int(2, 5)));
        new Faker("en", 3).Lorem.Sentence(3, 10).ShouldBe(new Faker("en", 3).Lorem.Sentence()); // the default range, drawn the same way
    }

    [Test]
    public void Word_and_sentence_ranges_are_checked()
    {
        var lorem = new Faker("en", 1).Lorem;
        Should.Throw<ArgumentOutOfRangeException>(() => lorem.Words(-1, 2)).ParamName.ShouldBe("min");
        Should.Throw<ArgumentException>(() => lorem.Words(5, 2)).ParamName.ShouldBe("max");
        Should.Throw<ArgumentOutOfRangeException>(() => lorem.Sentence(-1, 2)).ParamName.ShouldBe("minWords");
        Should.Throw<ArgumentException>(() => lorem.Sentence(5, 2)).ParamName.ShouldBe("maxWords");
    }

    // An empty name gave "_57@gmail.com": a name that is given must be a name; null still means "generate one".
    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public void Names_given_for_emails_and_usernames_must_not_be_empty(string name)
    {
        var internet = new Faker("en", 1).Internet;
        Should.Throw<ArgumentException>(() => internet.Email(name, "Doe")).ParamName.ShouldBe("firstName");
        Should.Throw<ArgumentException>(() => internet.Email("Jo", name)).ParamName.ShouldBe("lastName");
        Should.Throw<ArgumentException>(() => internet.ExampleEmail(name)).ParamName.ShouldBe("firstName");
        Should.Throw<ArgumentException>(() => internet.Username(name)).ParamName.ShouldBe("firstName");
        Should.Throw<ArgumentException>(() => internet.DisplayName("Jo", name)).ParamName.ShouldBe("lastName");
        internet.Email(null, "Doe").ShouldContain("@");
    }
}
