using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>The transliteration table that usernames and emails of non-Latin names are built with.</summary>
public sealed class CharMappingsTests
{
    [Test]
    public void The_table_has_every_letter_of_the_faker_js_mapping()
    {
        CharMappings.Count.ShouldBe(281);
    }

    [Test]
    [Arguments('Ж', "Zh")]
    [Arguments('ж', "zh")]
    [Arguments('Щ', "Sch")]
    [Arguments('ש', "sh")]
    [Arguments('ψ', "ps")]
    [Arguments('خ', "kh")]
    [Arguments('Ձ', "Dz")]
    [Arguments('Ъ', "")]
    public void Letters_map_to_their_replacement(char letter, string expected)
    {
        CharMappings.TryMap(letter, out var mapped).ShouldBeTrue();
        mapped.ShouldBe(expected);
    }

    [Test]
    public void ASCII_letters_and_unknown_characters_have_no_replacement()
    {
        CharMappings.TryMap('a', out _).ShouldBeFalse();
        CharMappings.TryMap('é', out _).ShouldBeFalse();
    }

    [Test]
    public void Usernames_of_cyrillic_and_ascii_names_are_made_ascii()
    {
        var faker = new Faker("en", seed: 5);

        faker.Internet.Username("Иван", "Петров").ShouldMatch("^Ivan[._]Petrov[0-9]*$");
        faker.Internet.Username("O'Brien", "Mc Donald").ShouldMatch("^OBrien[._]McDonald[0-9]*$");
        faker.Internet.Username("Zoë", "Müller").ShouldMatch("^Zoe[._]Muller[0-9]*$");
    }
}
