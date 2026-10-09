using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.TUnit.Tests;

public sealed record Customer(string FirstName, string LastName, string Email, Address Address, List<string> Tags);

public sealed record Address(string Street, string City, string ZipCode);

/// <summary>[FakeData] on test methods.</summary>
public sealed class FakeDataTests
{
    private static readonly ConcurrentBag<string> CountedEmails = [];

    [Test, FakeData]
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

    [Test, FakeData(Count = 20)]
    public void Data_annotations_are_honoured([Range(1, 10)] int quantity, [EmailAddress] string contact, [AllowedValues("S", "M", "L")] string size)
    {
        quantity.ShouldBeInRange(1, 10);
        new EmailAddressAttribute().IsValid(contact).ShouldBeTrue();
        size.ShouldBeOneOf("S", "M", "L");
    }

    [Test, FakeData]
    public void Objects_and_collections_are_populated(Customer customer, Address[] addresses)
    {
        customer.Email.ShouldContain("@");
        customer.Address.City.ShouldNotBeNullOrWhiteSpace();
        customer.Tags.ShouldNotBeEmpty();
        addresses.ShouldNotBeEmpty();
    }

    [Test, FakeData(Count = 3)]
    public void Count_generates_test_cases(string email) => CountedEmails.Add(email);

    [After(Class)]
    public static void Every_counted_case_ran_with_its_own_values() =>
        CountedEmails.Distinct().Count().ShouldBe(3);

    [Test, FakeData(Locale = "de-AT")]
    public void Locale_is_honoured(string country, string countryCode)
    {
        country.ShouldBe("Österreich");
        countryCode.ShouldBe("AT");
    }

    [Test, FakeData(Seed = 42)]
    public void Seed_is_honoured(string email) =>
        email.ShouldBe((string)new ArgumentGenerator([new FakeArgument("email", typeof(string))]).Seed(42).Row(0)[0]!);

    [Test, FakeData(ReferenceDate = "2030-01-01")]
    public void Reference_date_is_honoured(DateTime createdAt) =>
        createdAt.ShouldBeInRange(new DateTime(2025, 1, 1), new DateTime(2030, 1, 1));

    [Test, FakeData(NullProbability = 1)]
    public void Null_probability_applies_to_nullable_parameters(string? nickname, [Required] string? name, int count)
    {
        nickname.ShouldBeNull();
        name.ShouldNotBeNull();
        count.ShouldBeOfType<int>();
    }

    [Test, FakeData]
    public void Custom_conventions_apply(string sku) => sku.ShouldStartWith("SKU-");
}

/// <summary>
/// Test data is generated while tests are discovered, before any TUnit hook runs, so conventions are registered in a
/// module initializer.
/// </summary>
internal static class Conventions
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => Fake.Conventions.Add("Sku", f => f.Random.Replace("SKU-####"));
}

/// <summary>Seeds, independent of a running test.</summary>
public sealed class FakeDataSeedTests
{
    private static readonly FakeArgument[] Email = [new("email", typeof(string))];

    [Test]
    public void Default_seed_is_stable_and_depends_on_the_test()
    {
        var seed = FakeDataAttribute.DefaultSeed(null, Email);
        FakeDataAttribute.DefaultSeed(null, Email).ShouldBe(seed);
        FakeDataAttribute.DefaultSeed(null, [new("contact", typeof(string))]).ShouldNotBe(seed);

        // FNV-1a is fixed, unlike string.GetHashCode: the seed is the same on every run and machine.
        seed.ShouldBe(FakeDataAttribute.DefaultSeed(null, Email.ToList()));
    }

    [Test]
    public void Generator_uses_the_attribute_settings()
    {
        var attribute = new FakeDataAttribute { Seed = 7, Locale = "sv" };
        var row = attribute.CreateGenerator([new("city", typeof(string))], null).Row(0);
        row.ShouldBe(new ArgumentGenerator([new FakeArgument("city", typeof(string))]).Seed(7).Locale("sv").Row(0));
    }

    [Test]
    public void Invalid_settings_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeDataAttribute { Count = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeDataAttribute { NullProbability = 2 });
    }
}

/// <summary>[FakeData] on the class fills constructor parameters.</summary>
[FakeData]
public sealed class ConstructorInjectionTests(string firstName, string email)
{
    [Test]
    public void Constructor_parameters_are_filled()
    {
        firstName.ShouldNotBeNullOrWhiteSpace();
        email.ShouldContain("@");
    }
}

/// <summary>[FakeData] on a required property fills it.</summary>
public sealed class PropertyInjectionTests
{
    [FakeData]
    public required Customer Customer { get; init; }

    [Test]
    public void Properties_are_filled() => Customer.Email.ShouldContain("@");
}
