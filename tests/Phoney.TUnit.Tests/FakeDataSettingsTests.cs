namespace Phoney.TUnit.Tests;

/// <summary>The settings of [FakeData] read back what was set, and default to "not set".</summary>
public sealed class FakeDataSettingsTests
{
    [Test]
    public void Settings_default_to_one_case_and_nothing_else_set()
    {
        var attribute = new FakeDataAttribute();

        (attribute.Count, attribute.Seed, attribute.Locale, attribute.NullProbability, attribute.ReferenceDate).ShouldBe((1, 0L, (string?)null, 0d, (string?)null));
    }

    [Test]
    public void Settings_read_back_what_was_set()
    {
        var attribute = new FakeDataAttribute { Count = 3, Seed = 42, Locale = "sv", NullProbability = 0.25, ReferenceDate = "2030-01-01" };

        (attribute.Count, attribute.Seed, attribute.Locale, attribute.NullProbability, attribute.ReferenceDate).ShouldBe((3, 42L, "sv", 0.25, "2030-01-01"));
    }
}
