using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Phoney;
using Phoney.Generation;
using Phoney.NodaTime;

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

// Populating an existing object keeps what is set, also through the init-only members of a record.
var partial = Fakes.Person.Seed(2).Locale("sv").Populate(new Person { FirstName = "Anna", Home = new Address("Storgatan 1", "", "", "") });
if (partial.FirstName != "Anna" || partial.Home.Street != "Storgatan 1")
    throw new InvalidOperationException("populate overwrote existing values");
Check(partial.LastName, "populated last name");
Check(partial.Home.City, "populated nested record");
if (partial.Pets.Count == 0)
    throw new InvalidOperationException("populate did not fill collections");

// The facade without reflection: seeded like Fake.For, populates what is unset.
using (Fake.Seeded(9))
{
    var viaFacade = Fake.Generated<Person>().Populate(new Person { FirstName = "Kim" });
    if (viaFacade.FirstName != "Kim")
        throw new InvalidOperationException("Fake.Generated overwrote existing values");
    Check(viaFacade.LastName, "facade last name");
}

// Interfaces: [JsonDerivedType] defaults, [FakeSubtype] registrations and generator subtypes, no reflection.
var owners = Fakes.Person.Seed(3).Subtype<IContact, Email, Phone>().Generate(50);
if (owners.Any(p => p.Vehicle is not (Car or Bike)) || owners.Any(p => p.Insurance is not Policy) || owners.Any(p => p.Contact is not (Email or Phone)))
    throw new InvalidOperationException("subtypes were not generated");
if (!owners.Any(p => p.Contact is Email) || !owners.Any(p => p.Contact is Phone))
    throw new InvalidOperationException("one of several subtypes was not picked");

// Type-wide generators: every Address, Pet and IContact in the graph comes from the registered generator, no reflection.
var oslo = Fakes.Person.Seed(5)
    .Use(Fake.Generated<Address>().With(a => a.City, "Oslo"))
    .Use<IContact, Email>(Fake.Generated<Email>().With(e => e.Address, "oslo@example.com"))
    .Generate(20);
if (oslo.Any(p => p.Home.City != "Oslo") || oslo.Any(p => p.Contact is not Email { Address: "oslo@example.com" }))
    throw new InvalidOperationException("type generators were not used");

// Invalid and boundary data from data annotations, through a generated model, no reflection.
var invalid = Fakes.Ticket.Seed(6).InvalidCases();
if (invalid.Count != 5 || !invalid.All(c => c.Member switch
    {
        "Seats" => c.Value.Seats is 0 or 9,
        "Code" => c.Value.Code.Length is 1 or 7,
        "Contact" => !c.Value.Contact.Contains('@'),
        _ => false,
    }))
    throw new InvalidOperationException("invalid cases were not invalid: " + string.Join("; ", invalid));
var edges = Fakes.Ticket.Seed(6).BoundaryCases();
if (!edges.Where(c => c.Member == "Seats").Select(c => c.Value.Seats).Order().SequenceEqual([1, 8]) || !edges.Where(c => c.Member == "Code").Select(c => c.Value.Code.Length).Order().SequenceEqual([2, 6]))
    throw new InvalidOperationException("boundary cases were wrong: " + string.Join("; ", edges));

// Rules for members of nested objects (paths through objects and collections), through generated models, no reflection.
var oslo2 = Fakes.Person.Seed(7)
    .With(p => p.Home.City, "Oslo")
    .With(p => p.Pets.Each().PetName, "Rex")
    .Generate(20);
if (oslo2.Any(p => p.Home.City != "Oslo") || oslo2.SelectMany(p => p.Pets).Any(x => x.PetName != "Rex") || !oslo2.SelectMany(p => p.Pets).Any())
    throw new InvalidOperationException("path rules were not applied");

// Conditions and rule verification, through generated models, no reflection.
var threePets = Fakes.Person.Seed(8).Where(p => p.Pets.Count == 3).Generate(20);
if (threePets.Any(p => p.Pets.Count != 3))
    throw new InvalidOperationException("Where did not filter");
Fakes.Person.Seed(9).With(p => p.Home.City, "Oslo").Subtype<IContact, Email>().Use(Fake.Generated<Pet>()).VerifyRules().Generate(10);
try
{
    Fakes.Person.Seed(9).Use(Fake.Generated<Ticket>()).VerifyRules().Generate(10); // nothing in a Person needs a Ticket
    throw new InvalidOperationException("VerifyRules did not report an unused rule");
}
catch (InvalidOperationException e) when (e.Message.Contains("Use<Ticket>", StringComparison.Ordinal))
{
}

