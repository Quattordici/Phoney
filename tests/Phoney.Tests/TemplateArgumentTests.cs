using System.Text.RegularExpressions;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>Template functions honour their options, given as an options object (<c>{{string.alpha({"length":8})}}</c>) or as the plain value.</summary>
public sealed class TemplateArgumentTests
{
    private static readonly char[] Abc = ['a', 'b', 'c'];
    private static readonly char[] Ab01 = ['A', 'B', '0', '1'];
    private static readonly char[] ZeroOne = ['0', '1'];
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Faker Faker(long seed = 5) => new("en", seed) { ReferenceDate = Reference };

    private static string Parse(string template, long seed = 5) => Faker(seed).Parse(template);

    private static IEnumerable<string> Many(string template, int count = 40) =>
        Enumerable.Range(0, count).Select(i => Parse(template, seed: 100 + i));

    [Test]
    public void Functions_without_arguments_use_their_defaults()
    {
        Parse("{{finance.creditCardNumber}}").ShouldMatch(@"^[\d\- ]+$");
        Parse("{{internet.mac}}").ShouldMatch("^([0-9a-f]{2}:){5}[0-9a-f]{2}$");
        Parse("{{commerce.price}}").ShouldMatch(@"^\d+\.\d{2}$");
        Parse("{{finance.amount}}").ShouldMatch(@"^\d+\.\d{2}$");
    }

    [Test]
    public void Text_options_are_read_from_an_options_object_or_the_plain_value()
    {
        Parse("{{internet.mac(\"-\")}}").ShouldMatch("^([0-9a-f]{2}-){5}[0-9a-f]{2}$");
        Parse("{{internet.mac({\"separator\":\"\"})}}").ShouldMatch("^[0-9a-f]{12}$");
        Parse("{{commerce.price({\"symbol\":\"$\"})}}").ShouldMatch(@"^\$\d+\.\d{2}$");
        Parse("{{finance.amount({\"symbol\":\"€\",\"dec\":0})}}").ShouldMatch(@"^€\d+$");
        Parse("{{finance.creditCardNumber({\"issuer\":\"visa\"})}}").ShouldStartWith("4");
        Parse("{{finance.creditCardNumber(visa)}}").ShouldStartWith("4");
    }

    [Test]
    public void String_casing_and_exclusions_are_honoured()
    {
        Many("{{string.alpha({\"length\":8,\"casing\":\"upper\"})}}").ShouldAllBe(s => Regex.IsMatch(s, "^[A-Z]{8}$", RegexOptions.None, TimeSpan.FromSeconds(5)));
        Many("{{string.alpha({\"length\":8,\"casing\":\"lower\"})}}").ShouldAllBe(s => Regex.IsMatch(s, "^[a-z]{8}$", RegexOptions.None, TimeSpan.FromSeconds(5)));
        Many("{{string.alpha({\"length\":30,\"casing\":\"lower\",\"exclude\":[\"a\",\"b\",\"c\"]})}}").ShouldAllBe(s => s.IndexOfAny(Abc) < 0);
        Many("{{string.alphanumeric({\"length\":30,\"casing\":\"upper\",\"exclude\":\"AB01\"})}}")
            .ShouldAllBe(s => s.IndexOfAny(Ab01) < 0 && !s.Any(char.IsLower));
        Many("{{string.numeric({\"length\":30,\"exclude\":[\"0\",\"1\"]})}}").ShouldAllBe(s => s.IndexOfAny(ZeroOne) < 0);
    }

    [Test]
    public void Prefixes_and_casing_of_number_strings_are_honoured()
    {
        Many("{{string.hexadecimal({\"length\":6,\"casing\":\"upper\",\"prefix\":\"\"})}}").ShouldAllBe(s => Regex.IsMatch(s, "^[0-9A-F]{6}$", RegexOptions.None, TimeSpan.FromSeconds(5)));
        Parse("{{string.hexadecimal({\"length\":4})}}").ShouldStartWith("0x");
        Parse("{{string.binary({\"length\":4,\"prefix\":\"b\"})}}").ShouldMatch("^b[01]{4}$");
        Parse("{{string.octal({\"length\":4,\"prefix\":\"\"})}}").ShouldMatch("^[0-7]{4}$");
        Many("{{color.hex({\"prefix\":\"0x\",\"casing\":\"upper\"})}}").ShouldAllBe(s => Regex.IsMatch(s, "^0x[0-9A-F]{6}$", RegexOptions.None, TimeSpan.FromSeconds(5)));
        Parse("{{color.hex({\"includeAlpha\":true})}}").ShouldMatch("^#[0-9a-f]{8}$");
    }

