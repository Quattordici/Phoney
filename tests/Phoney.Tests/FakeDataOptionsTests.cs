using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>The settings and seed rules every test framework's <c>[FakeData]</c> shares.</summary>
public sealed class FakeDataOptionsTests
{
    private static readonly FakeArgument[] Arguments = [new("email", typeof(string)), new("age", typeof(int))];

    [Test]
    public void Defaults_are_one_case_and_no_overrides()
    {
        var options = new FakeDataOptions();

        options.Count.ShouldBe(1);
        options.Seed.ShouldBeNull();
        options.Locale.ShouldBeNull();
        options.NullProbability.ShouldBe(0);
        options.ReferenceDate.ShouldBeNull();
    }

    [Test]
    public void Settings_are_validated()
    {
        var options = new FakeDataOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.Count = 0).ParamName.ShouldBe("Count");
        Should.Throw<ArgumentOutOfRangeException>(() => options.NullProbability = 1.5).ParamName.ShouldBe("NullProbability");
        Should.Throw<ArgumentOutOfRangeException>(() => options.NullProbability = -0.1);
        Should.Throw<ArgumentOutOfRangeException>(() => options.NullProbability = double.NaN);
        var date = Should.Throw<ArgumentException>(() => options.ReferenceDate = "next tuesday");
        date.ParamName.ShouldBe("ReferenceDate");
        date.Message.ShouldContain("2030-01-01");
        Should.Throw<ArgumentException>(() => options.ReferenceDate = "12:00"); // a time alone would take today's date, so seeds would drift
        options.ReferenceDate.ShouldBeNull();
        options.ReferenceDate = "2030-01-01";
        options.ReferenceDate = null;
        Should.Throw<ArgumentNullException>(() => options.CreateGenerator(null!, "T.M"));
        Should.Throw<ArgumentException>(() => options.CreateGenerator(Arguments, " "));
    }

    [Test]
    public void The_default_seed_depends_on_the_test_and_the_arguments_only_and_never_changes()
    {
        // Pinned: a changed formula would silently move the data of every existing [FakeData] test.
        FakeDataOptions.DefaultSeed("Shop.OrderTests.Places_order", Arguments).ShouldBe(FakeDataOptions.DefaultSeed("Shop.OrderTests.Places_order", Arguments));
        FakeDataOptions.DefaultSeed("Shop.OrderTests.Places_order", Arguments).ShouldNotBe(FakeDataOptions.DefaultSeed("Shop.OrderTests.Cancels_order", Arguments));
        FakeDataOptions.DefaultSeed("Shop.OrderTests.Places_order", Arguments).ShouldNotBe(FakeDataOptions.DefaultSeed("Shop.OrderTests.Places_order", [Arguments[0]]));
        FakeDataOptions.DefaultSeed("Shop.OrderTests.Places_order", Arguments).ShouldBe(-1706052241072025365L);
    }

    [Test]
    public void The_default_seed_of_generic_types_does_not_depend_on_assembly_versions()
    {
        // FullName puts "System.Private.CoreLib, Version=…" in type arguments, which differs between .NET 8 and .NET 10 (CI runs both) and with every release.
        FakeArgument[] arguments = [new("quantity", typeof(int?)), new("emails", typeof(List<string>)), new("scores", typeof(Dictionary<string, int[]>[]))];
        FakeDataOptions.DefaultSeed("Shop.OrderTests.Places_order", arguments).ShouldBe(PinnedGenericSeed);
        FakeDataOptions.StableName(typeof(Dictionary<string, int[]>[])).ShouldBe("System.Collections.Generic.Dictionary`2[[System.String],[System.Int32[]]][]");
        FakeDataOptions.TestName(typeof(GenericTests<int>), "Runs").ShouldBe("Phoney.Tests.FakeDataOptionsTests+GenericTests`1[[System.Int32]].Runs");
        FakeDataOptions.TestName(typeof(FakeDataOptionsTests), "Runs").ShouldBe("Phoney.Tests.FakeDataOptionsTests.Runs");
    }

    private const long PinnedGenericSeed = 3979569184680146208L; // also computed in Python from the identity string with the version-free names

    private sealed class GenericTests<T>;

    [Test]
    public void An_explicit_seed_wins_and_the_environment_seed_moves_the_derived_one()
    {
        FakeDataOptions.ResolveSeed(42, "T.M", Arguments, environmentSeed: 7).ShouldBe(42);

        var derived = FakeDataOptions.ResolveSeed(null, "T.M", Arguments, environmentSeed: null);
        var moved = FakeDataOptions.ResolveSeed(null, "T.M", Arguments, environmentSeed: 7);

        derived.ShouldBe(FakeDataOptions.DefaultSeed("T.M", Arguments));
        moved.ShouldNotBe(derived);
        FakeDataOptions.ResolveSeed(null, "T.M", Arguments, environmentSeed: 7).ShouldBe(moved);
        FakeDataOptions.ResolveSeed(null, "T.M", Arguments, environmentSeed: 8).ShouldNotBe(moved);
    }

    [Test]
    public void Rows_are_reproducible_and_follow_locale_and_reference_date()
    {
        var options = new FakeDataOptions { Locale = "de-AT", ReferenceDate = "2030-01-01" };
        var arguments = new FakeArgument[] { new("country", typeof(string)), new("birthDate", typeof(DateOnly)) };

        var first = options.CreateGenerator(arguments, "T.M", environmentSeed: null).Row(0);
        var again = options.CreateGenerator(arguments, "T.M", environmentSeed: null).Row(0);

        first[0].ShouldBe("Österreich");
        first.ShouldBe(again);
        ((DateOnly)first[1]!).Year.ShouldBeLessThan(2013);
        new FakeDataOptions { Seed = 5 }.CreateGenerator(arguments, "T.M", null).Row(1)
            .ShouldBe(new FakeDataOptions { Seed = 5 }.CreateGenerator(arguments, "Other.Name", null).Row(1)); // an explicit seed ignores the test's name
    }
}
