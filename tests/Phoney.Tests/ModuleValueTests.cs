using System.Globalization;
using System.Text.RegularExpressions;
using Phoney.Data;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>The module methods that pick locale data or shape a value without a checksum: where the value comes from, its format and its options.</summary>
public sealed partial class ModuleValueTests
{
    private const int Draws = 300;

    private readonly Faker _faker = new("en", seed: 2026);

    /// <summary>Methods that return one value of a locale data entry.</summary>
    private static readonly (int Key, Func<Faker, string> Pick)[] DataPicks =
    [
        (DataKeys.AnimalDog, f => f.Animal.Dog()),
        (DataKeys.AnimalCat, f => f.Animal.Cat()),
        (DataKeys.AnimalSnake, f => f.Animal.Snake()),
        (DataKeys.AnimalBear, f => f.Animal.Bear()),
        (DataKeys.AnimalLion, f => f.Animal.Lion()),
        (DataKeys.AnimalCetacean, f => f.Animal.Cetacean()),
        (DataKeys.AnimalHorse, f => f.Animal.Horse()),
        (DataKeys.AnimalBird, f => f.Animal.Bird()),
        (DataKeys.AnimalCow, f => f.Animal.Cow()),
        (DataKeys.AnimalFish, f => f.Animal.Fish()),
        (DataKeys.AnimalCrocodilia, f => f.Animal.Crocodilia()),
        (DataKeys.AnimalInsect, f => f.Animal.Insect()),
        (DataKeys.AnimalRabbit, f => f.Animal.Rabbit()),
        (DataKeys.AnimalRodent, f => f.Animal.Rodent()),
        (DataKeys.DatabaseType, f => f.Database.Type()),
        (DataKeys.DatabaseCollation, f => f.Database.Collation()),
        (DataKeys.DatabaseEngine, f => f.Database.Engine()),
        (DataKeys.VehicleType, f => f.Vehicle.Type()),
        (DataKeys.VehicleFuel, f => f.Vehicle.Fuel()),
        (DataKeys.VehicleBicycleType, f => f.Vehicle.Bicycle()),
        (DataKeys.ColorHuman, f => f.Vehicle.Color()),
        (DataKeys.BookAuthor, f => f.Book.Author()),
        (DataKeys.BookGenre, f => f.Book.Genre()),
        (DataKeys.BookPublisher, f => f.Book.Publisher()),
        (DataKeys.BookSeries, f => f.Book.Series()),
        (DataKeys.MusicAlbum, f => f.Music.Album()),
        (DataKeys.MusicArtist, f => f.Music.Artist()),
        (DataKeys.MusicGenre, f => f.Music.Genre()),
        (DataKeys.FoodEthnicCategory, f => f.Food.EthnicCategory()),
        (DataKeys.ColorSpace, f => f.Color.Space()),
        (DataKeys.LocationContinent, f => f.Location.Continent()),
        (DataKeys.WordAdverb, f => f.Word.Adverb()),
        (DataKeys.WordConjunction, f => f.Word.Conjunction()),
        (DataKeys.WordInterjection, f => f.Word.Interjection()),
        (DataKeys.WordPreposition, f => f.Word.Preposition()),
        (DataKeys.WordVerb, f => f.Word.Verb()),
    ];

    public static IEnumerable<string> DataPickPaths() => DataPicks.Select(p => DataKeys.Paths[p.Key]);

    [Test]
    [MethodDataSource(nameof(DataPickPaths))]
    public void A_data_pick_returns_values_of_its_entry_and_more_than_one_of_them(string path)
    {
        var (key, pick) = DataPicks.Single(p => DataKeys.Paths[p.Key] == path);
        var values = ((StringsEntry)_faker.Data.Get(key)!).Values;

        var picked = Enumerable.Range(0, 50).Select(_ => pick(_faker)).ToList();

        picked.ShouldAllBe(v => values.Contains(v), path);
        if (values.Count > 1)
            picked.Distinct().Count().ShouldBeGreaterThan(1, path);
    }

