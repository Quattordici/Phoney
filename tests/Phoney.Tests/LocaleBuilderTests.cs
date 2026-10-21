using Phoney.Data;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>Every typed setter of <see cref="LocaleBuilder"/> and every value shape of a faker.js JSON definition reaches the generated values.</summary>
public sealed class LocaleBuilderTests
{
    /// <summary>Registers a locale under a code no other test uses and returns a seeded faker for it.</summary>
    private static Faker Register(string code, Action<LocaleBuilder> configure)
    {
        Locales.Register(code, l => configure(l.FallbackTo("en")));
        return new Faker(code, seed: 8);
    }

    [Test]
    public void Typed_setters_replace_the_data_their_module_method_reads()
    {
        var faker = Register("en_builder_typed", l => l
            .LastNames("Berg")
            .JobTitles("{{person.lastName}} Manager")
            .Streets("Storgatan")
            .States("Dalarna")
            .Countries("Sverige")
            .PhoneFormats("07#-### ## ##")
            .CompanyNames("{{person.lastName}} AB")
            .LoremWords("alfa", "beta"));

        for (var i = 0; i < 20; i++)
        {
            faker.Person.LastName().ShouldContain("Berg");
            faker.Person.LastName(Sex.Female).ShouldContain("Berg"); // sex-specific lists of the fallback are replaced too
            faker.Person.JobTitle().ShouldMatch("Berg.* Manager$");
            faker.Location.Street().ShouldBe("Storgatan");
            faker.Location.State().ShouldBe("Dalarna");
            faker.Location.Country().ShouldBe("Sverige");
            faker.Phone.Number().ShouldMatch(@"^07\d-\d{3} \d{2} \d{2}$");
            faker.Company.Name().ShouldMatch("Berg.* AB$");
            faker.Lorem.Word().ShouldBeOneOf("alfa", "beta");
        }
    }

    [Test]
    public void Sex_specific_last_names_leave_the_other_sex_alone()
    {
        var faker = Register("en_builder_last_female", l => l.Set("person.last_name_pattern.generic").LastNames(Sex.Female, "Petrova"));

        faker.Data.Get("person.last_name.female").ShouldBeOfType<StringsEntry>().Values.ShouldBe(["Petrova"]);
        faker.Data.Get("person.last_name.male").ShouldBeSameAs(Locales.Data("en").Get("person.last_name.male"));
    }

    [Test]
    public void Weighted_values_are_picked_by_weight_and_an_empty_list_removes_them()
    {
        var faker = Register("en_builder_weighted", l => l.SetWeighted("animal.dog", ("Tax", 9), ("Pudel", 1)));

        faker.Data.Get("animal.dog").ShouldBeOfType<WeightedEntry>();
        var dogs = Enumerable.Range(0, 1000).Select(_ => faker.Animal.Dog()).ToList();
        dogs.Count(d => d == "Tax").ShouldBeInRange(850, 950);
        dogs.Count(d => d == "Pudel").ShouldBeInRange(50, 150);
        dogs.Count(d => d == "Tax").ShouldBe(892); // the exact count for this seed, so a change in how weights are drawn shows

        var removed = Register("en_builder_weighted_removed", l => l.SetWeighted("animal.dog", ("Tax", 1)).SetWeighted("animal.dog"));
        removed.Data.Get("animal.dog").ShouldBeSameAs(Locales.Data("en").Get("animal.dog"));
    }

    [Test]
    public void Integer_lists_replace_numeric_data()
    {
        var faker = Register("en_builder_ints", l => l.SetInts("internet.http_status_code.success", 299));

        faker.Data.Get("internet.http_status_code.success").ShouldBeOfType<IntsEntry>().Kind.ShouldBe(EntryKind.Ints);
        faker.Internet.HttpStatusCode(HttpStatusCodeType.Success).ShouldBe(299);
    }

    [Test]
    public void Setters_reject_a_null_list()
    {
        var builder = new LocaleBuilder("en_builder_null");

        Should.Throw<ArgumentNullException>(() => builder.Set("animal.dog", null!));
        Should.Throw<ArgumentNullException>(() => builder.SetWeighted("animal.dog", null!));
        Should.Throw<ArgumentNullException>(() => builder.SetInts("internet.port", null!));
    }

    [Test]
    public void Json_definitions_import_every_value_shape()
    {
        const string Json = """
            {
              "metadata": { "title": "Builder test" },
              "animal": { "dog": [{ "value": "Tax", "weight": 3 }, { "value": "Pudel", "weight": 1 }], "cat": "Huskatt" },
              "internet": { "http_status_code": { "success": [201, 202] } },
              "airline": { "airline": [{ "name": "Phoney Air", "iataCode": "PH" }, { "name": "Seed Lines" }] },
              "location": { "state": null }
            }
            """;
        Locales.Register("en_builder_json", l => l.FallbackTo("en").ImportJson(Json));
        var faker = new Faker("en_builder_json", seed: 2);

        Locales.Get("en_builder_json").Title.ShouldBe("Builder test");
        faker.Data.Get("animal.dog").ShouldBeOfType<WeightedEntry>();
        faker.Animal.Cat().ShouldBe("Huskatt");
        faker.Internet.HttpStatusCode(HttpStatusCodeType.Success).ShouldBeOneOf(201, 202);
        var airlines = faker.Data.Get("airline.airline").ShouldBeOfType<RecordsEntry>();
        airlines.Kind.ShouldBe(EntryKind.Records);
        airlines.Fields.ShouldBe(["name", "iataCode"]);
        Enumerable.Range(0, 30).Select(_ => faker.Airline.Airline()).ShouldContain(new Airline("Seed Lines", ""));
        faker.Data.Has("location.state").ShouldBeFalse();
        faker.Data.Has("animal.cat").ShouldBeTrue();
        Should.Throw<PhoneyDataUnavailableException>(() => faker.Location.State());
    }

    [Test]
    [Arguments("[1, 2]", "A locale definition must be a JSON object")]
    [Arguments("""{ "animal": ["Tax"] }""", "Module 'animal' must be a JSON object")]
    [Arguments("""{ "animal": { "dog": ["Tax", 1] } }""", "'animal.dog' mixes value kinds")]
    [Arguments("""{ "animal": { "dog": 1 } }""", "Unsupported value at 'animal.dog'")]
    public void Malformed_json_definitions_say_where_they_are_wrong(string json, string message) =>
        Should.Throw<FormatException>(() => new LocaleBuilder("en_builder_bad").ImportJson(json)).Message.ShouldContain(message);
}
