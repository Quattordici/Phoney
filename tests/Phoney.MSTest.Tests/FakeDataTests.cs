using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Phoney.Generation;

namespace Phoney.MSTest.Tests;

public sealed record Customer(string FirstName, string LastName, string Email, Address Address, List<string> Tags);

public sealed record Address(string Street, string City, string ZipCode);

/// <summary>[FakeData] on MSTest methods.</summary>
[TestClass]
public sealed class FakeDataTests
{
    [TestMethod, FakeData]
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

    [TestMethod, FakeData(Count = 20)]
    public void Data_annotations_are_honoured([Range(1, 10)] int quantity, [EmailAddress] string contact, [AllowedValues("S", "M", "L")] string size)
    {
        quantity.ShouldBeInRange(1, 10);
        new EmailAddressAttribute().IsValid(contact).ShouldBeTrue();
        size.ShouldBeOneOf("S", "M", "L");
    }

    [TestMethod, FakeData]
    public void Objects_and_collections_are_populated(Customer customer, Address[] addresses)
    {
        customer.Email.ShouldContain("@");
        customer.Address.City.ShouldNotBeNullOrWhiteSpace();
        customer.Tags.ShouldNotBeEmpty();
        addresses.ShouldNotBeEmpty();
    }

    [TestMethod, FakeData(Locale = "de-AT")]
    public void Locale_is_honoured(string country, string countryCode)
    {
        country.ShouldBe("Österreich");
        countryCode.ShouldBe("AT");
    }

    [TestMethod, FakeData(Seed = 42)]
    public void Seed_is_honoured(string email) =>
        email.ShouldBe((string)new ArgumentGenerator([new FakeArgument("email", typeof(string))]).Seed(42).Row(0)[0]!);

    [TestMethod, FakeData(ReferenceDate = "2030-01-01")]
    public void Reference_date_is_honoured(DateTime createdAt) =>
        createdAt.ShouldBeInRange(new DateTime(2025, 1, 1), new DateTime(2030, 1, 1));

    [TestMethod, FakeData(NullProbability = 1)]
    public void Null_probability_applies_to_nullable_parameters(string? nickname, [Required] string? name, int count)
    {
        nickname.ShouldBeNull();
        name.ShouldNotBeNull();
        count.ShouldBeOfType<int>();
    }

    [TestMethod, FakeData(Count = 2), FakeData(Count = 2, Locale = "sv")]
    public void Several_attributes_add_their_cases(string city) => city.ShouldNotBeNullOrWhiteSpace();
}

/// <summary>What the attribute hands to MSTest, independent of a running test.</summary>
[TestClass]
public sealed class FakeDataRowTests
{
    private static MethodInfo Method(string name) => typeof(FakeDataRowTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Sample(string email, [Range(1, 3)] int quantity) { }

    [TestMethod]
    public void Count_gives_that_many_distinct_rows_with_one_value_per_parameter()
    {
        var rows = new FakeDataAttribute { Count = 4 }.GetData(Method(nameof(Sample))).ToList();

        rows.Count.ShouldBe(4);
        rows.ShouldAllBe(r => r.Length == 2);
        rows.Select(r => (string)r[0]!).Distinct().Count().ShouldBe(4);
        rows.ShouldAllBe(r => (int)r[1]! >= 1 && (int)r[1]! <= 3);
    }

    [TestMethod]
    public void Rows_are_the_same_every_time_they_are_asked_for()
    {
        var attribute = new FakeDataAttribute { Count = 3 };

        var first = attribute.GetData(Method(nameof(Sample))).Select(r => (string)r[0]!).ToList();
        var second = attribute.GetData(Method(nameof(Sample))).Select(r => (string)r[0]!).ToList();

        first.ShouldBe(second);
        attribute.GetDisplayName(Method(nameof(Sample)), null).ShouldBeNull();
    }

    [TestMethod]
    public void The_seed_is_derived_from_the_test_so_other_tests_get_other_data()
    {
        var one = new FakeDataAttribute().GetData(Method(nameof(Sample))).Single();
        var other = new FakeDataAttribute().GetData(typeof(FakeDataTests).GetMethod(nameof(FakeDataTests.Seed_is_honoured))!).Single();

        ((string)one[0]!).ShouldNotBe((string)other[0]!);
    }

    [TestMethod]
    public void Invalid_settings_and_arguments_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeDataAttribute { Count = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeDataAttribute { NullProbability = 2 });
        Should.Throw<ArgumentNullException>(() => new FakeDataAttribute().GetData(null!));
    }
}
