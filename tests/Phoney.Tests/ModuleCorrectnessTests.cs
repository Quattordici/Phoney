using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>Checks values that have rules: checksums, formats and ranges.</summary>
public sealed class ModuleCorrectnessTests
{
    private readonly Faker _faker = new("en", seed: 12345);

    [Theory]
    [InlineData("[A-Z]{3}-\\d{4}", "^[A-Z]{3}-[0-9]{4}$")]
    [InlineData("(foo|bar)baz", "^(foo|bar)baz$")]
    [InlineData("a{2,4}b?c+", "^a{2,4}b?c+$")]
    [InlineData("[^0-9a-z]{5}", "^[A-Z]{5}$")]
    [InlineData("\\w+@\\w+\\.com", "^\\w+@\\w+\\.com$")]
    [InlineData("/[a-c]{3}/i", "^[a-cA-C]{3}$")]
    [InlineData("T[0-9][ABCEGHJ-NPRSTVW-Z] [0-9][ABCEGHJ-NPRSTVW-Z][0-9]", "^T[0-9][ABCEGHJ-NPRSTVW-Z] [0-9][ABCEGHJ-NPRSTVW-Z][0-9]$")]
    public void FromRegExp_matches_its_pattern(string pattern, string check)
    {
        for (var i = 0; i < 200; i++)
            _faker.Helpers.FromRegExp(pattern).ShouldMatch(check);
    }

    [Theory]
    [InlineData("[b-a]")]
    [InlineData("(abc")]
    [InlineData("abc\\")]
    public void FromRegExp_reports_invalid_patterns(string pattern) =>
        Should.Throw<FormatException>(() => _faker.Helpers.FromRegExp(pattern));

    [Fact]
    public void Credit_card_numbers_pass_the_Luhn_check()
    {
        foreach (var issuer in new[] { "visa", "mastercard", "american_express", "discover", "jcb", "diners_club", null })
        {
            for (var i = 0; i < 50; i++)
                HelpersModule.LuhnCheck(_faker.Finance.CreditCardNumber(issuer)).ShouldBeTrue();
        }
    }

    [Fact]
    public void Credit_card_formats_with_ranges_are_expanded() =>
        _faker.Helpers.ReplaceCreditCardSymbols("2[221-720]-####-####-###L").ShouldMatch("^2(22[1-9]|2[3-9]\\d|[3-6]\\d\\d|7[01]\\d|720)-\\d{4}-\\d{4}-\\d{4}$");

    [Fact]
    public void Imei_is_valid() =>
        HelpersModule.LuhnCheck(_faker.Phone.Imei()).ShouldBeTrue();

