using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Phoney.Generation;
using Phoney.Templates;

namespace Phoney.Tests;

/// <summary>Small public members: descriptions, conversions, optional lookups and members reached through an interface.</summary>
public sealed class PublicHelperTests
{
    [Test]
    public void A_locale_describes_itself_by_code_and_title()
    {
        Locales.Get("sv").ToString().ShouldBe("sv (Swedish)");

        Locales.Register("xx_untitled", l => l.FallbackTo("en"));
        Locales.Get("xx_untitled").ToString().ShouldBe("xx_untitled");
    }

    [Test]
    public void A_data_exception_keeps_its_cause()
    {
        var cause = new FormatException("bad");

        var exception = new PhoneyDataException("broken data", cause);

        exception.Message.ShouldBe("broken data");
        exception.InnerException.ShouldBeSameAs(cause);
    }

    [Test]
    public void Try_pick_returns_a_value_or_null_instead_of_throwing()
    {
        var faker = new Faker("az", seed: 1);

        faker.TryPick("animal.dog").ShouldNotBeNullOrWhiteSpace();
        faker.TryPick("no.such.path").ShouldBeNull();
        faker.TryPick("location.state").ShouldBeNull(); // faker.js marks states as not applicable to Azerbaijani
        Should.Throw<PhoneyDataUnavailableException>(() => faker.Pick("location.state"));
    }

    [Test]
    public void Fork_and_keyed_through_the_interface_are_the_fakers_own()
    {
        IFaker faker = new Faker("sv", seed: 3);

        faker.Fork(4).Person.FullName().ShouldBe(new Faker("sv", seed: 3).Fork(4).Person.FullName());
        faker.Keyed("customer:1").Person.FullName().ShouldBe(new Faker("sv").Keyed("customer:1").Person.FullName());
        faker.Fork(4).Locale.ShouldBe("sv");
    }

    [Test]
    public void Subset_sizes_from_methods_equal_the_implicit_conversions()
    {
        var exact = SubsetSize.FromInt32(3);
        var range = SubsetSize.FromTuple((1, 4));

        (exact.Min, exact.Max).ShouldBe((3, 3));
        (range.Min, range.Max).ShouldBe((1, 4));
        exact.ShouldBe((SubsetSize)3);
        range.ShouldBe((SubsetSize)(1, 4));
    }

    [Test]
    public void An_argument_generator_lists_its_arguments_and_sizes_its_collections()
    {
        FakeArgument[] arguments = [new("tags", typeof(List<string>)), new("count", typeof(int))];
        var generator = Fake.Arguments(arguments);

        generator.Arguments.Select(a => a.Name).ShouldBe(["tags", "count"]);
        generator.CollectionSize(4, 4).Seed(1).Generate(20).ShouldAllBe(row => ((List<string>)row[0]!).Count == 4);
        generator.CollectionSize(0, 0).Seed(1).Generate()[0].ShouldBeOfType<List<string>>().ShouldBeEmpty(); // zero means none
        Should.Throw<ArgumentOutOfRangeException>(() => generator.CollectionSize(-1, 2)).ParamName.ShouldBe("min");
        Should.Throw<ArgumentOutOfRangeException>(() => generator.CollectionSize(3, 2)).ParamName.ShouldBe("max");
    }

    [Test]
    public void A_collection_that_cannot_take_the_built_elements_says_what_to_use_instead()
    {
        // ReadOnlyCollection<T> is made from an IList<T> and ImmutableArray<T> from an IEnumerable<T>: neither takes a number.
        Should.Throw<InvalidOperationException>(() => CollectionConverters.Create(typeof(ReadOnlyCollection<int>), typeof(HashSet<int>)))
            .Message.ShouldContain("Use a List<T>, HashSet<T> or Dictionary<TKey, TValue> member");
        Should.Throw<InvalidOperationException>(() => CollectionConverters.Create(typeof(ImmutableArray<int>), typeof(int)))
            .Message.ShouldContain("ImmutableArray`1 is a collection Phoney builds");
        CollectionConverters.Create(typeof(List<int>), typeof(List<int>)).ShouldBeNull(); // nothing to convert
    }

    [Test]
    public void Template_functions_can_read_a_list_argument_as_their_own_array()
    {
        TemplateFunctions.Register("helperTests.joinReversed", (_, args) =>
        {
            var values = args.Strings(0);
            Array.Reverse(values); // a copy: the parsed template keeps its values
            return string.Join("-", values);
        });
        var faker = new Faker(seed: 1);

        faker.Parse("{{helperTests.joinReversed([\"a\", \"b\", \"c\"])}}").ShouldBe("c-b-a");
        faker.Parse("{{helperTests.joinReversed([\"a\", \"b\", \"c\"])}}").ShouldBe("c-b-a");
    }
}
