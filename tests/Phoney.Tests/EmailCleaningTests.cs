using System.Text.RegularExpressions;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>Cleaning the local part of an email by hand (no <see cref="Regex"/>, which costs a process milliseconds on its first use) must give what the patterns gave.</summary>
public sealed partial class EmailCleaningTests
{
    [GeneratedRegex("[^A-Za-z0-9._+-]+")]
    private static partial Regex InvalidChars();

    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex RepeatedDots();

    private static IEnumerable<string> Samples()
    {
        string[] fixedSamples = ["", "plain", "a.b", "a..b", "a...b..c", "..", ".", "a b", "o'brien", "Zoë", "a-b_c+d", "x@y", "tab\there", "ünï.cödé..x", "é́", "𝒜b", "a.-.b", "@@", "100%", "end.", ".start"];
        foreach (var sample in fixedSamples)
            yield return sample;

        // Every string of up to 5 characters over an alphabet with all the interesting classes.
        const string alphabet = "a.-_+Z9 é'@";
        for (var length = 1; length <= 5; length++)
        {
            var total = (int)Math.Pow(alphabet.Length, length);
            for (var n = 0; n < total; n++)
            {
                var chars = new char[length];
                var rest = n;
                for (var i = 0; i < length; i++)
                {
                    chars[i] = alphabet[rest % alphabet.Length];
                    rest /= alphabet.Length;
                }

                yield return new string(chars);
            }
        }
    }

    [Test]
    public void Invalid_characters_are_removed_like_the_pattern_does()
    {
        foreach (var sample in Samples())
            InternetModule.RemoveInvalidEmailChars(sample).ShouldBe(InvalidChars().Replace(sample, ""), $"for '{sample}'");
    }

    [Test]
    public void Runs_of_dots_become_one_dot_like_the_pattern_does()
    {
        foreach (var sample in Samples())
            InternetModule.CollapseDots(sample).ShouldBe(RepeatedDots().Replace(sample, "."), $"for '{sample}'");
    }

    [Test]
    public void A_string_that_needs_no_cleaning_is_returned_as_it_is()
    {
        var text = "already.clean-1";

        InternetModule.RemoveInvalidEmailChars(text).ShouldBeSameAs(text);
        InternetModule.CollapseDots(text).ShouldBeSameAs(text);
    }
}