    [Fact]
    public void Ibans_have_valid_check_digits()
    {
        for (var i = 0; i < 200; i++)
        {
            var iban = _faker.Finance.Iban();
            // ISO 13616: move the first four characters to the end, convert letters to numbers, mod 97 == 1.
            var rearranged = iban[4..] + iban[..4];
            var digits = string.Concat(rearranged.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture)));
            (BigInteger.Parse(digits, CultureInfo.InvariantCulture) % 97).ShouldBe(BigInteger.One, iban);
        }
    }

    [Fact]
    public void Iban_for_a_country_has_that_country_and_length()
    {
        var iban = _faker.Finance.Iban("SE");
        iban.ShouldStartWith("SE");
        iban.Length.ShouldBe(24);
        _faker.Finance.Iban("SE", formatted: true).ShouldMatch("^SE\\d{2}( \\d{4}){5}$");
    }

    [Fact]
    public void Isbns_have_valid_check_digits()
    {
        for (var i = 0; i < 100; i++)
        {
            var isbn13 = _faker.Commerce.Isbn().Replace("-", "", StringComparison.Ordinal);
            isbn13.Length.ShouldBe(13);
            isbn13.Select((c, idx) => (c - '0') * (idx % 2 == 0 ? 1 : 3)).Sum().ShouldBe(isbn13.Select((c, idx) => (c - '0') * (idx % 2 == 0 ? 1 : 3)).Sum() / 10 * 10);

            var isbn10 = _faker.Commerce.Isbn(IsbnVariant.Isbn10).Replace("-", "", StringComparison.Ordinal);
            isbn10.Length.ShouldBe(10);
            var sum = isbn10.Select((c, idx) => (c == 'X' ? 10 : c - '0') * (10 - idx)).Sum();
            (sum % 11).ShouldBe(0, isbn10);
        }
    }

    [Fact]
    public void Upc_has_a_valid_check_digit()
    {
        var upc = _faker.Commerce.Upc("0123");
        upc.ShouldStartWith("0123");
        upc.Length.ShouldBe(12);
        var sum = upc.Select((c, idx) => (c - '0') * (idx % 2 == 0 ? 3 : 1)).Sum();
        (sum % 10).ShouldBe(0);
    }

    [Fact]
    public void Routing_numbers_have_valid_checksums()
    {
        for (var i = 0; i < 100; i++)
        {
            var routing = _faker.Finance.RoutingNumber();
            routing.Length.ShouldBe(9);
            var sum = routing.Select((c, idx) => (c - '0') * (idx % 3) switch { 0 => 3, 1 => 7, _ => 1 }).Sum();
            (sum % 10).ShouldBe(0, routing);
        }
    }

    [Fact]
    public void Vin_has_valid_check_digit()
    {
        var vin = _faker.Vehicle.Vin();
        vin.Length.ShouldBe(17);
        vin.ShouldNotContain('I');
        vin[8].ToString().ShouldBe(VehicleModule.VinCheckDigit(vin));
    }

    [Fact]
    public void Ipv4_respects_networks_and_cidr_blocks()
    {
        for (var i = 0; i < 100; i++)
        {
            _faker.Internet.Ipv4(IPv4Network.PrivateC).ShouldStartWith("192.168.");
            _faker.Internet.Ipv4("10.20.0.0/16").ShouldStartWith("10.20.");
        }

        _faker.Internet.Ipv4("1.2.3.4/32").ShouldBe("1.2.3.4");
    }

    [Fact]
    public void Emails_are_well_formed()
    {
        var email = new Regex("^[A-Za-z0-9._+-]+@[a-z0-9.-]+$");
        foreach (var locale in new[] { "en", "ru", "el", "ja", "ar", "he" })
        {
            var faker = new Faker(locale, seed: 1);
            for (var i = 0; i < 50; i++)
                faker.Internet.Email().ShouldMatch(email.ToString(), $"{locale}");
        }
    }

    [Fact]
    public void Email_uses_given_names()
    {
        var email = _faker.Internet.Email("Åsa", "Öberg", provider: "example.com");
        email.ShouldEndWith("@example.com");
        email.ShouldContain("Asa");
    }

    [Fact]
    public void Number_ranges_and_multiples()
    {
        for (var i = 0; i < 1000; i++)
        {
            _faker.Number.Int(10, 20).ShouldBeInRange(10, 20);
            (_faker.Number.Int(0, 100, multipleOf: 5) % 5).ShouldBe(0);
            var d = _faker.Number.Double(1, 2, fractionDigits: 2);
            d.ShouldBeInRange(1, 2);
            Math.Round(d, 2).ShouldBe(d);
            var m = _faker.Number.Decimal(0, 10, 3);
            m.ShouldBeInRange(0m, 10m);
            decimal.Round(m, 3).ShouldBe(m);
        }
    }

    [Fact]
    public void Price_has_charm_endings_and_stays_in_range()
    {
        var prices = Enumerable.Range(0, 500).Select(_ => _faker.Commerce.Price(1, 100)).ToList();
        prices.ShouldAllBe(p => p >= 1 && p <= 100);
        prices.Count(p => decimal.Truncate(p * 100) % 10 == 9).ShouldBeGreaterThan(150);
    }

    [Fact]
    public void Dates_are_relative_to_the_reference_date()
    {
        var reference = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var faker = new Faker(seed: 1) { ReferenceDate = reference };
        for (var i = 0; i < 200; i++)
        {
            faker.Date.Past().ShouldBeInRange(reference.AddYears(-1), reference);
            faker.Date.Future().ShouldBeInRange(reference, reference.AddYears(1));
            faker.Date.Recent(3).ShouldBeInRange(reference.AddDays(-3), reference);
            var birth = faker.Date.Birthdate(18, 30);
            var age = reference.Year - birth.Year - (new DateOnly(reference.Year, birth.Month, Math.Min(birth.Day, 28)) > DateOnly.FromDateTime(reference.Date) ? 1 : 0);
            age.ShouldBeInRange(17, 30);
        }
    }

    [Fact]
    public void Ulid_and_uuid_v7_encode_the_timestamp()
    {
        var at = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _faker.String.Ulid(at).ShouldMatch("^01HK[0-9A-HJKMNP-TV-Z]{22}$");
        _faker.String.UuidV7(at).ShouldMatch("^018cc251-f400-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
    }

    [Fact]
    public void Word_length_filters_and_strategies()
    {
        _faker.Word.Noun(5, 5).Length.ShouldBe(5);
        Should.Throw<PhoneyDataException>(() => _faker.Word.Noun(100, 100));
        _faker.Word.Noun(100, 100, WordLengthStrategy.Longest).Length.ShouldBeGreaterThan(5);
        _faker.Word.Noun(100, 100, WordLengthStrategy.Closest).Length.ShouldBeGreaterThan(5);
    }

    [Fact]
    public void Full_name_honours_given_parts()
    {
        for (var i = 0; i < 50; i++)
        {
            var name = _faker.Person.FullName("Ada", "Lovelace");
            name.ShouldContain("Ada");
            name.ShouldContain("Lovelace");
        }
    }

    [Fact]
    public void Gendered_names_come_from_the_gendered_lists()
    {
        var sv = new Faker("sv", seed: 3);
        var female = Locales.Data("sv").Get("person.first_name.female").ShouldBeOfType<Phoney.Data.StringsEntry>().Values;
        for (var i = 0; i < 50; i++)
            female.ShouldContain(sv.Person.FirstName(Sex.Female));
    }

    [Fact]
    public void Color_values_format_as_css()
    {
        _faker.Color.Rgb().ShouldMatch("^#[0-9a-f]{6}$");
        _faker.Color.RgbValues(includeAlpha: true).ToCss().ShouldStartWith("rgba(");
        _faker.Color.Hsl().ToCss().ShouldMatch("^hsl\\(\\d+deg \\d+% \\d+%\\)$");
        _faker.Color.ColorByCssColorSpace(CssSpace.DisplayP3).ShouldStartWith("color(display-p3 ");
    }

    [Fact]
    public void Jwt_has_three_base64url_parts() =>
        _faker.Internet.Jwt().ShouldMatch("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9]{64}$");

    [Fact]
    public void Mime_types_and_extensions_are_consistent()
    {
        var mime = _faker.System.MimeType();
        mime.ShouldContain('/');
        _faker.System.FileExt("image/png").ShouldBe("png");
        Should.Throw<ArgumentException>(() => _faker.System.FileExt("nope/nope"));
    }
}