    [Test]
    public void The_sex_option_picks_the_same_names_as_the_api()
    {
        Parse("{{person.firstName({\"sex\":\"female\"})}}").ShouldBe(Faker().Person.FirstName(Sex.Female));
        Parse("{{person.firstName(male)}}").ShouldBe(Faker().Person.FirstName(Sex.Male));
        Parse("{{person.lastName({\"sex\":\"male\"})}}").ShouldBe(Faker().Person.LastName(Sex.Male));
        Parse("{{person.middleName({\"sex\":\"female\"})}}").ShouldBe(Faker().Person.MiddleName(Sex.Female));
        Parse("{{person.prefix({\"sex\":\"female\"})}}").ShouldBe(Faker().Person.Prefix(Sex.Female));
        Parse("{{person.firstName}}").ShouldBe(Faker().Person.FirstName());
        Should.Throw<FormatException>(() => Parse("{{person.firstName({\"sex\":\"robot\"})}}")).Message.ShouldContain("robot");
    }

    [Test]
    public void Full_name_takes_a_first_name_a_last_name_and_a_sex()
    {
        var name = Parse("{{person.fullName({\"firstName\":\"Ada\",\"lastName\":\"Lovelace\"})}}");

        name.ShouldContain("Ada");
        name.ShouldContain("Lovelace");
        Parse("{{person.fullName({\"sex\":\"female\"})}}").ShouldBe(Faker().Person.FullName(sex: Sex.Female));
    }

    [Test]
    public void Internet_options_reach_the_api()
    {
        Parse("{{internet.url({\"appendSlash\":true,\"protocol\":\"http\"})}}").ShouldMatch("^http://.+/$");
        Parse("{{internet.url}}").ShouldStartWith("https://");
        Parse("{{internet.email({\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"provider\":\"example.org\"})}}").ToLowerInvariant().ShouldMatch("^ada.*@example\\.org$");
        Parse("{{internet.username({\"firstName\":\"Ada\",\"lastName\":\"Lovelace\"})}}").ToLowerInvariant().ShouldContain("ada");
        Parse("{{internet.password({\"length\":20})}}").Length.ShouldBe(20);
        Parse("{{internet.password({\"length\":12,\"pattern\":\"[a-z]\",\"prefix\":\"pw_\"})}}")
            .ShouldBe(Faker().Internet.Password(12, false, new Regex("[a-z]", RegexOptions.None, TimeSpan.FromSeconds(5)), "pw_"));
    }

    [Test]
    public void Zip_codes_and_birthdates_take_their_options()
    {
        Parse("{{location.zipCode({\"format\":\"###-##\"})}}").ShouldMatch(@"^\d{3}-\d{2}$");
        Parse("{{location.zipCode(\"##-##\")}}").ShouldMatch(@"^\d{2}-\d{2}$");
        Many("{{date.birthdate({\"min\":30,\"max\":30,\"mode\":\"age\"})}}").ShouldAllBe(d => d.StartsWith("1994", StringComparison.Ordinal) || d.StartsWith("1995", StringComparison.Ordinal));
        Many("{{date.birthdate({\"min\":1990,\"max\":1992,\"mode\":\"year\"})}}").ShouldAllBe(d => d.StartsWith("1990", StringComparison.Ordinal) || d.StartsWith("1991", StringComparison.Ordinal) || d.StartsWith("1992", StringComparison.Ordinal));
        Many("{{date.birthdate}}").ShouldAllBe(d => string.CompareOrdinal(d, "1944") >= 0 && string.CompareOrdinal(d, "2008") <= 0);
    }

    [Test]
    public void Bad_option_values_are_reported_with_what_is_accepted()
    {
        Should.Throw<FormatException>(() => Parse("{{string.alpha({\"casing\":\"shouty\"})}}")).Message.ShouldContain("shouty");
    }

    [Test]
    public void A_password_pattern_that_no_character_matches_fails_instead_of_looping_forever()
    {
        var faker = Faker();
        var none = new Regex("[é]", RegexOptions.None, TimeSpan.FromSeconds(5));

        var thrown = Should.Throw<ArgumentException>(() => faker.Internet.Password(10, pattern: none));
        thrown.ParamName.ShouldBe("pattern");
        thrown.Message.ShouldContain("matches no");
        Should.Throw<ArgumentException>(() => Parse("{{internet.password({\"pattern\":\"[é]\"})}}"));
    }

