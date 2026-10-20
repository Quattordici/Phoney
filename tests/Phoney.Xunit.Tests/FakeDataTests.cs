using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Phoney.Generation;
using Xunit.Sdk;

namespace Phoney.Xunit.Tests;

public sealed record Customer(string FirstName, string LastName, string Email, Address Address, List<string> Tags);

public sealed record Address(string Street, string City, string ZipCode);

/// <summary>[FakeData] on xUnit theories.</summary>
public sealed class FakeDataTests
{
    [Theory, FakeData]
    public void Parameters_describe_the_same_person(string firstName, string lastName, string email, DateOnly birthDate, int age)
    {
        var local = email[..email.IndexOf('@')];
        (local.Contains(Ascii(firstName), StringComparison.OrdinalIgnoreCase) ||
         local.Contains(Ascii(lastName), StringComparison.OrdinalIgnoreCase)).ShouldBeTrue($"{firstName} {lastName} <{email}>");

        // Seeded data uses the fixed reference date, so the age is computed from it.
        var today = DateOnly.FromDateTime(Faker.DefaultSeededReferenceDate.UtcDateTime);
        age.ShouldBe(today.Year - birthDate.Year - (today < birthDate.AddYears(today.Year - birthDate.Year) ? 1 : 0));

        static string Ascii(string s) => new(s.Normalize(System.Text.NormalizationForm.FormKD).Where(char.IsAsciiLetter).ToArray());
    }

    [Theory, FakeData(Count = 20)]
    public void Data_annotations_are_honoured([Range(1, 10)] int quantity, [EmailAddress] string contact, [AllowedValues("S", "M", "L")] string size)
    {
        quantity.ShouldBeInRange(1, 10);
        new EmailAddressAttribute().IsValid(contact).ShouldBeTrue();
        size.ShouldBeOneOf("S", "M", "L");
    }

    [Theory, FakeData]
    public void Objects_and_collections_are_populated(Customer customer, Address[] addresses)
    {
        customer.Email.ShouldContain("@");
        customer.Address.City.ShouldNotBeNullOrWhiteSpace();
        customer.Tags.ShouldNotBeEmpty();
        addresses.ShouldNotBeEmpty();
    }

    [Theory, FakeData(Locale = "de-AT")]
    public void Locale_is_honoured(string country, string countryCode)
    {
        country.ShouldBe("Österreich");
        countryCode.ShouldBe("AT");
    }

    [Theory, FakeData(Seed = 42)]
    public void Seed_is_honoured(string email) =>
        email.ShouldBe((string)new ArgumentGenerator([new FakeArgument("email", typeof(string))]).Seed(42).Row(0)[0]!);

    [Theory, FakeData(ReferenceDate = "2030-01-01")]
    public void Reference_date_is_honoured(DateTime createdAt) =>
        createdAt.ShouldBeInRange(new DateTime(2025, 1, 1), new DateTime(2030, 1, 1));

    [Theory, FakeData(NullProbability = 1)]
    public void Null_probability_applies_to_nullable_parameters(string? nickname, [Required] string? name, int count)
    {
        nickname.ShouldBeNull();
        name.ShouldNotBeNull();
        count.ShouldBeOfType<int>();
    }

    [Theory, FakeData(Count = 2), FakeData(Count = 2, Locale = "sv")]
    public void Several_attributes_add_their_cases(string city) => city.ShouldNotBeNullOrWhiteSpace();
}

/// <summary>What the attribute hands to xUnit, independent of a running test.</summary>
public sealed class FakeDataRowTests
{
    private static MethodInfo Method(string name) => typeof(FakeDataRowTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Sample(string email, [Range(1, 3)] int quantity) { }

    [Fact]
    public async Task Count_gives_that_many_distinct_rows_with_one_value_per_parameter()
    {
        var rows = await new FakeDataAttribute { Count = 4 }.GetData(Method(nameof(Sample)), new DisposalTracker());

        rows.Count.ShouldBe(4);
        rows.ShouldAllBe(r => r.GetData().Length == 2);
        rows.Select(r => (string)r.GetData()[0]!).Distinct().Count().ShouldBe(4);
        rows.ShouldAllBe(r => (int)r.GetData()[1]! >= 1 && (int)r.GetData()[1]! <= 3);
    }

    [Fact]
    public async Task Rows_are_the_same_every_time_they_are_asked_for()
    {
        var attribute = new FakeDataAttribute { Count = 3 };

        var first = await attribute.GetData(Method(nameof(Sample)), new DisposalTracker());
        var second = await attribute.GetData(Method(nameof(Sample)), new DisposalTracker());

        first.Select(r => (string)r.GetData()[0]!).ShouldBe(second.Select(r => (string)r.GetData()[0]!));
        attribute.SupportsDiscoveryEnumeration().ShouldBeTrue();
    }

    [Fact]
    public async Task The_seed_is_derived_from_the_test_so_other_tests_get_other_data()
    {
        var one = await new FakeDataAttribute().GetData(Method(nameof(Sample)), new DisposalTracker());
        var other = await new FakeDataAttribute().GetData(typeof(FakeDataTests).GetMethod(nameof(FakeDataTests.Seed_is_honoured))!, new DisposalTracker());

        ((string)one.Single().GetData()[0]!).ShouldNotBe((string)other.Single().GetData()[0]!);
    }

    [Fact]
    public void Invalid_settings_and_arguments_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeDataAttribute { Count = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeDataAttribute { NullProbability = 2 });
        Should.Throw<ArgumentNullException>(async () => await new FakeDataAttribute().GetData(null!, new DisposalTracker()));
    }
}