    [Test]
    [MethodDataSource(nameof(DataPickPaths))]
    public void A_data_pick_is_reproducible_for_a_seed(string path)
    {
        var pick = DataPicks.Single(p => DataKeys.Paths[p.Key] == path).Pick;

        Enumerable.Range(0, 10).Select(_ => pick(new Faker("en", seed: 5))).Distinct().Count().ShouldBe(1, path);
    }

    [Test]
    public void Parts_of_speech_honour_word_lengths()
    {
        Func<int?, int?, WordLengthStrategy, string>[] parts =
        [
            _faker.Word.Adverb, _faker.Word.Conjunction, _faker.Word.Interjection, _faker.Word.Preposition, _faker.Word.Verb,
        ];
        foreach (var part in parts)
        {
            for (var i = 0; i < 20; i++)
                part(4, 6, WordLengthStrategy.Fail).Length.ShouldBeInRange(4, 6);
        }
    }

    [Test]
    public void The_shortest_strategy_falls_back_to_the_shortest_words()
    {
        var shortest = ((StringsEntry)_faker.Data.Get(DataKeys.WordVerb)!).Values.Min(v => v.Length);

        for (var i = 0; i < 20; i++)
            _faker.Word.Verb(500, 600, WordLengthStrategy.Shortest).Length.ShouldBe(shortest);
    }

