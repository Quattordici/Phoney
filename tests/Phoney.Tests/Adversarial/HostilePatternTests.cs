using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Phoney.Modules;

namespace Phoney.Tests.Adversarial;

/// <summary>
/// Patterns written to hurt <c>Helpers.FromRegExp</c> (and so <c>[RegularExpression]</c> members): nesting deep enough to overflow the stack, repetitions that
/// would need gigabytes, syntax .NET rejects, and random patterns checked against .NET's own <see cref="Regex"/>.
/// </summary>
public sealed class HostilePatternTests
{
    private static string Generate(string pattern) => new Faker("en", seed: 1).Helpers.FromRegExp(pattern);

    [Test]
    [Arguments(1_000)]
    [Arguments(100_000)]
    public void Groups_nested_too_deep_are_a_format_error_instead_of_a_stack_overflow(int depth)
    {
        var pattern = new string('(', depth) + "a" + new string(')', depth);

        Should.Throw<FormatException>(() => Generate(pattern)).Message.ShouldContain("nested");
    }

    [Test]
    public void Groups_nested_as_deep_as_real_patterns_go_still_work() =>
        Generate(new string('(', 100) + "a" + new string(')', 100)).ShouldBe("a");

    [Test]
    [Arguments("a{2000000000}")]
    [Arguments("a{1,2000000000}")]
    [Arguments("((a{1000}){1000}){1000}")]
    [Arguments("(\\d{100000}|x){1000}")]
    [Arguments("(a+){100000}")]
    [Arguments("(){2000000000}")] // nothing repeated two billion times is two billion loop turns, so it counts like characters
    [Arguments("(a{0}){2000000000}")]
    [Arguments("((){100000}){100000}")]
    [Arguments("(|){2000000000}")]
    public void Patterns_that_could_produce_gigabytes_are_refused_up_front(string pattern)
    {
        var watch = Stopwatch.StartNew();

        Should.Throw<FormatException>(() => Generate(pattern)).Message.ShouldContain("characters");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Long_but_reasonable_repetitions_still_work()
    {
        Generate("a{100000}").Length.ShouldBe(100_000);
        Generate("(\\d{100}-){1000}").Length.ShouldBe(101_000);
        Generate("(){100000}").ShouldBeEmpty();
        Generate("a{0}").ShouldBeEmpty();
    }

    [Test]
    [Arguments("a{99999999999}")]
    [Arguments("a{1,99999999999}")]
    [Arguments("a{99999999999,}")]
    public void A_repetition_count_beyond_an_int_is_a_format_error_like_in_dotnet(string pattern)
    {
        Should.Throw<ArgumentException>(() => new Regex(pattern)); // .NET refuses it too
        Should.Throw<FormatException>(() => Generate(pattern)).Message.ShouldContain("too large");
    }

    [Test]
    [Arguments("*")]
    [Arguments("+a")]
    [Arguments("?")]
    [Arguments("a|*")]
    [Arguments("(*)")]
    [Arguments("a**")]
    [Arguments("a+*")]
    [Arguments("a{2}{3}")]
    [Arguments("a?+")]
    [Arguments("a{2}*")]
    public void A_quantifier_without_something_to_repeat_is_a_format_error_like_in_dotnet(string pattern)
    {
        Should.Throw<ArgumentException>(() => new Regex(pattern));
        Should.Throw<FormatException>(() => Generate(pattern)).Message.ShouldContain("quantifier");
    }

    [Test]
    [Arguments("a*?", "")]
    [Arguments("a+?b", "ab")]
    [Arguments("a??", "")]
    [Arguments("a{2}?", "aa")]
    [Arguments("a{,5}", "a{,5}")]
    [Arguments("{", "{")]
    [Arguments("a{x}", "a{x}")]
    public void Lazy_quantifiers_and_literal_braces_are_still_read_as_dotnet_reads_them(string pattern, string shortest)
    {
        var value = Generate(pattern);

        Regex.IsMatch(value, "^(?:" + pattern + ")$").ShouldBeTrue(value);
        value.Length.ShouldBeGreaterThanOrEqualTo(shortest.Length);
    }

    [Test]
    public void A_reversed_range_of_characters_that_are_not_printable_names_them_by_code()
    {
        var error = Should.Throw<FormatException>(() => Generate("[😀-😂]"));

        error.Message.ShouldContain("\\uDE00-\\uD83D");
    }

    /// <summary>
    /// Random patterns built from the supported syntax: whatever .NET accepts, the generated text matches it (when the pattern has no lookaround or inner anchor),
    /// and nothing but a <see cref="FormatException"/> ever comes out.
    /// </summary>
    [Test]
    public void Random_patterns_give_text_that_dotnet_regex_matches()
    {
        var faker = new Faker("en", seed: 2024);
        var checkedPatterns = 0;
        for (var i = 0; i < 3_000; i++)
        {
            var pattern = RandomPattern(faker, depth: 0);
            PatternGenerator generator;
            try
            {
                generator = PatternGenerator.Get(pattern);
            }
            catch (FormatException)
            {
                continue;
            }

            var regex = new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
            for (var n = 0; n < 5; n++)
            {
                var value = generator.Generate(faker.Random);
                if (generator.IsExact)
                    regex.IsMatch(value).ShouldBeTrue($"'{value}' does not match {pattern}");
            }

            checkedPatterns++;
        }

        checkedPatterns.ShouldBeGreaterThan(2_500);
    }

    /// <summary>Random text made of regular expression syntax: parsing it gives a generator or a <see cref="FormatException"/>, never another exception or a hang.</summary>
    [Test]
    public void Random_syntax_soup_is_either_a_pattern_or_a_format_error()
    {
        const string Soup = "ab01()[]{}^$|*+?.,-\\:=!<>'dwsDWSpPxu{}LNu2F";
        var faker = new Faker("en", seed: 7);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 20_000; i++)
        {
            var pattern = new string([.. Enumerable.Range(0, faker.Random.Int(1, 12)).Select(_ => Soup[faker.Random.Index(Soup.Length)])]);
            try
            {
                _ = PatternGenerator.Get(pattern).Generate(faker.Random);
            }
            catch (FormatException)
            {
                // refused with a message: fine
            }
        }

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    private static string RandomPattern(Faker faker, int depth)
    {
        var sb = new StringBuilder();
        var atoms = faker.Random.Int(1, 4);
        for (var a = 0; a < atoms; a++)
        {
            sb.Append(faker.Random.Int(0, depth < 3 ? 9 : 7) switch
            {
                0 => faker.Random.Element(["a", "Z", "7", "-", "_", " "]),
                1 => faker.Random.Element(["\\d", "\\w", "\\s", "\\D", "\\W", "\\S", "\\p{L}", "\\p{Lu}", "\\P{Nd}", "\\x41", "\\u00e9", "\\.", "\\-"]),
                2 => ".",
                3 => faker.Random.Element(["[a-z]", "[A-F0-9]", "[^a-z]", "[\\d_]", "[a-]", "[-a]", "[^\\W]", "[\\u0041-\\u0043]", "[.]", "[]a]"]),
                4 => "b",
                5 => "é",
                6 => "\\b",
                7 => "x",
                8 => "(" + RandomPattern(faker, depth + 1) + ")",
                _ => "(?:" + RandomPattern(faker, depth + 1) + "|" + RandomPattern(faker, depth + 1) + ")",
            });
            sb.Append(faker.Random.Int(0, 9) switch
            {
                0 => "?",
                1 => "*",
                2 => "+",
                3 => "{2}",
                4 => "{1,3}",
                5 => "{0,}",
                6 => "*?",
                _ => "",
            });
        }

        return sb.ToString();
    }
}