if (Fakes.Person.Seed(10).Where(p => p.Pets, pets => pets.Count == 2).Generate(10).Any(p => p.Pets.Count != 2))
    throw new InvalidOperationException("Where on a member was not applied");

var numbered = Fakes.Person.Seed(10).Sequence(p => p.FirstName, i => "P" + i).Cycle(p => p.Status, [Status.Active, Status.Suspended]).Generate(6);
if (!numbered.Select(p => p.FirstName).SequenceEqual(["P0", "P1", "P2", "P3", "P4", "P5"]) || numbered[3].Status != Status.Suspended)
    throw new InvalidOperationException("Sequence or Cycle was not applied");

if (people.Any(p => p.Badges.Count == 0))
    throw new InvalidOperationException("read-only collections were not filled");
if (people.Any(p => p.Aliases.IsDefault || p.Aliases.Length == 0 || p.Visits.Count == 0))
    throw new InvalidOperationException("immutable or frozen collections were not generated");

// Data feeds (CSV and JSON) through generated models, no reflection.
var fed = Fakes.Person.Seed(12).WithFeed(DataFeed.FromCsv("FirstName,Status\nAnn,Suspended\n\"Bo, Jr.\",Active\n")).Generate(4);
var fedFromJson = Fakes.Person.Seed(12).WithFeed(DataFeed.FromJson("[{\"firstName\":\"Cy\",\"status\":\"suspended\"}]")).Generate(2);
if (!fed.Select(p => p.FirstName).SequenceEqual(["Ann", "Bo, Jr.", "Ann", "Bo, Jr."]) || fed[0].Status != Status.Suspended || fed[1].Status != Status.Active
    || fedFromJson.Any(p => p.FirstName != "Cy" || p.Status != Status.Suspended))
    throw new InvalidOperationException("feeds were not applied");

// Back-references through generated models, no reflection.
var folders = Fakes.Folder.Seed(11).WithBackReferences().Generate(5);
if (folders.Any(f => f.Documents.Count == 0 || f.Documents.Any(d => !ReferenceEquals(d.Folder, f))))
    throw new InvalidOperationException("back-references were not set");

// Rules for every member of a type, through generated models, no reflection.
if (Fakes.Person.Seed(10).WithType(Status.Suspended).Generate(10).Any(p => p.Status != Status.Suspended))
    throw new InvalidOperationException("WithType was not applied");

// Time: a TimeProvider drives relative dates.
var clock = new FixedTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
if (Fakes.Person.Seed(1).TimeProvider(clock).Generate().BirthDate.Year < 1950)
    throw new InvalidOperationException("time provider was not used");

// NodaTime: values and convention-driven members, no reflection.
if (faker.Noda().Instant.Past() >= NodaTime.Instant.FromDateTimeOffset(faker.ReferenceDate) || faker.Noda().TimeZone() is null)
    throw new InvalidOperationException("NodaTime values failed");
if (people.Any(p => p.CreatedAt == default || p.CreatedAt > NodaTime.Instant.FromDateTimeOffset(Faker.DefaultSeededReferenceDate)))
    throw new InvalidOperationException("NodaTime members were not generated");

// Dependency injection with a source-generated generator.
using (var services = new ServiceCollection().AddPhoney(o => o.Seed = 1).AddGenerator(Fakes.Person).Services.BuildServiceProvider())
{
    using var scope = services.CreateScope();
    Check(scope.ServiceProvider.GetRequiredService<IFaker>().Person.FullName(), "injected faker");
    if (scope.ServiceProvider.GetRequiredService<Generator<Person>>().Generate().Email != Fakes.Person.Seed(1).Generate().Email)
        throw new InvalidOperationException("injected generator differs");
}

// National ids: valid check digits, no reflection.
var personnummer = faker.NationalId.Person();
if (!NationalIds.IsValid(NationalIdKind.Person, "SE", personnummer))
    throw new InvalidOperationException($"invalid personnummer '{personnummer}'");
Check(faker.NationalId.Vat("GB"), "vat number");