    [Test]
    public void Parts_of_speech_reject_negative_lengths_and_name_the_parameter()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _faker.Word.Adverb(minLength: -1)).ParamName.ShouldBe("minLength");
        Should.Throw<ArgumentOutOfRangeException>(() => _faker.Word.Verb(maxLength: -1)).ParamName.ShouldBe("maxLength");
        Should.Throw<ArgumentException>(() => _faker.Word.Preposition(5, 2)).ParamName.ShouldBe("maxLength");
        _faker.Word.Conjunction(0, strategy: WordLengthStrategy.Closest).ShouldNotBeNullOrEmpty(); // zero is no lower bound, not an error
    }

    [Test]
    public void Text_has_every_shape_and_never_an_empty_one()
    {
        var texts = Enumerable.Range(0, Draws).Select(_ => _faker.Lorem.Text()).ToList();

        texts.ShouldAllBe(t => t.Trim().Length > 0 && !t.Contains("{{"));
        texts.ShouldContain(t => t.Contains('\n'));  // paragraphs or lines
        texts.ShouldContain(t => !t.Contains('\n')); // a sentence or sentences
    }

    [Test]
    public void Zip_codes_by_state_are_in_the_range_of_the_state()
    {
        var faker = new Faker("en_US", seed: 3);

        for (var i = 0; i < 50; i++)
            int.Parse(faker.Location.ZipCodeByState("CA"), CultureInfo.InvariantCulture).ShouldBeInRange(90001, 96162);
        faker.Parse("{{location.zipCodeByState(\"AK\")}}").ShouldStartWith("99");
    }

    [Test]
    public void A_zip_code_for_an_unknown_state_says_which_state_and_locale()
    {
        var exception = Should.Throw<PhoneyDataException>(() => new Faker("en_US").Location.ZipCodeByState("XX"));

        exception.Message.ShouldContain("'XX'");
        exception.Message.ShouldContain("en_US");
        Should.Throw<ArgumentException>(() => new Faker("en_US").Location.ZipCodeByState(" "));
    }

    [Test]
    public void Currency_parts_belong_to_one_currency()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var currency = new Faker("en", seed).Finance.Currency();
            new Faker("en", seed).Finance.CurrencyName().ShouldBe(currency.Name);
            new Faker("en", seed).Finance.CurrencyNumericCode().ShouldBe(currency.NumericCode);
            new Faker("en", seed).Finance.CurrencyNumericCode().ShouldMatch("^[0-9]{3}$");
        }
    }

    [Test]
    public void Currency_symbols_skip_currencies_without_one()
    {
        var symbols = Enumerable.Range(0, Draws).Select(_ => _faker.Finance.CurrencySymbol()).ToList();

        symbols.ShouldAllBe(s => s.Length > 0);
        symbols.Distinct().Count().ShouldBeGreaterThan(5);
    }

    [Test]
    public void Card_verification_values_and_ethereum_addresses_have_their_format()
    {
        for (var i = 0; i < Draws; i++)
        {
            _faker.Finance.CreditCardCvv().ShouldMatch("^[0-9]{3}$");
            _faker.Finance.EthereumAddress().ShouldMatch("^0x[0-9a-f]{40}$");
        }
    }

    [Test]
    [Arguments(BitcoinAddressFamily.Legacy, BitcoinNetwork.Mainnet, "1", 26, 34)]
    [Arguments(BitcoinAddressFamily.Legacy, BitcoinNetwork.Testnet, "m", 26, 34)]
    [Arguments(BitcoinAddressFamily.Segwit, BitcoinNetwork.Mainnet, "3", 26, 34)]
    [Arguments(BitcoinAddressFamily.Segwit, BitcoinNetwork.Testnet, "2", 26, 34)]
    [Arguments(BitcoinAddressFamily.Bech32, BitcoinNetwork.Mainnet, "bc1", 42, 42)]
    [Arguments(BitcoinAddressFamily.Bech32, BitcoinNetwork.Testnet, "tb1", 42, 42)]
    [Arguments(BitcoinAddressFamily.Taproot, BitcoinNetwork.Mainnet, "bc1p", 62, 62)]
    [Arguments(BitcoinAddressFamily.Taproot, BitcoinNetwork.Testnet, "tb1p", 62, 62)]
    public void Bitcoin_addresses_have_the_prefix_length_and_alphabet_of_their_family(BitcoinAddressFamily family, BitcoinNetwork network, string prefix, int min, int max)
    {
        // Base58 (Bitcoin's alphabet: no 0, O, I or l) for the base58check families, the bech32 data alphabet (BIP 173) for the others.
        const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        const string Bech32 = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        var alphabet = family is BitcoinAddressFamily.Legacy or BitcoinAddressFamily.Segwit ? Base58 : Bech32;
        for (var i = 0; i < 100; i++)
        {
            var address = _faker.Finance.BitcoinAddress(family, network);
            address.ShouldStartWith(prefix);
            address.Length.ShouldBeInRange(min, max);
            address[prefix.Length..].ShouldAllBe(c => alphabet.Contains(c), address);
        }
    }

    [Test]
    public void A_bitcoin_address_without_a_family_is_one_of_every_family()
    {
        var prefixes = Enumerable.Range(0, Draws).Select(_ => _faker.Finance.BitcoinAddress()).Select(a => a.StartsWith("bc1p", StringComparison.Ordinal) ? "bc1p" : a.StartsWith("bc1", StringComparison.Ordinal) ? "bc1" : a[..1]);

        prefixes.Distinct().Order(StringComparer.Ordinal).ShouldBe(["1", "3", "bc1", "bc1p"]);
    }

    [Test]
    public void Litecoin_addresses_are_base58_with_a_litecoin_prefix()
    {
        for (var i = 0; i < Draws; i++)
            _faker.Finance.LitecoinAddress().ShouldMatch("^[LM3][1-9A-HJ-NP-Za-km-z]{25,32}$");
    }

    [Test]
    public void Bics_follow_iso_9362_with_a_known_country()
    {
        var withBranch = 0;
        for (var i = 0; i < Draws; i++)
        {
            var bic = _faker.Finance.Bic();
            bic.ShouldMatch("^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$");
            IbanData.Iso3166.ShouldContain(bic[4..6]);
            if (bic.Length == 11)
                withBranch++;
        }

        withBranch.ShouldBeInRange(1, Draws - 1); // random when not asked for
        _faker.Finance.Bic(includeBranchCode: true).Length.ShouldBe(11);
        _faker.Finance.Bic(includeBranchCode: false).Length.ShouldBe(8);
    }

    [Test]
    public void Display_names_keep_letters_but_drop_spaces_and_apostrophes()
    {
        var names = Enumerable.Range(0, Draws).Select(_ => _faker.Internet.DisplayName("Åsa Li", "O'Brien")).ToList();

        names.ShouldAllBe(n => n.StartsWith("ÅsaLi", StringComparison.Ordinal) && !n.Contains(' ') && !n.Contains('\''));
        names.ShouldContain(n => DisplayNameFirstAndNumber().IsMatch(n));
        names.ShouldContain(n => DisplayNameFirstAndLast().IsMatch(n));
        names.ShouldContain(n => DisplayNameFirstLastAndNumber().IsMatch(n));
        names.ShouldAllBe(n => DisplayNameFirstAndNumber().IsMatch(n) || DisplayNameFirstAndLast().IsMatch(n) || DisplayNameFirstLastAndNumber().IsMatch(n));
        _faker.Internet.DisplayName().ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Protocols_http_methods_and_ports_are_the_documented_values()
    {
        var protocols = Enumerable.Range(0, 100).Select(_ => _faker.Internet.Protocol()).Distinct().Order(StringComparer.Ordinal);
        var methods = Enumerable.Range(0, Draws).Select(_ => _faker.Internet.HttpMethod()).Distinct().Order(StringComparer.Ordinal);

        protocols.ShouldBe(["http", "https"]);
        methods.ShouldBe(["DELETE", "GET", "PATCH", "POST", "PUT"]);
        for (var i = 0; i < Draws; i++)
            _faker.Internet.Port().ShouldBeInRange(1, 65535);
    }

    [Test]
    public void A_domain_name_is_a_domain_word_and_a_suffix_drawn_like_a_url()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var name = new Faker("en", seed).Internet.DomainName();
            var dot = name.IndexOf('.', StringComparison.Ordinal);
            var (word, suffix) = (name[..dot], name[(dot + 1)..]);

            InternetModule.IsDomainSlug(word).ShouldBeTrue(name);
            ((StringsEntry)_faker.Data.Get(DataKeys.InternetDomainSuffix)!).Values.ShouldContain(suffix);
            new Faker("en", seed).Internet.DomainWord().ShouldBe(word);
            new Faker("en", seed).Internet.Url(appendSlash: false).ShouldBe("https://" + name);
        }
    }

    [Test]
    public void Emails_with_special_characters_stay_valid_addresses()
    {
        const string Specials = "!#$%&'*+/=?^`{|}~";
        var emails = Enumerable.Range(0, Draws).Select(_ => _faker.Internet.Email(allowSpecialCharacters: true)).ToList();

        // RFC 5322 dot-atom local part: atext characters, dots only between them.
        emails.ShouldAllBe(e => DotAtomEmail().IsMatch(e));
        emails.ShouldContain(e => LocalPart(e).IndexOfAny(Specials.ToCharArray()) >= 0);
        _faker.Internet.ExampleEmail("Ada", "Lovelace", allowSpecialCharacters: true).ShouldMatch("@example\\.(com|net|org)$");
    }

    private static string LocalPart(string email) => email[..email.IndexOf('@', StringComparison.Ordinal)];

    [Test]
    public void Memorable_passwords_alternate_consonants_and_vowels()
    {
        for (var i = 0; i < 100; i++)
        {
            _faker.Internet.Password(12, memorable: true).ShouldMatch("^([b-df-hj-np-tv-z][aeiou])*[b-df-hj-np-tv-z]?$");
            _faker.Internet.Password(8, memorable: true).Length.ShouldBe(8);
        }

        _faker.Internet.Password(10, memorable: true, prefix: "Xy").ShouldStartWith("Xy"); // after a consonant comes a vowel
        _faker.Internet.Password(10, memorable: true, prefix: "Xy")[2].ShouldBeOneOf('a', 'e', 'i', 'o', 'u');
    }

    [Test]
    public void File_types_are_the_top_level_types_of_the_mime_types()
    {
        var mime = (RecordsEntry)_faker.Data.Get(DataKeys.SystemMimeType)!;
        var topLevel = Enumerable.Range(0, mime.Count).Select(row => mime.Get(row, 0)!.Split('/')[0]).ToHashSet(StringComparer.Ordinal);

        var fileTypes = Enumerable.Range(0, Draws).Select(_ => _faker.System.FileType()).ToList();
        var common = Enumerable.Range(0, Draws).Select(_ => _faker.System.CommonFileType()).Distinct().Order(StringComparer.Ordinal);

        fileTypes.ShouldAllBe(t => topLevel.Contains(t));
        fileTypes.Distinct().Count().ShouldBeGreaterThan(3);
        common.ShouldBe(["application", "audio", "image", "text", "video"]);
    }

    [Test]
    public void Network_interfaces_follow_the_predictable_naming_scheme()
    {
        var names = Enumerable.Range(0, Draws).Select(_ => _faker.System.NetworkInterface()).ToList();

        // systemd's predictable names: on-board (o), hot-plug slot (s), MAC (x) and PCI (p…s…), optionally with a PCI domain (P).
        names.ShouldAllBe(n => NetworkInterfaceName().IsMatch(n));
        foreach (var shape in new[] { "^(en|wl|ww)o", "^(en|wl|ww)s", "^(en|wl|ww)x", "^(P[0-9])?(en|wl|ww)p" })
            names.ShouldContain(n => Regex.IsMatch(n, shape), shape);
    }

    [Test]
    public void Cron_fields_are_in_range()
    {
        for (var i = 0; i < Draws; i++)
        {
            var fields = _faker.System.Cron(includeYear: true).Split(' ');

            fields.Length.ShouldBe(6);
            InRangeOr(fields[0], 0, 59, "*").ShouldBeTrue(fields[0]);
            InRangeOr(fields[1], 0, 23, "*").ShouldBeTrue(fields[1]);
            InRangeOr(fields[2], 1, 31, "*", "?").ShouldBeTrue(fields[2]);
            InRangeOr(fields[3], 1, 12, "*").ShouldBeTrue(fields[3]);
            InRangeOr(fields[4], 0, 6, "*", "?", "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT").ShouldBeTrue(fields[4]);
            InRangeOr(fields[5], 1970, 2099, "*").ShouldBeTrue(fields[5]);
        }

        static bool InRangeOr(string field, int min, int max, params string[] others) =>
            others.Contains(field) || (int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max);
    }

    [Test]
    public void Cron_macros_only_come_when_asked_for()
    {
        string[] macros = ["@annually", "@daily", "@hourly", "@monthly", "@reboot", "@weekly", "@yearly"];

        Enumerable.Range(0, Draws).Select(_ => _faker.System.Cron()).ShouldAllBe(c => c.Split(' ', StringSplitOptions.None).Length == 5);
        var mixed = Enumerable.Range(0, Draws).Select(_ => _faker.System.Cron(includeNonStandard: true)).ToList();
        mixed.ShouldContain(c => macros.Contains(c));
        mixed.ShouldContain(c => c.Split(' ').Length == 5);
    }

    [Test]
    public void Record_locators_leave_out_digits_and_look_alikes_unless_allowed()
    {
        for (var i = 0; i < Draws; i++)
        {
            _faker.Airline.RecordLocator().ShouldMatch("^[A-HJKMNP-Z]{6}$");
            _faker.Airline.RecordLocator(allowNumerics: true).ShouldMatch("^[A-HJKMNP-Z2-9]{6}$");
            _faker.Airline.RecordLocator(allowVisuallySimilarCharacters: true).ShouldMatch("^[A-Z]{6}$");
            _faker.Airline.RecordLocator(allowNumerics: true, allowVisuallySimilarCharacters: true).ShouldMatch("^[A-Z0-9]{6}$");
        }
    }

    [Test]
    public void Vehicle_registration_marks_have_the_uk_shape() =>
        Enumerable.Range(0, Draws).Select(_ => _faker.Vehicle.Vrm()).ShouldAllBe(v => Regex.IsMatch(v, "^[A-Z]{2}[0-9]{2}[A-Z]{3}$"));

    [Test]
    public void MongoDb_object_ids_are_24_lower_case_hex_characters() =>
        Enumerable.Range(0, Draws).Select(_ => _faker.Database.MongoDbObjectId()).ShouldAllBe(id => Regex.IsMatch(id, "^[0-9a-f]{24}$"));

    [Test]
    [Arguments((byte)255, (byte)0, (byte)16, null, "#ff0010")]
    [Arguments((byte)1, (byte)2, (byte)3, 1.0, "#010203ff")]
    [Arguments((byte)1, (byte)2, (byte)3, 0.0, "#01020300")]
    [Arguments((byte)1, (byte)2, (byte)3, 0.5, "#01020380")]
    public void Rgb_colors_format_as_hex(byte r, byte g, byte b, double? alpha, string expected) =>
        new RgbColor(r, g, b, alpha).ToHex().ShouldBe(expected);

    [Test]
    public void Card_formats_repeat_a_character_a_fixed_or_random_number_of_times()
    {
        for (var i = 0; i < 100; i++)
        {
            var fixedLength = _faker.Finance.CreditCardNumber("4#{4}-L");
            var ranged = _faker.Finance.CreditCardNumber("5#{3,6}-L");

            fixedLength.ShouldMatch("^4[0-9]{4}-[0-9]$");
            ranged.ShouldMatch("^5[0-9]{3,6}-[0-9]$");
            HelpersModule.LuhnCheck(fixedLength).ShouldBeTrue(fixedLength);
            HelpersModule.LuhnCheck(ranged).ShouldBeTrue(ranged);
        }

        Enumerable.Range(0, 100).Select(_ => _faker.Finance.CreditCardNumber("5#{3,6}-L").Length).Distinct().Count().ShouldBe(4);
    }

    [Test]
    [Arguments(@"\P{Lu}{8}", "^[a-z0-9]{8}$")]
    [Arguments(@"\P{Nd}{8}", "^[A-Za-z]{8}$")]
    [Arguments(@"\P{L}{8}", "^[0-9]{8}$")]
    public void Negated_unicode_categories_give_the_other_alphanumeric_characters(string pattern, string check)
    {
        for (var i = 0; i < 100; i++)
            _faker.Helpers.FromRegExp(pattern).ShouldMatch(check);
    }

    [Test]
    public void Decimals_between_ends_with_more_digits_than_a_long_holds_are_in_range_and_rounded()
    {
        // 1.00000000000000000000001 has a 77-bit mantissa, so the ends can't be scaled into a long directly.
        const decimal Min = 1.00000000000000000000001m;
        const decimal Max = 4.99999999999999999999999m;
        var values = Enumerable.Range(0, Draws).Select(_ => _faker.Number.Decimal(Min, Max, 2)).ToList();

        values.ShouldAllBe(v => v >= 1.01m && v <= 4.99m && decimal.Round(v, 2) == v);
        values.Distinct().Count().ShouldBeGreaterThan(100);
        new Faker("en", seed: 1).Number.Decimal(Min, Max, 2).ShouldBe(new Faker("en", seed: 1).Number.Decimal(Min, Max, 2));
        Should.Throw<ArgumentException>(() => _faker.Number.Decimal(1.00000000000000000000001m, 1.00900000000000000000001m, 2)).ParamName.ShouldBe("decimals");
    }

    [Test]
    public void Address_templates_are_the_location_methods()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            new Faker("en_US", seed).Parse("{{location.secondaryAddress}}").ShouldBe(new Faker("en_US", seed).Location.SecondaryAddress());
            new Faker("en_US", seed).Parse("{{location.postalAddress}}").ShouldBe(new Faker("en_US", seed).Location.PostalAddress());
        }
    }

    [GeneratedRegex("^ÅsaLi[0-9]{1,2}$")]
    private static partial Regex DisplayNameFirstAndNumber();

    [GeneratedRegex("^ÅsaLi[._]OBrien$")]
    private static partial Regex DisplayNameFirstAndLast();

    [GeneratedRegex("^ÅsaLi[._]OBrien[0-9]{1,2}$")]
    private static partial Regex DisplayNameFirstLastAndNumber();

    [GeneratedRegex("^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@[a-z0-9.-]+\\.[a-z]+$")]
    private static partial Regex DotAtomEmail();

    [GeneratedRegex("^(?:(?:en|wl|ww)(?:o[0-9]|s[0-9](?:f[0-9])?(?:d[0-9])?|x[0-9a-f]{12})|(?:P[0-9])?(?:en|wl|ww)p[0-9]s[0-9](?:f[0-9])?(?:d[0-9])?)$")]
    private static partial Regex NetworkInterfaceName();
}