    [Test]
    public void A_rare_password_pattern_still_works_and_gives_the_same_characters_as_before()
    {
        // One printable character in 94 matches: far below the cap of consecutive misses.
        var rare = new Regex("[#]", RegexOptions.None, TimeSpan.FromSeconds(5));

        Faker().Internet.Password(6, pattern: rare).ShouldBe("######");
        Faker().Internet.Password(12, pattern: new Regex("[a-z]", RegexOptions.None, TimeSpan.FromSeconds(5)), prefix: "x")
            .ShouldBe(Faker().Internet.Password(12, pattern: new Regex("[a-z]", RegexOptions.None, TimeSpan.FromSeconds(5)), prefix: "x"));
    }

    [Test]
    [Arguments("string.alpha({\"length\":100001})", 100000)]
    [Arguments("string.numeric({\"length\":2000000000})", 100000)]
    [Arguments("string.hexadecimal({\"length\":100001})", 100000)]
    [Arguments("string.sample(100001)", 100000)]
    [Arguments("string.nanoid(100001)", 100000)]
    [Arguments("internet.password({\"length\":2000000000})", 100000)]
    [Arguments("finance.accountNumber({\"length\":100001})", 100000)]
    [Arguments("finance.pin({\"length\":100001})", 100000)]
    [Arguments("git.commitSha({\"length\":100001})", 100000)]
    [Arguments("lorem.words(10001)", 10000)]
    [Arguments("lorem.sentence(10001)", 10000)]
    [Arguments("lorem.slug(10001)", 10000)]
    [Arguments("word.words({\"count\":10001})", 10000)]
    [Arguments("lorem.sentences(1001)", 1000)]
    [Arguments("lorem.lines(1001)", 1000)]
    [Arguments("lorem.paragraph(1001)", 1000)]
    [Arguments("lorem.paragraphs(301)", 300)]
    [Arguments("lorem.paragraphs(2000000000)", 300)]
    public void A_length_or_count_beyond_the_limit_is_refused_with_the_limit_in_the_message(string call, int limit)
    {
        var thrown = Should.Throw<FormatException>(() => Parse("{{" + call + "}}"));

        thrown.Message.ShouldContain("at most " + limit);
    }

    [Test]
    public void Compound_items_up_to_their_limits_stay_small()
    {
        Parse("{{lorem.paragraphs(300)}}").Length.ShouldBeLessThan(150_000);
        Parse("{{lorem.sentences(1000)}}").Length.ShouldBeLessThan(150_000);
        Parse("{{lorem.words(10000)}}").Length.ShouldBeLessThan(150_000);
    }

    [Test]
    public void Lengths_up_to_the_limit_and_the_defaults_still_work()
    {
        Parse("{{string.alpha({\"length\":100000})}}").Length.ShouldBe(100000);
        Parse("{{internet.password({\"length\":100000})}}").Length.ShouldBe(100000);
        Parse("{{string.alpha}}").Length.ShouldBe(1);
        Parse("{{string.nanoid}}").Length.ShouldBe(21);
        Parse("{{string.sample}}").Length.ShouldBe(10);
        Parse("{{internet.password}}").Length.ShouldBe(15);
        Parse("{{finance.pin}}").Length.ShouldBe(4);
    }

    [Test]
    public void A_plain_length_is_only_the_length_and_never_a_casing_or_a_prefix()
    {
        Parse("{{string.alpha(5)}}").ShouldMatch("^[A-Za-z]{5}$");
        Parse("{{string.alphanumeric(6)}}").ShouldMatch("^[A-Za-z0-9]{6}$");
        Parse("{{string.numeric(4)}}").ShouldMatch(@"^\d{4}$");
        Parse("{{string.hexadecimal(4)}}").ShouldMatch("^0x[0-9a-fA-F]{4}$");
        Parse("{{string.binary(4)}}").ShouldMatch("^0b[01]{4}$");
        Parse("{{string.octal(4)}}").ShouldMatch("^0o[0-7]{4}$");
        Parse("{{internet.password(20)}}").ShouldBe(Faker().Internet.Password(20));
    }

    [Test]
    public void A_plain_text_is_not_taken_for_every_option_of_a_function()
    {
        Parse("{{internet.email(x)}}").ShouldBe(Faker().Internet.Email());
        Parse("{{internet.username(x)}}").ShouldBe(Faker().Internet.Username());
        Parse("{{person.fullName(x)}}").ShouldBe(Faker().Person.FullName());
        Parse("{{internet.url(x)}}").ShouldStartWith("https://");
        Parse("{{commerce.price(x)}}").ShouldMatch(@"^\d+\.\d{2}$");
    }
}