// Closed generic classes, tuples, memory, grids, addresses and big numbers as members of a generated model.
var shipment = ShipmentFakes.Shipment.Seed(5).Generate();
Check(shipment.Reply.Payload.Name, "generic member");
Check(shipment.Reply.Items.Count > 0 ? shipment.Reply.Items[0].Name : null, "generic member items");
Check(shipment.Pair.Item2, "tuple");
Check(shipment.Entry.Key, "key/value pair");
Check(shipment.Address.ToString(), "IP address");
Check(shipment.Version.ToString(), "version");
Check(shipment.Weight.ToString(), "big number");
if (shipment.Buffer.Length == 0 || shipment.Grid.Length == 0 || shipment.Pair.Item1 == 0)
    throw new InvalidOperationException("buffer, grid or tuple not filled");

// A random subset per object, for a list member: no reflection, positions in stack memory.
var roles = new[] { "admin", "editor", "viewer", "billing", "support" };
var crew = ShipmentFakes.Shipment.Seed(12).WithOneOf(s => s.Crew, roles, (2, 3)).Generate(100);
if (crew.Any(s => s.Crew.Count is < 2 or > 3 || s.Crew.Distinct().Count() != s.Crew.Count))
    throw new InvalidOperationException("subset off");

// Weighted choices: the running totals are made once and a pick allocates nothing.
var picked = ShipmentFakes.Shipment.Seed(11).WithOneOf(s => s.Weight, (new System.Numerics.BigInteger(5), 9), (new System.Numerics.BigInteger(7), 1)).Generate(400);
if (picked.Any(s => s.Weight != 5 && s.Weight != 7) || picked.Count(s => s.Weight == 5) < 320)
    throw new InvalidOperationException("weighted choice off");

// Normally distributed numbers: generic math over a BigInteger and a nullable, in a generated model, without reflection.
var heavy = ShipmentFakes.Shipment.Seed(9).WithNormal(s => s.Weight, 1000, 50).Generate(200);
var average = heavy.Average(s => (double)s.Weight);
if (average is < 980 or > 1020)
    throw new InvalidOperationException($"normal distribution off: {average}");
Check(faker.Number.Normal<decimal>(100, 15, min: 50, max: 150).ToString(System.Globalization.CultureInfo.InvariantCulture), "normal decimal");

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
    public IVehicle Vehicle { get; set; } = null!;
    public IInsurance Insurance { get; set; } = null!;
    public IContact Contact { get; set; } = null!;
    public NodaTime.Instant CreatedAt { get; set; }
    public System.Collections.Immutable.ImmutableArray<string> Aliases { get; set; }
    public System.Collections.Frozen.FrozenDictionary<string, int> Visits { get; set; } = null!;
    public List<string> Badges { get; } = [];
}

[JsonDerivedType(typeof(Car))]
[JsonDerivedType(typeof(Bike))]
public interface IVehicle;

public sealed class Car : IVehicle
{
    public string LicensePlate { get; set; } = "";
}

public sealed class Bike : IVehicle
{
    public int Gears { get; set; }
}

public interface IInsurance;

public sealed record Policy(string AccountNumber) : IInsurance;

public interface IContact;

public sealed record Email(string Address) : IContact;

public sealed record Phone(string Number) : IContact;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed record Address(string Street, string City, string ZipCode, string Country);

public sealed record Pet(string PetName, int Age);

public enum Status
{
    Active,
    Suspended,
}

public sealed class Ticket
{
    [System.ComponentModel.DataAnnotations.Range(1, 8)]
    public int Seats { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(6, MinimumLength = 2)]
    public string Code { get; set; } = "";

    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string Contact { get; set; } = "";
}

public sealed class Folder
{
    public string Title { get; set; } = "";
    public List<Document> Documents { get; set; } = [];
}

public sealed class Document
{
    public string Name { get; set; } = "";
    public Folder? Folder { get; set; }
}

public sealed class Crate
{
    public string Name { get; set; } = "";
}

public sealed class Reply<T>
{
    public T Payload { get; set; } = default!;
    public List<T> Items { get; set; } = [];
}

public sealed class Shipment
{
    public Reply<Crate> Reply { get; set; } = null!;
    public (int, string) Pair { get; set; }
    public KeyValuePair<string, int> Entry { get; set; }
    public System.Net.IPAddress Address { get; set; } = null!;
    public Version Version { get; set; } = null!;
    public System.Numerics.BigInteger Weight { get; set; }
    public Memory<byte> Buffer { get; set; }
    public int[,] Grid { get; set; } = null!;
    public List<string> Crew { get; set; } = [];
}

[FakeFor<Shipment>]
internal static partial class ShipmentFakes;

[FakeFor<Folder>, FakeFor<Person>, FakeFor<Email>, FakeFor<Phone>, FakeFor<Ticket>, FakeSubtype<IInsurance, Policy>]
internal static partial class Fakes;
