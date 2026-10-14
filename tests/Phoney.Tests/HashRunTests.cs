using System.Text.RegularExpressions;

namespace Phoney.Tests;

/// <summary>Replacing runs of <c>#</c> by hand (the building and apartment numbers of every address) must give what the pattern <c>#+</c> gave, with the same draws.</summary>
public sealed class HashRunTests
{
    [Test]
    [Arguments("")]
    [Arguments("no hashes")]
    [Arguments("#")]
    [Arguments("###")]
    [Arguments("Apt. ###")]
    [Arguments("Suite #-##x#")]
    [Arguments("##/###/#")]
    [Arguments("#####trailing")]
    public void Runs_are_replaced_like_the_pattern_does(string text)
    {
        var expected = new Faker("en", seed: 11);
        var actual = new Faker("en", seed: 11);

        var oracle = Regex.Replace(text, "#+", m => expected.String.Numeric(m.Length, allowLeadingZeros: false));

        actual.Helpers.ReplaceHashRunsWithoutLeadingZero(text).ShouldBe(oracle);
        actual.Random.Int(0, int.MaxValue).ShouldBe(expected.Random.Int(0, int.MaxValue)); // the same draws were made
    }
}
