using Phonery;
using Phonery.Generation;

// Exercises the AOT-relevant paths and exits non-zero on failure.
var faker = new Faker("sv", seed: 42);
Check(faker.Person.FullName(), "person");
Check(faker.Internet.Email(), "email");
Check(faker.Location.StreetAddress(useFullAddress: true), "address");
Check(faker.Finance.Iban("SE"), "iban");
Check(faker.Parse("{{person.firstName}} från {{location.city}}"), "template");
Check(faker.Helpers.FromRegExp("[A-Z]{3}-\\d{3}"), "regex");
Check(faker.Internet.Jwt(), "jwt");

foreach (var locale in Locales.All)
{
    if (locale.Code != "base")
        Check(new Faker(locale.Code, seed: 1).Person.FullName(), $"name in {locale.Code}");
}

var people = Fakes.Person.Seed(1).Locale("de").Generate(1_000);
Check(people[0].Email, "generated person");
Check(people[0].Home.City, "nested address");
if (people.Any(p => p.Pets.Count == 0))
    throw new InvalidOperationException("collections were not populated");

var parallel = Fakes.Person.Seed(1).Locale("de").GenerateParallel(1_000);
if (!parallel.Select(p => p.Email).SequenceEqual(people.Select(p => p.Email)))
    throw new InvalidOperationException("parallel generation differs from sequential generation");

Console.WriteLine($"OK: {people[0].FirstName} {people[0].LastName} <{people[0].Email}>, {people[0].Home.City}");
return 0;

static void Check(string? value, string what)
{
    if (string.IsNullOrWhiteSpace(value) || value.Contains("{{", StringComparison.Ordinal))
        throw new InvalidOperationException($"{what} failed: '{value}'");
}

public sealed class Person
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public Address Home { get; init; } = null!;
    public List<Pet> Pets { get; set; } = [];
    public Status Status { get; set; }
}

public sealed record Address(string Street, string City, string ZipCode, string Country);

public sealed record Pet(string PetName, int Age);

public enum Status
{
    Active,
    Suspended,
}

[FakeFor<Person>]
internal static partial class Fakes;
