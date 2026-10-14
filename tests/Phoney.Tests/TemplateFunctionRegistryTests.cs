using Phoney.Templates;

namespace Phoney.Tests;

/// <summary>The built-in template functions are registered module by module, the first time one of the module is asked for.</summary>
public sealed class TemplateFunctionRegistryTests
{
    [Test]
    public void Every_built_in_function_is_found_by_name_in_any_casing()
    {
        TemplateFunctions.Contains("Person.FirstName").ShouldBeTrue();
        TemplateFunctions.Contains("person.firstname").ShouldBeTrue();
        TemplateFunctions.Contains("NATIONALID.PERSON").ShouldBeTrue();
        TemplateFunctions.Contains("number.double").ShouldBeTrue(); // an alias of number.float
        TemplateFunctions.Contains("person.noSuchThing").ShouldBeFalse();
        TemplateFunctions.Contains("nomodule").ShouldBeFalse();
        TemplateFunctions.Names.Count.ShouldBeGreaterThanOrEqualTo(236); // the built-ins; other tests register more
    }

    [Test]
    public void The_names_include_every_module_even_when_none_was_used_before()
    {
        var names = TemplateFunctions.Names;

        foreach (var module in new[] { "helpers", "number", "string", "person", "location", "internet", "nationalId", "phone", "company", "commerce", "finance", "date", "lorem", "word", "color", "animal", "book", "music", "food", "hacker", "vehicle", "airline", "database", "system", "git", "image" })
            names.ShouldContain(n => n.StartsWith(module + ".", StringComparison.OrdinalIgnoreCase), $"module {module}");
    }

    [Test]
    public void A_registered_function_replaces_the_built_in_even_if_its_module_was_not_loaded_yet()
    {
        // Registered before anything of the module "music" was used in this test, and kept by the module's later registration.
        TemplateFunctions.Register("music.genre", (_, _) => "custom genre");
        try
        {
            new Faker("en", seed: 1).Parse("{{music.genre}}").ShouldBe("custom genre");
            new Faker("en", seed: 1).Parse("{{music.songName}}").ShouldNotBeNullOrEmpty(); // the rest of the module is there
        }
        finally
        {
            TemplateFunctions.Register("music.genre", (faker, _) => faker.Music.Genre());
        }
    }

    [Test]
    public async Task Threads_that_ask_for_a_module_at_the_same_time_all_find_its_functions()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => TemplateFunctions.Contains("vehicle.vin") && TemplateFunctions.Contains("airline.flightNumber"))));

        results.ShouldAllBe(found => found);
    }
}
