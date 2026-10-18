using System.Text;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>
/// Usernames, emails and slugs strip diacritics by decomposing the text. With invariant globalization (<c>InvariantGlobalization</c>, common in
/// containers) <see cref="string.Normalize(NormalizationForm)"/> leaves non-ASCII text as it is, so Phoney carries its own table and the data stays
/// the same on every machine.
/// </summary>
public sealed class DecompositionTests
{
    [Test]
    public void The_table_agrees_with_the_platform_normalisation()
    {
        var entries = Decomposition.TableEntries();
        entries.Count.ShouldBeGreaterThan(900);
        foreach (var (text, decomposed) in entries)
        {
            Decomposition.WithTable(text).ShouldBe(decomposed);
            if (Decomposition.PlatformNormalizes)
                text.Normalize(NormalizationForm.FormKD).ShouldBe(decomposed, $"U+{char.ConvertToUtf32(text, 0):X4}");
            else
                Decomposition.FormKD(text).ShouldBe(decomposed); // invariant globalization: the table is what is used
        }
    }

    [Test]
    [Arguments("Zoë Müller")]
    [Arguments("Åsa Öberg-Ståhl")]
    [Arguments("Łukasz Żółć")] // Ł has no decomposition: it stays and is encoded later, as before
    [Arguments("Ἀθηνᾶ")]
    [Arguments("Йосиф")]
    [Arguments("한국어 이름")] // Hangul syllables decompose by formula
    [Arguments("ﬁ ① ㈜ ™ ½")]
    [Arguments("plain ascii")]
    public void Text_decomposes_like_the_platform(string text)
    {
        var expected = Decomposition.PlatformNormalizes ? text.Normalize(NormalizationForm.FormKD) : Pinned[text];

        Decomposition.WithTable(text).ShouldBe(expected);
        Decomposition.FormKD(text).ShouldBe(expected);
    }

    [Test]
    public void Usernames_and_slugs_drop_diacritics_with_any_globalization_mode()
    {
        var faker = new Faker(seed: 1);

        faker.Internet.Username("Zoë", "Müller").ShouldMatch("^Zoe[._]?Muller[0-9]*$");
        HelpersModule.Slugify("Åsa Öberg").ShouldBe("Asa-Oberg");
    }

    // FormKD of the cases above as Python's unicodedata (an independent implementation) gives it, for runs without ICU.
    private static readonly Dictionary<string, string> Pinned = new(StringComparer.Ordinal)
    {
        ["Zoë Müller"] = "Zoë Müller",
        ["Åsa Öberg-Ståhl"] = "Åsa Öberg-Ståhl",
        ["Łukasz Żółć"] = "Łukasz Żółć",
        ["Ἀθηνᾶ"] = "Ἀθηνᾶ",
        ["Йосиф"] = "Йосиф",
        ["한국어 이름"] = "한국어 이름",
        ["ﬁ ① ㈜ ™ ½"] = "fi 1 (주) TM 1⁄2",
        ["plain ascii"] = "plain ascii",
    };
}
