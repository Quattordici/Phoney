using System.Globalization;
using System.Text.RegularExpressions;
using Phoney.Modules;
using Phoney.Templates;

namespace Phoney.Tests;

/// <summary>
/// The fast paths (integer decimal arithmetic, templates written into one buffer, one-pass emails, precompiled name patterns) must give exactly
/// what the straightforward code gave: same draws, same strings, same decimal bits (scale included).
/// </summary>
public sealed partial class FastPathParityTests
{
    [GeneratedRegex("^[a-z][a-z-]*[a-z]$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainSlug();

    /// <summary>Values captured before the fast paths existed, from <c>new Faker(locale, seed: 2025)</c> in this order.</summary>
    [Test]
    [Arguments("en", "4592 Tremblay Manors|8872 Rowan Wells Apt. 291|Raleighmouth|19950-7979|Obie Keebler|Eden_Rolfson70@gmail.com|Ryann36|https://disloyal-seagull.net/|90189,0,0,131072|2024-04-14T19:15:21.5900000+00:00")]
    [Arguments("de", "Hamberger Str. 92a|Tannenbergstr. 24a 8 OG|Bräutigamburg|41914|Dylan Weniger|Arda_Tivontschik@hotmail.com|Jamie.Sagafe42|https://freidenkend-drache.net|70389,0,0,131072|2024-11-15T02:35:03.7040000+00:00")]
    [Arguments("sv", "Norra Kyrkoallén 548|Parkallén 7 Lgh. 189|Österfred|48481|Linus Mohamed|Tommy.Danielsson74@yahoo.com|Ella88|https://grave-sundae.com/|92379,0,0,131072|2024-12-20T12:57:06.3100000+00:00")]
    [Arguments("ru", "наб. Тополиная, 354|Почтовая наб., 872 кв. 894|Шатки|484819|Арсений Иосифович Савельев|Vlas.Gerasimova28@yandex.ru|Olimpiada_Ignatov|https://vague-fraudster.сайт/|79335,0,0,131072|2024-02-12T15:48:39.6040000+00:00")]
    [Arguments("ja", "4丁目6番9号|4丁目9番5号 菅野872号室|港葵区|914-8481|野中 優斗|q5x.hlzfglfrx74@outlook.jp|q5x75|https://salty-finger.or.jp/|35603,0,0,131072|2024-12-20T12:57:06.3100000+00:00")]
    public void Seeded_values_are_the_ones_from_before_the_fast_paths(string locale, string expected)
    {
        var f = new Faker(locale, seed: 2025);
        string[] actual =
        [
            f.Location.StreetAddress(), f.Location.StreetAddress(useFullAddress: true), f.Location.City(), f.Location.ZipCode(), f.Person.FullName(),
            f.Internet.Email(), f.Internet.Username(), f.Internet.Url(), string.Join(",", decimal.GetBits(f.Commerce.Price())), f.Date.Past().ToString("O", CultureInfo.InvariantCulture),
        ];

        string.Join("|", actual).ShouldBe(expected);
    }

    [Test]
    public void Decimals_and_prices_match_decimal_arithmetic_bit_for_bit()
    {
        decimal[] edges =
        [
            0m, 0.5m, 1m, 1.00m, 9.99m, 10m, 100m, 999.995m, 1000m, 12345.6789m, 0.000000000000000000001m, 1e15m, 1e16m, 9.9e17m, 1e18m, 1.000000000000000001m,
            18446744073709551615m, 18446744073709551616m, 1e20m, decimal.MaxValue, -1m, -0.01m, -0.4m, -1000m, -1e18m, decimal.MinValue,
        ];
        var arguments = new SeededRandom(99);
        for (var i = 0; i < 20_000; i++)
        {
            var a = arguments.Bool(0.4) ? (decimal)arguments.Double(-5000, 5000) : edges[arguments.Index(edges.Length)];
            var b = arguments.Bool(0.4) ? (decimal)arguments.Double(-5000, 5000) : edges[arguments.Index(edges.Length)];
            if (arguments.Bool(0.3))
                a = decimal.Round(a, arguments.Int(0, 4));
            var (min, max) = a <= b ? (a, b) : (b, a);
            var decimals = arguments.Bool(0.6) ? arguments.Int(0, 4) : arguments.Int(0, 28);
            var seed = arguments.Long(0, 1000);

            Outcome(() => new Faker("en", seed).Number.Decimal(min, max, decimals))
                .ShouldBe(Outcome(() => Legacy.Decimal(new SeededRandom(seed), min, max, decimals)), $"Decimal({min}, {max}, {decimals}) seed {seed}");
            Outcome(() => new Faker("en", seed).Commerce.Price(min, max, decimals))
                .ShouldBe(Outcome(() => Legacy.Price(new SeededRandom(seed), min, max, decimals)), $"Price({min}, {max}, {decimals}) seed {seed}");
        }

        static string Outcome(Func<decimal> value)
        {
            try
            {
                return string.Join(",", decimal.GetBits(value()));
            }
            catch (ArgumentException e)
            {
                return e.GetType().Name + " " + e.ParamName;
            }
        }
    }

    [Test]
    public void Domain_slugs_are_checked_like_the_regular_expression()
    {
        string[] samples = ["", "a", "ab", "a-b", "-ab", "ab-", "a--b", "a1b", "Ab", "aB-c", "é", "aéb", "a_b", "a.b", "ab c", "AB"];
        foreach (var sample in samples)
            InternetModule.IsDomainSlug(sample).ShouldBe(DomainSlug().IsMatch(sample), sample);

        // What it is used for: slugs of the words of every locale.
        foreach (var locale in Locales.All)
        {
            var f = new Faker(locale.Code, seed: 1);
            for (var i = 0; i < 50; i++)
            {
                foreach (var word in new[] { Try(() => f.Word.Adjective()), Try(() => f.Word.Noun()), Try(() => f.Lorem.Word()) })
                {
                    var slug = HelpersModule.Slugify(word);
                    InternetModule.IsDomainSlug(slug).ShouldBe(DomainSlug().IsMatch(slug), $"{locale.Code}: {slug}");
                }
            }
        }

        static string Try(Func<string> word)
        {
            try
            {
                return word();
            }
            catch (PhoneyDataException)
            {
                return "";
            }
        }
    }

    [Test]
    [NotInParallel] // replaces a built-in template function for the whole process
    public void A_template_function_registered_over_a_writing_built_in_is_the_one_used()
    {
        var original = TemplateFunctions.Get("location.buildingNumber")!;
        TemplateFunctions.Register("location.buildingNumber", (_, _) => "42");
        try
        {
            new Faker("en", seed: 3).Parse("{{location.buildingNumber}} Fast Path Street").ShouldBe("42 Fast Path Street");
        }
        finally
        {
            TemplateFunctions.Register("location.buildingNumber", original);
        }
    }

    /// <summary>Decimal and price generation as it was written with decimal arithmetic only, the reference for the integer fast paths.</summary>
    private static class Legacy
    {
        public static decimal Decimal(SeededRandom random, decimal min, decimal max, int decimals)
        {
            if (max < min)
                throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
            ArgumentOutOfRangeException.ThrowIfNegative(decimals);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 28);

            var largest = Math.Max(Math.Abs(min), Math.Abs(max));
            if (decimals <= 18 && largest <= NumberModule.Pow10(18 - decimals))
            {
                var scale = NumberModule.Pow10(decimals);
                var first = decimal.Ceiling(min * scale);
                var last = decimal.Floor(max * scale);
                if (last < first)
                    throw new ArgumentException("no value", nameof(decimals));
                var drawn = random.Long(0, (long)last - (long)first);
                return decimal.Round((first + drawn) / scale, decimals);
            }

            var places = decimals;
            while (places > 0 && largest > decimal.MaxValue / NumberModule.Pow10(places))
                places--;

            var factor = NumberModule.Pow10(places);
            var low = decimal.Ceiling(min * factor);
            var high = decimal.Floor(max * factor);
            if (high < low)
                throw new ArgumentException("no value", nameof(decimals));
            if (low < 0 && high > decimal.MaxValue + low)
            {
                var share = (decimal)random.Double();
                var value = decimal.Floor((low * (1 - share)) + (high * share));
                return decimal.Round(Math.Clamp(value, low, high) / factor, places);
            }

            var range = high - low;
            var offset = range <= long.MaxValue
                ? random.Long(0, (long)range)
                : decimal.Floor((decimal)random.Double() * (range < decimal.MaxValue ? range + 1 : range));
            return decimal.Round((low + offset) / factor, places);
        }

        public static decimal Price(SeededRandom random, decimal min, decimal max, int decimals)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(decimals);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 28);
            if (min < 0 || max < 0)
                return 0;
            if (min == max)
                return decimal.Round(min, decimals);

            var generated = Decimal(random, min, max, decimals);
            if (decimals == 0)
                return generated;

            var places = decimals;
            while (places > 0 && generated >= NumberModule.Pow10(Math.Min(18, 28 - places)) && generated > decimal.MaxValue * new decimal(1, 0, 0, false, (byte)places))
                places--;
            var unit = new decimal(1, 0, 0, false, (byte)places);
            var lastDigit = decimal.Truncate(generated * NumberModule.Pow10(places)) % 10;
            var newLastDigit = random.Index(10) switch
            {
                < 5 => 9,
                < 8 => 5,
                8 => 0,
                _ => random.Index(10),
            };
            if (newLastDigit > lastDigit && generated > 79_228_162_514_264_337_593_543_950_326m)
                return generated;
            var combined = generated - (lastDigit * unit) + (newLastDigit * unit);
            return combined >= min && combined <= max ? combined : generated;
        }
    }
}
