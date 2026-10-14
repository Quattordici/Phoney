<!-- An absolute URL so the logo also shows on nuget.org, which renders this README but not relative images. -->
# [![Phoney](https://raw.githubusercontent.com/Quattordici/Phoney/main/images/phoney-logo.png)](https://github.com/Quattordici/Phoney)

[![NuGet](https://img.shields.io/nuget/v/Phoney.svg?label=NuGet)](https://www.nuget.org/packages/Phoney)
[![NuGet prerelease](https://img.shields.io/nuget/vpre/Phoney.svg?label=NuGet%20prerelease)](https://www.nuget.org/packages/Phoney)
[![NuGet downloads](https://img.shields.io/nuget/dt/Phoney.svg?label=Downloads)](https://www.nuget.org/packages/Phoney)
[![Phoney.TUnit](https://img.shields.io/nuget/vpre/Phoney.TUnit.svg?label=Phoney.TUnit)](https://www.nuget.org/packages/Phoney.TUnit)
[![Phoney.Xunit](https://img.shields.io/nuget/vpre/Phoney.Xunit.svg?label=Phoney.Xunit)](https://www.nuget.org/packages/Phoney.Xunit)
[![Phoney.NUnit](https://img.shields.io/nuget/vpre/Phoney.NUnit.svg?label=Phoney.NUnit)](https://www.nuget.org/packages/Phoney.NUnit)
[![Phoney.MSTest](https://img.shields.io/nuget/vpre/Phoney.MSTest.svg?label=Phoney.MSTest)](https://www.nuget.org/packages/Phoney.MSTest)
[![Phoney.DependencyInjection](https://img.shields.io/nuget/vpre/Phoney.DependencyInjection.svg?label=Phoney.DependencyInjection)](https://www.nuget.org/packages/Phoney.DependencyInjection)
[![Phoney.EntityFrameworkCore](https://img.shields.io/nuget/vpre/Phoney.EntityFrameworkCore.svg?label=Phoney.EntityFrameworkCore)](https://www.nuget.org/packages/Phoney.EntityFrameworkCore)
[![Phoney.NodaTime](https://img.shields.io/nuget/vpre/Phoney.NodaTime.svg?label=Phoney.NodaTime)](https://www.nuget.org/packages/Phoney.NodaTime)

**Fast, easy and flexible fake data for .NET**, powered by the locale data of [faker.js](https://github.com/faker-js/faker).

- **76 locales** imported from faker.js (`@faker-js/faker` 10.6.0), kept up to date by an automated sync pipeline.
- **One line to a value or a fully populated object**: `Fake.Person.FullName()`, `Fake.Many<Customer>(100)`.
- **Convention-based population** of classes, records and structs: `FirstName`, `Email`, `BillingCity`, `Price`, `CreatedAt`… just work, and names, emails and avatars of one object belong to the same person.
- **Flexible**: fluent, immutable generators, rules that depend on other members, uniqueness, strict mode (also checked at compile time), custom conventions, custom locales, faker.js-style templates.
- **Interfaces and polymorphism**: `.Subtype<IShape, Circle, Square>()`, app-wide `Fake.Subtypes`, and `[JsonDerivedType]` honoured out of the box.
- **Invalid and boundary data**: `.Invalid(x => x.Quantity)`, `.InvalidCases()` and `.BoundaryCases()` break or hit the limits of your data annotations, to test that validation rejects what it should.
- **Replayable random tests**: `[FakeSeed]` for TUnit runs a test on random data and prints the seed when it fails; `PHONEY_SEED=… dotnet test` replays it.
- **Rules for nested members**: `.With(x => x.Billing.City, "Oslo")` and `.With(x => x.Lines.Each().Sku, f => …)` reach into the object graph, per position.
- **Data from files**: `.WithFeed(DataFeed.FromCsvFile("people.csv"))` fills members from the rows of a CSV or JSON file, one row per object.
- **Numbering**: `.Sequence(x => x.Code, i => $"T{i + 1:D4}")` and `.Cycle(x => x.Queue, ["a", "b"])` give members values by the object's position, the same however you generate.
- **Conditions and checked rules**: `.Where(o => o.Total > 100)` keeps only objects that satisfy a condition, and `.VerifyRules()` fails when a nested rule, `Subtype`, `Use` or `WithConvention` never applied to anything.
- **Type-wide rules**: `.Use(Fake.For<Address>().Locale("de"))` generates every nested `Address` in the graph with one generator.
- **Testable time**: relative dates follow a `TimeProvider` such as `FakeTimeProvider`.
- **Fast**: lookups are array indexed and allocation free, templates are parsed once, and a **source generator** makes generation reflection free.
- **Deterministic**: same seed means same data, dates included, on every OS and .NET version and every day you run it; item *n* is the same whether you generate 1 or 1,000,000 items, sequentially or in parallel.
- **Trim and Native AOT safe** with the source generator.
- **Integrations**: `[FakeData]` for TUnit tests, `services.AddPhoney()` for dependency injection and ASP.NET Core, `HasFakeData` for EF Core seeding, and NodaTime types.

| Package | What it adds |
|---|---|
| `Phoney` | fakers, generators, the source generator and the Strict analyzer |
| `Phoney.TUnit` | [`[FakeData]` for TUnit](#test-data-in-tunit) |
| `Phoney.Xunit`, `Phoney.NUnit`, `Phoney.MSTest` | [`[FakeData]` for xUnit.net v3, NUnit and MSTest](#test-data-in-xunit-nunit-and-mstest) |
| `Phoney.DependencyInjection` | [`services.AddPhoney()`, `IFaker`, injectable generators](#dependency-injection-and-aspnet-core) |
| `Phoney.EntityFrameworkCore` | [`HasFakeData` seed data](#ef-core-seeding) |
| `Phoney.NodaTime` | [NodaTime values and members](#nodatime) |

Targets .NET 8 and .NET 10.

```bash
dotnet add package Phoney
```

## Quick start

```csharp
using Phoney;

// Values
Fake.Person.FullName();                         // "Madisen Collins"
Fake.Internet.Email();                          // "Ryann_Beahan@yahoo.com"
Fake.Location.StreetAddress(useFullAddress: true);

// A whole, coherent person
PersonProfile p = Fake.Person.Profile();        // names, email, username, phone, birthdate + age, address, national id

// A specific locale and a seed
var faker = new Faker("sv", seed: 42);
faker.Person.FullName();                        // "Madeleine Öberg Nordström"
faker.Finance.Iban("SE", formatted: true);      // valid check digits

// Objects
Customer customer = Fake.One<Customer>();
List<Customer> customers = Fake.Many<Customer>(1_000);
```

## A whole person

`Person.Profile()` returns a `PersonProfile` whose parts belong together: the names match the sex, email and username are built from the names, the age matches the birthdate, and the national id (when the locale's country has a scheme) encodes birthdate and sex.

```csharp
var faker = new Faker("de", seed: 7);
PersonProfile p = faker.Person.Profile(Sex.Female, minAge: 25, maxAge: 40);
// p.FirstName, p.LastName, p.FullName, p.Email, p.Username, p.Phone, p.BirthDate, p.Age, p.JobTitle, p.NationalId
// p.Address.Street, p.Address.City, p.Address.ZipCode, p.Address.State, p.Address.CountryCode

AddressProfile address = faker.Location.Address(); // just an address, as parts
```

For your own types, [`Fake.For<T>()`](#objects) gives the same coherence: a `Customer`'s `FirstName`, `Email`, `Avatar` and `Gender` describe one person.

## National and tax IDs

Personal identity numbers, company registration numbers and VAT numbers come with **valid check digits** and use the locale's country unless you pass one:

```csharp
var faker = new Faker("sv", seed: 1);
faker.NationalId.Person();                                   // "19560703-6190" (personnummer)
faker.NationalId.Person("NO", birthDate: new DateOnly(1990, 5, 17), sex: Sex.Female, formatted: false);
faker.NationalId.Company();                                  // "214200-3165" (organisationsnummer)
faker.NationalId.Vat("GB");                                  // "GB 154 4706 67"
```

| | Person | Company | VAT |
|---|---|---|---|
| SE | personnummer | organisationsnummer | ✓ |
| NO | fødselsnummer | organisasjonsnummer | ✓ |
| DK | CPR | CVR | ✓ |
| FI | henkilötunnus | Y-tunnus | ✓ |
| US | SSN (format rules, no check digit) | EIN | |
| GB | NINO (format rules, no check digit) | | ✓ |
| BR | CPF | CNPJ | |
| PL | PESEL | REGON | ✓ (NIP) |
| NL | BSN | | |
| ES | DNI | | |
| FR | NIR | SIREN | ✓ |
| CA | SIN | | |

Where the number encodes a birth date and sex (SE, NO, DK, FI, PL, FR) it encodes the ones you pass, so a `PersonProfile` or a generated object has an id that agrees with its `BirthDate` and `Sex`. Members named `Personnummer`, `Ssn`, `NationalId`, `Cpf`, `Pesel`… (person), `OrgNr`, `Cnpj`, `Ein`… (company) and `VatNumber` get these values by convention. A locale whose country has no scheme leaves them empty; `NationalId` throws a `PhoneyDataUnavailableException`, so pass the country there.

Every scheme can also validate, and you can register your own:

```csharp
NationalIds.IsValid(NationalIdKind.Person, "SE", "19560703-6190");   // true
NationalIds.Supported(NationalIdKind.Vat);                           // ["DK", "FI", "FR", "GB", "NO", "PL", "SE"]
NationalIds.Register(new MyEmployeeNumbers());                       // derive from NationalIdScheme
```

The numbers are structurally valid, not issued: they can coincide with real people's or companies' numbers, so use them for test data only. Danish CPR numbers satisfy the classic mod-11 rule that new numbers no longer follow, so old and new validators accept them; the validator checks the date and century digit only.

## Values

A `Faker` groups values by topic:

| Module | Examples |
|---|---|
| `Person` | `Profile()`, `FirstName(Sex.Female)`, `LastName()`, `FullName()`, `Sex()`, `JobTitle()`, `Bio()`, `Prefix()` |
| `Location` | `Address()`, `StreetAddress()`, `City()`, `ZipCode()`, `State()`, `Country()`, `CountryCode()`, `Latitude()`, `NearbyGpsCoordinate()` |
| `Internet` | `Email()`, `Username()`, `Url()`, `DomainName()`, `Ipv4(IPv4Network.PrivateC)`, `Ipv6()`, `Mac()`, `Password()`, `UserAgent()`, `Jwt()`, `Emoji()` |
| `Phone` | `Number(PhoneStyle.International)`, `Imei()` |
| `Company` / `Commerce` | `Name()`, `CatchPhrase()`, `ProductName()`, `Price()`, `Isbn()`, `Upc()` |
| `Finance` | `Iban()`, `Bic()`, `CreditCardNumber(CardIssuer.Visa)`, `Currency()`, `Amount()`, `RoutingNumber()`, `BitcoinAddress()` |
| `NationalId` | `Person()`, `Company()`, `Vat()`, e.g. a Swedish personnummer, a US SSN, a Brazilian CPF, valid check digits |
| `Date` | `Past()`, `Future()`, `Recent()`, `Soon()`, `Between(...)`, `Birthdate(18, 65)`, `Month()`, `Weekday()` |
| `Lorem` / `Word` | `Sentence()`, `Paragraphs()`, `Slug()`, `Noun(minLength: 5)` |
| `String` / `Number` | `Uuid()`, `UuidV7()`, `Ulid()`, `NanoId()`, `AlphaNumeric(10)`, `Int(1, 6)`, `Decimal(0, 100, 2)` |
| `Color` | `Human()`, `Hex()`, `Rgb()` (typed, with `ToCss()`/`ToHex()`), `Hsl()`, `Lab()` |
| `Image` | `Url(640, 480)`, `Avatar(Sex.Female)`, `DataUri()` (self-contained SVG) |
| `Helpers` | `FromRegExp("[A-Z]{3}-\\d{4}")`, `Slugify()`, `LuhnCheck()`, `Multiple(f => …, 5)`, `UniqueValues(…, 10)` |
| also | `Animal`, `Book`, `Music`, `Food`, `Hacker`, `Vehicle` (with valid VINs), `Airline`, `Science`, `Database`, `System`, `Git` |

Checksums are real: credit cards and IMEIs pass Luhn, IBANs pass MOD 97, ISBN, UPC, VIN and ABA routing numbers have valid check digits.

`Fake.*` is a thread-safe static facade over a per-thread `Faker`.

`faker.Random` is the seeded source of randomness (xoshiro256\*\*) for your own values: `Int`, `Long`, `Double`, `Decimal`, `Bool(0.2)`, `Element(list)`, `Elements(list, 3)`, `Weighted(("gold", 1), ("silver", 3))`, `Enum<T>()`, `Guid()`, `Replace("##??")`.

Image URLs come from replaceable templates, so generated data doesn't have to point at public placeholder services:

```csharp
ImageSources.UseHost("https://images.test");                 // …/photos/{seed}/{width}x{height}, …/avatars/{sex}/{size}/{index}.jpg
ImageSources.Photo = "https://cdn.example.com/{width}x{height}?s={seed}";
```

### Reproducible data

A seed fixes values *and* dates: seeded fakers and generators compute relative dates (past, recent, birthdates, ages) from `Faker.DefaultSeededReferenceDate` (2025-01-01 UTC) unless you set `ReferenceDate`, so seeded output doesn't change from one day to the next.

```csharp
var faker = new Faker("de", seed: 2024);                         // same values and dates on every run
var later = new Faker("de", seed: 2024) { ReferenceDate = DateTimeOffset.UtcNow }; // opt back into "now"

[Fact]
public void Creates_an_invoice()
{
    using var _ = Fake.Seeded(42);           // Fake.*, Fake.One/Many/For are reproducible in this test only
    var customer = Fake.One<Customer>();     // the same customer on every run, even with tests in parallel
}

Fake.Seed(42);                                // or seed the facade app-wide (every thread starts the same sequence)
```

`Generator<T>.Seed(n)` does the same for a generator. For tests that should run on random data but be replayable, see [`[FakeSeed]`](#test-data-in-tunit) and `FakeSeed.Pick()`. One exception: `Unique(...)` members combined with `GenerateParallel` depend on thread timing, so use sequential generation when such results must be stable.

### Controlling "now" with `TimeProvider`

Relative dates can follow a .NET `TimeProvider` instead of the system clock, so they move with the clock your tests already control (for example `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`):

```csharp
var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

var faker = new Faker("sv") { TimeProvider = time };            // faker.Date.Past() is before 2030-01-01
var orders = Fake.For<Order>().TimeProvider(time).Generate(10);   // CreatedAt, ages, timelines follow the clock
var rows = Fake.Arguments(method).TimeProvider(time).Generate(5); // argument rows too
Fake.TimeProvider = time;                                         // the static facade, Fake.For/One/Many and Fake.Arguments
time.Advance(TimeSpan.FromDays(30));                              // read on every use, so dates move along
```

The reference date is, in order: an explicit `ReferenceDate`, the `TimeProvider`, `DefaultSeededReferenceDate` for seeded fakers and generators, and otherwise the system clock.

## Templates

Templates use the same names as the API:

```csharp
faker.Parse("{{Person.FirstName}} ordered {{Commerce.ProductName}} from {{Company.Name}}");
faker.Parse("Ticket {{Helpers.FromRegExp([A-Z]{3}-[0-9]{4})}}, PIN {{String.Numeric(4)}}");
faker.Parse("{{Person.FirstName}} lives at {{Location.Address}}");
```

Names are case-insensitive, so faker.js templates (`{{person.firstName}}`, `{{number.int({"min":1,"max":9})}}`) work unchanged, and so do locale data paths and record fields (`{{airline.airport.iataCode}}`). Register your own functions with `TemplateFunctions.Register("Shop.Sku", (f, _) => f.Random.Replace("SKU-#####"))`.

## Objects

```csharp
var customers = Fake.For<Customer>()
    .Locale("sv")
    .Seed(42)                                                     // reproducible
    .With(x => x.LoyaltyPoints, f => f.Random.Int(0, 5_000))      // value from a factory
    .With(x => x.Status, CustomerStatus.Active)                    // fixed value
    .With(x => x.Email, (f, x) => f.Internet.Email(x.FirstName, x.LastName, provider: "example.se")) // depends on the object
    .With(x => x.Address, Fake.For<Address>().Locale("de"))        // nested generator
    .Unique(x => x.Email)
    .Ignore(x => x.InternalNotes)
    .Generate(100);                                                // also: Generate(), Stream(), GenerateParallel(n)
```

- **Supported shapes**: classes with a parameterless constructor, records and classes with constructor parameters (the constructor with most parameters is used), `init` and `required` members, public fields, structs, nested objects, interfaces and abstract classes (through [subtypes](#interfaces-and-subtypes)), `T[]`, `List<T>` and its interfaces, `HashSet<T>`, `Dictionary<K,V>` and [more collection types](#collection-types), enums, nullable types, `byte[]`, and `Guid`, `DateTime(Offset)`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri` plus every numeric type.
- **Cycles** (`Customer → Order → Customer`) stop with `null`, or point back at the containing object with [`WithBackReferences()`](#back-references). `MaxDepth(n)` (default 4) bounds nesting, `CollectionSize(min, max)` (default 1–3) sizes collections, `NullProbability(p)` (default 0) leaves nullable members null.
- **Generators are immutable**: every method returns a new generator, so you can share them and derive variants: `var admins = users.With(u => u.Role, "Admin");`.
- **Order**: constructor arguments, then members in declaration order, then rules that depend on the object (in the order added), then `AfterCreate` actions. `CreateWith(f => new Customer(...))` replaces construction.
- **Strict mode**: `.Strict()` throws when a scalar member has neither a rule, a convention nor a data annotation that defines it, so new members can't silently get meaningless data. The included analyzer reports the same at compile time (**PHONEY004**) for chains like `Fake.For<T>().Strict()…Generate()`, so a new member breaks the build instead of the test run. It can't see app-wide `Fake.Conventions` (registered at runtime): cover such members with `.Ignore(...)`/`.With(...)` or suppress the warning.

### Collection types

Besides arrays, `List<T>`, `HashSet<T>`, `Dictionary<K,V>` and their interfaces, members of these types are generated and filled:

| Built like a list | Built like a set | Built like a dictionary |
|---|---|---|
| `ImmutableArray<T>`, `ImmutableList<T>`, `IImmutableList<T>` | `ImmutableHashSet<T>`, `IImmutableSet<T>`, `ImmutableSortedSet<T>` | `ImmutableDictionary<K,V>`, `IImmutableDictionary<K,V>`, `ImmutableSortedDictionary<K,V>` |
| `ReadOnlyCollection<T>`, `Collection<T>`, `ObservableCollection<T>` | `SortedSet<T>` | `SortedDictionary<K,V>`, `SortedList<K,V>` |
| `Queue<T>`, `Stack<T>`, `LinkedList<T>` | `FrozenSet<T>` | `FrozenDictionary<K,V>` |
| `ConcurrentBag<T>`, `ConcurrentQueue<T>`, `ConcurrentStack<T>` | | `ConcurrentDictionary<K,V>` |

Each is built exactly like its `List<T>`, `HashSet<T>` or `Dictionary<K,V>` counterpart (so a seed gives the same elements whichever type holds them) and converted. Sizes, `[MinLength]`/`[MaxLength]`, `CollectionSize`, `Invalid`/`Boundary`, `Populate` (a default `ImmutableArray<T>` counts as unset), `Each()` paths and the source generator all work the same.

**Read-only collections are filled in place.** A collection with a getter only (`public List<Line> Lines { get; } = [];`), a private setter or a `readonly` field gets its generated elements added to the collection the object already holds:

```csharp
public class Order { public List<Line> Lines { get; } = []; public ISet<string> Tags { get; } = new HashSet<string>(); }
Fake.One<Order>().Lines.Count;   // 1–3 generated lines
```

- Works for `List<T>`, `IList<T>`, `ICollection<T>`, `HashSet<T>`, `ISet<T>`, `Collection<T>`, `ObservableCollection<T>`, `SortedSet<T>`, `LinkedList<T>`, `Dictionary<K,V>`, `IDictionary<K,V>`, `SortedDictionary<K,V>`, `SortedList<K,V>` and `ConcurrentDictionary<K,V>`. Collections that can't be added to (`IEnumerable<T>`, `IReadOnlyList<T>`, immutable, frozen, queues, stacks, bags), a property that holds `null`, and a collection that is read-only at runtime are left alone.
- Rules work as for any member: `Ignore`, `CollectionSize`, `[MinLength]`, `x.Lines.Each().Sku` paths, `Invalid`/`Boundary`, `With(x => x.Lines, list)` (the elements are **added to** the existing collection; a rule that depends on the object, `(f, x) => …`, is rejected because there is nothing to set). Back-references reach into them.
- They are drawn **after all other members**, so the data of every existing type for a seed is unchanged. `Populate` fills the ones that are empty and keeps the others; `PopulateMode.All` replaces their elements.
- The EF Core package leaves navigations (and other collections of objects) of this shape empty, because seed data can't hold them. Works the same with reflection and with the source generator.

For code that works from `Type` values, `Fake.One(type)` and `Fake.Many(type, count)` create objects of a type known only at runtime (reflection, like `Fake.One<T>()`).

### Data from CSV and JSON

`WithFeed` fills members from the rows of a CSV or JSON file, for data you want to control (a product catalogue, a list of real-looking test accounts) while the rest is generated:

```csharp
var feed = DataFeed.FromCsvFile("customers.csv");        // FirstName,LastName,Country\nAda,Lovelace,GB\n…  (also FromCsv(text), FromJson(text), FromJsonFile(path))
var customers = Fake.For<Customer>().WithFeed(feed).Generate(100);      // row 1, 2, 3… and round again; the other members are generated as usual
var sample = Fake.For<Customer>().Seed(7).WithFeed(feed, FeedOrder.Random).Generate(100); // a reproducible random row per customer
```

- **One row per object**, so values that belong together stay together. The row depends on the object's position (or, for `FeedOrder.Random`, the seed and the position), so item *n* is the same however you generate: batches, `GenerateParallel`, `Stream()`.
- **Columns fill members by name**, ignoring case, spaces and underscores (`first_name` fills `FirstName`), also constructor parameters of records. Values are converted with the invariant culture: strings, numbers, booleans (`true`/`1`/`yes`), enums by name, dates and times, `Guid`, `Uri`; an empty value of a nullable member is `null`.
- **Strict about mistakes.** A column that no member matches, a value that doesn't fit its member's type (the message names the row, the column, the value and the type), ragged CSV rows, duplicate columns and unterminated quotes throw when the feed is read or the generator is first used, not in the middle of a run. `ignoreUnknownColumns: true` skips unmatched columns on purpose.
- **Precedence**: a rule for the member (`With`, `Ignore`…) wins over the feed; the feed wins over name conventions and `WithType`. Members without a column are generated independently of the row (a `Email` convention doesn't know the `FirstName` from the file), so add the columns that must agree.
- CSV follows RFC 4180 (quoted values with delimiters, `""` quotes and line breaks, CRLF, a byte order mark); pass `delimiter: ';'` for other separators. JSON must be an array of flat objects with the same properties.
- A generator with a feed can't be given to `With`, `Use` or `Fake.Generators.Add`: every nested object would take the first row. `Populate` fills the members that are unset. Works the same with reflection and with the source generator.

### Back-references

With navigation properties in both directions (`Order.Lines`, `OrderLine.Order`) a cycle stops with `null` by default. `WithBackReferences()` closes it, like an ORM would:

```csharp
var orders = Fake.For<Order>().WithBackReferences().Generate(10);
orders[0].Lines[0].Order;   // the very same order (ReferenceEquals), not null
```

- After an object is created, every member that is still `null` and whose type one of the objects above it satisfies gets the **nearest** such object: `Line.Order` is the order that holds the line, and a `Line.Customer` is the customer further up. Collections and dictionary values are walked too.
- Members that have a value are kept (a rule for the member wins), nothing is drawn, so **the rest of the data is exactly the data of the same seed without it**, and a `Where` condition or `AfterCreate` action sees the finished graph. `Populate` fills the missing references of an existing graph.
- It works through settable members (setters, `init`, fields). Constructor-only members of positional records can be neither read nor set and are left alone.
- The graph has real cycles afterwards: `ToString`, `Equals` and `GetHashCode` of records and serializers without cycle handling (`ReferenceHandler.Preserve` for System.Text.Json) may not cope. A nested generator (`Use`, `With`) wires the graph it creates.
- Works the same with reflection and with the source generator.

### Rules for nested members

A rule can name a member of a nested object with a path, so you don't need a generator for the whole object just to change one value:

```csharp
var invoices = Fake.For<Invoice>()
    .With(i => i.Billing.City, "Oslo")                          // this address only: Shipping is another position
    .With(i => i.Billing.Contact.Phone, f => f.Random.Replace("+47 ## ## ## ##"))
    .With(i => i.Lines.Each().Sku, f => f.Commerce.Ean13())     // every element of a list, array or set
    .With(i => i.ByCode.Values.Each().Quantity, 1)              // every value of a dictionary
    .Ignore(i => i.Lines.Each().Supplier!.Email)
    .Invalid(i => i.Lines.Each().Quantity)                      // see "Invalid and boundary data"
    .Generate(10);
```

- **Which rules**: `With`, `WithOneOf`, `Ignore`, `Invalid` and `Boundary` accept paths. `Unique` and rules that depend on the object (`With(x => …, (f, x) => …)`) don't, since they belong to the generated object: you get an `ArgumentException`, and the analyzer reports it (**PHONEY007**).
- **A position, not a type**: `Billing.City` and `Shipping.City` are both addresses, and only the first is changed. For every object of a type use [`Use`](#type-wide-generators).
- **`Each()`** is only a marker the compiler needs (`FakePath.Each`): it says "every element" inside the expression and can't be called. `x.Lines[0]` is rejected for the same reason.
- **Checked when the generator is first used**: a member that doesn't exist or can't be set, a string or number in the middle of a path, or `Invalid` on a member without annotations throws an `InvalidOperationException` that names the path. Below an interface or abstract class the picked subtype decides, so that part is checked when the object is created.
- **Precedence**: a rule for the member itself (`With(i => i.Billing, generator)`), `Use` and `Fake.Generators` create the whole object with their own rules, so path rules inside it don't apply. `Populate` creates missing nested objects with the path rules and fills existing ones as before.
- Works the same with reflection and with the source generator.

### Interfaces and subtypes

Members declared as an interface, an abstract class or a base class are generated as one of their subtypes:

```csharp
var drawings = Fake.For<Drawing>()
    .Subtype<IShape, Circle>()               // every IShape in the object graph is a Circle
    .Subtype<IAnimal, Cat, Dog>()            // one of Cat or Dog, picked at random (reproducible with a seed)
    .With(d => d.Logo, Fake.For<Square>())   // one member with its own generator of a subtype
    .Generate(10);

Fake.Subtypes.Add<IShape, Circle, Square>(); // app-wide, for generators created afterwards
Fake.Subtypes.Remove<IShape>();              // or Clear() for all
```

Up to four subtypes fit the type arguments; pass more as types: `.Subtype<IShape>(typeof(Circle), typeof(Square), …)`. A subtype must be concrete and assignable to the base type, otherwise you get an `ArgumentException` saying which.

- **`[JsonDerivedType]` works out of the box**: an interface or abstract class that lists its derived types for System.Text.Json uses them as its default subtypes.
- **Precedence**: the generator's `Subtype`, then `Fake.Subtypes`, then `[JsonDerivedType]`. An interface or abstract class with none of them stays `null`. A concrete base class is generated as itself unless you map it (`.Subtype<Vehicle, Truck>()`).
- `Populate` fills an existing object as its runtime type: a `Circle` in an `IShape` member keeps being a circle and gets its missing members.
- With the source generator, `[FakeSubtype<IShape, Circle>]` on the `[FakeFor]` class registers the subtype app-wide and generates its model; `[JsonDerivedType]` types get models automatically. In Native AOT apps every subtype needs a generated model (`[FakeFor<T>]`, `[FakeSubtype<,>]` or `[JsonDerivedType]`).

### Type-wide generators

`With(x => x.Address, generator)` configures one member. `Use` configures a **type**: every nested object of that type in the graph, whether it sits in a member, a list, an array or a dictionary, and at any depth, comes from the generator you give it:

```csharp
var orders = Fake.For<Order>()
    .Use(Fake.For<Address>().Locale("de").With(a => a.Country, "Germany"))   // Order.Billing, Order.Shipping, Customer.Home…
    .Use<IPayment, Card>(Fake.For<Card>().With(c => c.Network, "Visa"))       // an interface generated by the generator of a subtype
    .Generate(100);
```

- **Precedence**: a rule for the member (`With(o => o.Billing, …)`, `Ignore`) wins over the type's generator, which wins over `Subtype`, `Fake.Subtypes`, `[JsonDerivedType]` and the type's model. A `Subtype` that picked `Card` for an `IPayment` member still uses the generator registered for `Card`.
- **App-wide**: `Fake.Generators.Add(Fake.For<Address>().Locale("sv"))` registers a generator for every generator compiled afterwards (like `Fake.Subtypes` and `Fake.Conventions`: do it at startup or in a test setup hook), also for `[FakeData]` parameters. A generator's own `Use` for the same type wins; `Remove<T>()` and `Clear()` undo it. The registered generator is shared, so its `Unique` members are unique across everything it creates.
- **Self-contained**: the generator brings its own rules, locale, conventions and `Unique` sets (uniqueness holds across every object it creates). It inherits the reference date and, unless it sets its own, the locale.
- **Whole graph, innermost wins**: a generator registered on an inner `Use` replaces the outer one for what it creates; types it doesn't register still come from the outer generators. A later `Use` for the same type replaces an earlier one.
- **Not the root**: the object you generate is always made by the generator you call `Generate` on. Cycles and `MaxDepth` still stop nesting with `null`.
- **`Populate`** creates the nested objects that are missing with the type's generator; nested objects you already have are filled member by member, as before.
- Only objects can be registered. Scalars (strings, numbers, dates, enums) come from rules and conventions, so `Use` throws an `ArgumentException` that points to `WithConvention`.
- Works the same with reflection and with the source generator: `Fake.Generated<Address>()` gives the AOT-safe generator to register.

### Conditions with `Where`

`Where` keeps only the objects that satisfy a condition: an object that does not is thrown away and generated again.

```csharp
var bigOrders = Fake.For<Order>().Seed(1)
    .Where(o => o.Total > 100)
    .Where(o => o.Status != OrderStatus.Cancelled)    // several conditions must all hold
    .Generate(50);
```

- **Same seed, same data.** An object that already satisfies the condition is exactly the one the generator gives without it, so adding a condition only changes the objects it rejects. Retries draw from a seed derived from the seed, the position and the attempt, so item *n* is still the same whether you generate 1 or 1,000,000 items, sequentially, in parallel or as a `Stream()`.
- **It sees the finished object**: dependent rules and `AfterCreate` actions have run, so the condition can look at anything.
- **For conditions most objects meet.** A condition that is not met within `maxAttempts` (default 1000, `Where(…, maxAttempts: 50)`; with several conditions the smallest limit applies) throws an `InvalidOperationException` that says what to do: set the members it looks at with `With(...)` instead of waiting for luck. Creation runs for every attempt, so `AfterCreate` actions may run more than once per result, and a rejected object has used up its `Unique` values.
- **Nested too**: a generator with a condition filters what it creates as a nested generator (`With(x => x.Ticket, generator)`, `Use(generator)`, `Fake.Generators`).
- **`Populate`** supports a condition with `PopulateMode.All` (the object is filled in again until it passes). `MissingOnly` keeps the values it finds, which cannot be generated again, so it throws before touching the object. `InvalidCases()` and `BoundaryCases()` ignore conditions: their objects are made to sit outside or on the edge of the valid ones.
- Works the same with reflection and with the source generator. A constant `maxAttempts` below 1 is reported by the analyzer (**PHONEY011**).

**One member instead of the whole object.** `Where(x => x.Member, condition)` draws just that member again, which is cheaper and leaves the rest of the object alone:

```csharp
var orders = Fake.For<Order>().Seed(1)
    .Where(o => o.Total, total => total > 100)          // only Total is drawn again
    .Unique(o => o.Reference).Where(o => o.Reference, r => !r.StartsWith("TEST"))
    .Generate(50);
```

- It filters what the member's rule or convention produces, whether `With` or `Where` comes first; several conditions on a member must all hold, and with `Unique` a value must be accepted and new. A member that passes on the first try keeps the value it would have without the condition.
- **Members of the generated object only** (`x => x.Billing.City` is rejected, and reported by the analyzer, **PHONEY007**). A member that is ignored, set from the object (`With(x => x.Email, (f, x) => …)`) or created by `CreateWith` has nothing to draw again and throws when the generator is first used.
- **Identity members have one value.** `FirstName`, `LastName`, `Email`, `Username`, address parts, `BirthDate`, `Age`, `NationalId` and sex/gender enums come from the object's identity (so that they agree) and cannot be drawn again: you get an exception that says so. Give the member a value with `With(...)` and filter that, or use `Where` on the object, which generates the whole person again.
- `Populate` filters the values it generates and keeps the ones it finds; `InvalidCases()` and `BoundaryCases()` ignore conditions.

### Checking that rules apply

A rule that names a position in the object graph can silently do nothing: `.With(x => x.Lines.Each().Sku, "A")` when the lists happen to be empty, `.With(x => x.Billing.City, "Oslo")` after `.With(x => x.Billing, generator)` replaced the whole address, or a `Subtype` for an interface nothing uses. `VerifyRules()` turns that into a failure:

```csharp
var invoices = Fake.For<Invoice>().Seed(1)
    .With(i => i.Billing.City, "Oslo")
    .With(i => i.Lines.Each().Sku, f => f.Commerce.Ean13())
    .Subtype<IPayment, Card>()
    .WithConvention<string>(n => n.EndsWith("Code"), f => f.Random.Replace("??-###"))
    .VerifyRules()
    .Generate(20);      // InvalidOperationException listing every rule that did not apply to any of the 20 invoices
```

- **What is checked**: path rules (`With`, `WithOneOf`, `Ignore`, `Invalid`, `Boundary` on a nested member), `Subtype`, `Use` and `WithConvention`: the rules whose reach depends on what the graph contains. Rules for the generated object's own members always apply and are not checked.
- **Used once is used**: a rule counts as soon as it applied to any object this generator produced, so a rule for a nullable member or a sometimes empty list passes when one object in the call had it. Usage adds up over the calls of one generator instance.
- **Checked when generation ends**: after `Generate()`, `Generate(n)`, `GenerateParallel(n)` and `Populate(...)` (not when nothing was generated). `Stream()` never ends and throws a `NotSupportedException` instead of silently skipping the check. `InvalidCases()` and `BoundaryCases()` are not verified.
- **Typical causes**: the object never exists (always null, an empty collection, `MaxDepth`, a cycle, another subtype), a rule for the enclosing member or a `Use` generator creates or skips it, `Populate` keeps an existing object (path rules only shape objects that are created), or the graph has no such member or type.
- **A nested generator can't verify**: it has no `Generate` call of its own, so passing a verifying generator to `With`, `Use` or `Fake.Generators.Add` throws an `ArgumentException`; call `VerifyRules(false)` on it and verify the generator you call `Generate` on.
- Works the same with reflection and with the source generator. Turn it off again with `VerifyRules(false)`. The analyzers' rules about discarded results and replaced rules (**PHONEY005**, **PHONEY008**) catch the mistakes that are visible in the code; this catches the ones that depend on the data.

### Numbering and cycling

`Sequence` gives a member a value that depends on the object's position in the generator's sequence, and `Cycle` repeats a list of values in turn:

```csharp
var tickets = Fake.For<Ticket>().Seed(1)
    .Sequence(t => t.Code, i => $"T{i + 1:D4}")                       // T0001, T0002, T0003…
    .Cycle(t => t.Queue, ["north", "south", "east"])                  // north, south, east, north…
    .Generate(100);
```

- **Position, not luck.** The first object a generator creates is 0 and the numbers continue over calls (`Generate(10)` then `Generate(10)` gives 0–9, then 10–19). Item *n* gets the value for *n* whatever the seed, the batch size, `GenerateParallel` or `Stream()`. An object that `Where` generates again keeps its position, so the numbers stay contiguous.
- **Members of the generated object only.** A rule for a member of a nested object (`x.Billing.Code`) is rejected, and so is giving a generator with `Sequence`/`Cycle` rules to `With`, `Use` or `Fake.Generators.Add`: it counts the objects it generates itself, and every nested object would be number 0. The analyzer reports the first (**PHONEY007**).
- A later rule for the member replaces it, and the other way round (**PHONEY008** reports that in one chain). `Populate` numbers the objects it fills in the same way and keeps members that are set. For the object's own `Id` the [convention](#conventions) already counts 1, 2, 3…

### Filling in existing objects

`Populate` fills in an object you already have, with the same conventions, rules and annotations as `Generate`:

```csharp
var order = Fake.Populate(new Order { CustomerId = 42 });         // CustomerId stays 42, everything else is filled
Fake.For<Customer>().Seed(1).With(x => x.Status, "VIP").Populate(existing);
generator.Populate(existing, PopulateMode.All);                   // regenerate every member
List<Customer> filled = generator.Populate(customers);            // many at once, each from its own place in the seeded sequence
```

- **`PopulateMode.MissingOnly`** (the default) fills members that hold their default (`null`, `0`, `false`, `Guid.Empty`, an empty string or collection) and keeps the others. A set nested object is populated member by member. `PopulateMode.All` replaces everything that is not ignored.
- Kept values shape the generated ones: with a set `FirstName`, `LastName`, `BirthDate` or sex/gender enum, the email, username, age and avatar are generated to match.
- Members you `Ignore` are never touched; rules that depend on the object and `AfterCreate` run afterwards; `CreateWith` is ignored because nothing is created. Records are filled through their `init` properties, and a struct is populated as a copy (use the return value).
- A `false` or `0` that you set on purpose counts as unset in `MissingOnly`: protect it with `.Ignore(x => x.IsActive)`.
- With a seed, `Populate(new T(), PopulateMode.All)` gives exactly what `Generate()` gives.

### Values that agree

Within one object, related members describe the same thing:

- **One person**: `FirstName`, `LastName`, `Email`, `Username`, `Avatar` and `Gender`/`Sex` enums belong together, and `Age` matches `BirthDate`/`DateOfBirth`.
- **One address**: `Street`, `City`, `ZipCode`, `State`, `Country` and `CountryCode` come from one address. For country locales (`de_AT`, `en_US`…) the country is the locale's own (`Österreich`, `AT`).
- **One timeline**: the first "created" date (`CreatedAt`, `RegisteredAt`, `StartDate`…) is when the object was created, "updated" dates (`UpdatedAt`, `LastLogin`…) fall between it and the reference date, and future dates (`ExpiresAt`, `DueDate`, `EndDate`) come after both.

Across objects, `WithOneOf` links graphs to data you generated already:

```csharp
var customers = Fake.Many<Customer>(10);
var orders = Fake.For<Order>()
    .WithOneOf(o => o.CustomerId, customers, c => c.Id)   // an existing customer's id
    .WithOneOf(o => o.Status, [OrderStatus.Paid, OrderStatus.Shipped])
    .Generate(100);
```

### Validation attributes

Generated objects satisfy their data annotations, so they pass `Validator.TryValidateObject`, EF Core and ASP.NET Core validation:

| Annotation | Effect |
|---|---|
| `[StringLength]`, `[MinLength]`, `[MaxLength]`, `[Length]` | string length; element count for collections |
| `[Range]` | numbers and dates within the range (`[Range(typeof(DateTime), "2020-01-01", "2020-12-31")]`) |
| `[RegularExpression]` | a value matching the pattern |
| `[EmailAddress]`, `[Phone]`, `[Url]`, `[CreditCard]`, `[Base64String]` | a value of that format, whatever the member is called |
| `[AllowedValues]`, `[DeniedValues]` | one of / none of the listed values |
| `[Required]` | never `null` (even with `NullProbability`) and never empty |

Annotations work the same with reflection and with the source generator, on properties, fields and record parameters (`[property: Range(1, 5)]`).

### Invalid and boundary data

The annotations that make generated objects valid also tell Phoney how to break them, so you can check that validation rejects what it should and accepts what sits right on the edge:

```csharp
// One object with one broken member, everything else valid (0 or 11 for [Range(1, 10)]).
var order = Fake.For<Order>().Invalid(o => o.Quantity).Generate();
var edge = Fake.For<Order>().Boundary(o => o.Quantity).Generate();            // 1 or 10

// Every violation of every annotated member, one object each.
foreach (var c in Fake.For<Order>().Seed(1).InvalidCases())
    validator.Validate(c.Value).IsValid.ShouldBeFalse(c.ToString());          // "Quantity: above the maximum (10)"

// Every value exactly on a limit; all of them are valid.
foreach (var c in Fake.For<Order>().Seed(1).BoundaryCases())
    validator.Validate(c.Value).IsValid.ShouldBeTrue(c.ToString());           // "Name: the maximum length (50)"
```

| Annotation | `Invalid` gives | `Boundary` gives |
|---|---|---|
| `[Required]` | `null`, and `""` for strings | strings: at least one character |
| `[StringLength]`, `[MinLength]`, `[MaxLength]`, `[Length]` on strings | one character too short / too long | exactly the minimum / maximum length |
| the same on collections and arrays | one element too few / too many | exactly the minimum / maximum count |
| `[Range]` on numbers and dates | one unit (one day for dates) below the minimum / above the maximum | the minimum / the maximum |
| `[RegularExpression]`, `[EmailAddress]`, `[Phone]`, `[Url]`, `[CreditCard]`, `[Base64String]` | text the real validator rejects | none: they define a value, not an edge |
| `[AllowedValues]`, `[DeniedValues]` | a value that is not allowed / a denied one | none |

- **As test data**: every case can be a test case of its own, named after what it is (`Validation_rejects(Quantity: above the maximum (10))`). With TUnit:

  ```csharp
  public static IEnumerable<Func<ConstraintCase<Order>>> Violations() =>
      Fake.For<Order>().Seed(1).InvalidCases().Select(c => (Func<ConstraintCase<Order>>)(() => c));

  [Test, MethodDataSource(nameof(Violations))]
  public void Validation_rejects(ConstraintCase<Order> violation) =>
      Validate(violation.Value).IsValid.ShouldBeFalse(violation.ToString());
  ```
- **One change per case.** A case is the object `Generate()` gives for the seed, with only that member replaced, so it differs from a valid object in the member it is about (a collection that gets a different size also changes the draws after it). `InvalidCases()` and `BoundaryCases()` use one base object for all cases (the `Seed`, or a random one).
- **Which violation?** `Invalid(member)` picks one violation per object, reproducibly with a seed, so `Generate(50)` covers them all. A value can break more than one limit of its member (an empty string is also too short); other members stay valid.
- **Never silent.** A member without annotations to break throws an `InvalidOperationException` that lists the supported ones. Rules for the same member are replaced (`Invalid` and `Boundary` take the member's rule slot), ignored members are skipped, and limits beyond 100,000 characters or elements are not built.
- Works the same with reflection and with the source generator, and with `CreateWith`. `PopulateMode.MissingOnly` only replaces members that are unset.

### Conventions

Member names (case and `_` ignored, trailing words matched, so `BillingCity` is a city and `HomeEmailAddress` an email) pick the generator, for example:

| Member | Value |
|---|---|
| `FirstName`, `LastName`, `FullName`, `Name` on a person-like type | names of one generated identity |
| `Email`, `Username`, `Avatar`, `Gender`/`Sex` enums | consistent with that identity |
| `Street`, `Address1`, `City`, `Zip`/`PostalCode`, `State`, `Country`, `CountryCode` | one address in the locale |
| `Phone`, `Mobile`, `Company`, `JobTitle`, `Department`, `ProductName`, `Description`, `Notes` | matching module |
| `Url`, `Website`, `Ip`, `Mac`, `UserAgent`, `Iban`, `Bic`, `CreditCard`, `Isbn`, `Vin`, `Sku`, `Token`, `Version` | matching module |
| `Id` (`int`/`long`) | 1, 2, 3… in generation order; `CustomerId` is a random reference |
| `Age` and `BirthDate`/`DateOfBirth` | matching each other |
| `Price`/`Amount`/`Total`, `Quantity`, `Rating`, `Year`, `Percentage`, `Latitude` | sensible ranges |
| `CreatedAt`, `UpdatedAt`, `ExpiresAt`/`DueDate` | an ordered timeline: past, recent, future |

`Name` and `Title` look at the declaring type (`Company.Name` is a company name, `Product.Name` a product, `Book.Title` a title). Anything else gets a value based on its type. Add your own:

```csharp
Fake.Conventions.Add<string>(name => name.EndsWith("Sku"), f => f.Random.Replace("SKU-#####")); // app-wide
Fake.For<Order>().WithConvention<decimal>(n => n == "Vat", f => 0.25m);                          // one generator
```

Both apply to every object in the graph, so `Order.Lines[0].Sku` is covered as well as `Order.Sku`. A generator's own conventions come before the app-wide ones, and explicit rules (`With`, `Ignore`…) come before both.

To give **every member of a type** a value, whatever it is called, use `WithType` (or `Fake.Conventions.Add<T>(factory)` app-wide):

```csharp
var orders = Fake.For<Order>()
    .WithType<DateOnly>(f => f.Date.Past().Date)    // every DateOnly member, nested objects included
    .WithType("n/a")                                 // every string member, FirstName and Email too
    .With(o => o.Reference, f => f.Random.Replace("ORD-####"))   // a rule for the member still wins
    .Generate(10);
```

It sets members: the elements of collections keep their generated values, the nullable form of the type (`DateOnly?`) is matched too, and data annotations are not applied to the value. It is `WithConvention<T>(_ => true, factory)`, and `.Strict()` and the analyzer count it as covering the members of that type.

### NodaTime

The `Phoney.NodaTime` package adds NodaTime values and NodaTime members:

```bash
dotnet add package Phoney.NodaTime
```

```csharp
Instant created = faker.Noda().Instant.Past();
LocalDate birthday = faker.Noda().LocalDate.Birthdate();
var subscriptions = Fake.Many<Subscription>(10); // Instant CreatedAt, LocalDate BirthDate… follow the conventions
```

`Instant`, `OffsetDateTime`, `ZonedDateTime`, `LocalDateTime`, `LocalDate`, `LocalTime` and `Duration` members are generated like their built-in counterparts (`DateTimeOffset`, `DateTime`, `DateOnly`, `TimeOnly`, `TimeSpan`), so `CreatedAt`, `ExpiresAt`, `BirthDate` and `Age` agree; this works with reflection and with source-generated models. See the [package README](src/Phoney.NodaTime/README.md).

## Source generator and Native AOT

`Fake.For<T>()` uses reflection (with compiled delegates) unless a generated model exists. Declare the types you generate:

```csharp
[FakeFor<Customer>, FakeFor<Order>]
internal static partial class TestData;

var customers = TestData.Customer.Seed(1).Generate(100); // a Generator<Customer>, no reflection
```

The generator (included in the package) writes a model for each listed type and every type it contains, and registers them at startup, so `Fake.For<Customer>()` uses them as well. `Fake.For`, `One`, `Many` and `Populate` fall back to reflection for other types, so the compiler flags them in trimmed and AOT apps even when a model exists; **`Fake.Generated<Customer>()`** is the same facade (seed, locale and clock of `Fake.Seeded`) without any reflection, so it is warning free there, and it throws with the fix when the type has no generated model:

```csharp
var customer = Fake.Generated<Customer>().Populate(new Customer { Id = 42 }); // trim and AOT safe
```

Generated and reflection models produce **identical data for the same seed**. With generated models Phoney is trim and Native AOT safe; reflection entry points are annotated (`RequiresUnreferencedCode`/`RequiresDynamicCode`) so the compiler tells you where they're used.

`[FakeFor]` and `[FakeSubtype]` go on a partial class or record. Subtypes of interfaces and abstract classes need models too: types listed with `[JsonDerivedType]` on the base type get them automatically, and `[FakeSubtype<IShape, Circle>]` on the same class generates a model for `Circle` and registers it as a subtype app-wide (see [Interfaces and subtypes](#interfaces-and-subtypes)). NodaTime members are generated without reflection when `Phoney.NodaTime` is referenced.

The package also reports these diagnostics:

| Id | Severity | Meaning |
|---|---|---|
| PHONEY001 | Error | the class with `[FakeFor]` must be `partial` |
| PHONEY002 | Warning | a type can't be generated (abstract, no public constructor, inaccessible); its members stay default |
| PHONEY003 | Error | `[FakeFor]` is inside a generic type |
| PHONEY004 | Warning | a `.Strict()` generator has scalar members without a rule, convention or annotation |
| PHONEY005 | Warning | the result of a generator method is discarded (`gen.With(...);`: generators are immutable), or of `Populate` on a struct (it returns the populated copy) |
| PHONEY006 | Warning | `Fake.Seeded(...)` is never disposed, so everything after it stays seeded; use `using var _ = Fake.Seeded(1);` |
| PHONEY007 | Warning | a rule would throw when the generator is first used: the member isn't a public settable member or constructor parameter, the selector isn't `x => x.Member`, or an object-dependent rule targets a constructor parameter |
| PHONEY008 | Warning | a rule replaces an earlier rule for the same member in the same chain, or a `Use` generator / `Subtype` mapping replaces an earlier one for the same type in the same chain |
| PHONEY009 | Error | `[FakeSubtype<TBase, TSubtype>]` names an abstract type or interface, which can't be created; the registration is left out |
| PHONEY010 | Warning | a new generator with a constant `.Seed(...)` is created inside a loop, so every iteration generates the same objects |
| PHONEY011 | Warning | a constant argument is out of range and the call throws (`NullProbability(1.5)`, `CollectionSize(5, 2)`, `Generate(-1)`, `Unique(x => x.Id, 0)`, `Where(x => …, 0)`…) |
| PHONEY012 | Info | `Fake.For<T>()`, `One`, `Many` or `Populate` generate a type with reflection because no `[FakeFor<T>]` covers it |

The IDE offers fixes for two of them: **PHONEY001** (make the class and its containing types `partial`) and **PHONEY012** (add `[FakeFor<T>]` to the partial class that already lists types, or create a `FakeModels` class). PHONEY012 is only a suggestion, since reflection is fine unless the app is trimmed or published with Native AOT; silence it with `dotnet_diagnostic.PHONEY012.severity = none` in `.editorconfig`.

The advisory rules (PHONEY004–008 and 010–012) are in the category `Phoney.Usage`; PHONEY001–003 and 009, which come from the source generator, are in `Phoney`. To switch off or tone down all the advisory rules at once without touching the generator's errors:

```ini
# .editorconfig
[*.cs]
dotnet_analyzer_diagnostic.category-Phoney.Usage.severity = suggestion
```

## Dependency injection and ASP.NET Core

The `Phoney.DependencyInjection` package registers fakers and generators in `IServiceCollection`:

```bash
dotnet add package Phoney.DependencyInjection
```

```csharp
builder.Services.AddPhoney(o => { o.Locale = "sv"; o.Seed = 42; })
    .AddGenerator<Customer>(g => g.With(c => c.Country, "SE"))  // singleton Generator<Customer>
    .AddGenerator(TestData.Order);                              // source-generated: no reflection, AOT safe

app.MapGet("/demo/customers", (Generator<Customer> customers) => customers.Generate(10));
app.MapGet("/demo/name", (IFaker faker) => faker.Person.FullName());
```

- `IFaker`/`Faker` are scoped, `IFakerFactory` creates fakers for other locales and seeds, and generators are singletons. `IFaker` itself is in the core package (implemented by `Faker`), for your own abstractions and test doubles.
- `PhoneyOptions`: `Locale`, `Seed`, `ReferenceDate` and `TimeProvider`.
- With `Seed`, every scope starts at the seed, so each request or test scope is reproducible.
- `ReferenceDate`, or a `TimeProvider` (from the options or registered in the container, e.g. `FakeTimeProvider`), sets "now".
- In integration tests, call `AddPhoney(o => o.Seed = 1)` again in `WebApplicationFactory.ConfigureTestServices`; it configures the same options.

## EF Core seeding

The `Phoney.EntityFrameworkCore` package turns generated data into `HasData` seed data:

```bash
dotnet add package Phoney.EntityFrameworkCore
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    var customers = modelBuilder.Entity<Customer>().HasFakeData(50);
    modelBuilder.Entity<Order>().HasFakeData(200, g => g.With(o => o.Status, OrderStatus.Paid));
}
```

- The data is the same on every model build (the seed is derived from the entity name), so migrations stay stable.
- Navigations and owned objects are left empty, strings respect `HasMaxLength`, a single integer key is numbered 1…n when values clash, and foreign keys are pointed at seeded principals (seed principals first, or link with `WithOneOf`).
- Call it after the entity's other configuration; it reads keys, maximum lengths and navigations from the model.

## Test data in TUnit

The `Phoney.TUnit` package adds `[FakeData]`, which fills [TUnit](https://tunit.dev) test parameters the way Phoney fills the members of an object:

```bash
dotnet add package Phoney.TUnit
```

```csharp
[Test, FakeData]
public async Task Places_order(Customer customer, string email, [Range(1, 10)] int quantity) { … }

[Test, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
public async Task Formats_address(string street, string city, string zipCode) { … }
```

- Parameters are filled by name and type, objects and collections recursively, and data annotations on parameters are honoured.
- The parameters of one test case agree like the members of one object: `firstName`, `email`, `birthDate` and `age` describe the same person, `street`, `city` and `zipCode` one address.
- **Reproducible by default**: the seed is derived from the test's class, method and parameters, so every run and machine gets the same values (and the same test names). Set `Seed = 42` for other values.
- Options: `Count`, `Seed`, `Locale`, `NullProbability` and `ReferenceDate`. It also works on the test class (constructor parameters) and on `required` properties.
- Interface and abstract parameters get one of their subtypes from `Fake.Subtypes` or `[JsonDerivedType]`.
- Test data is created while TUnit discovers tests, so register custom conventions (`Fake.Conventions.Add`) and subtypes (`Fake.Subtypes.Add`) in a `[Before(TestDiscovery)]` hook; `[Before(Assembly)]` and later hooks run too late.
- **`PHONEY_SEED=123 dotnet test`** moves the seed of every `[FakeData]` test at once, to run the whole suite on other data; the same value replays it. Tests still differ from each other, and an explicit `Seed` stays as it is.

### Random data you can replay

`[FakeSeed]` runs a test on a seeded `Fake` facade: `Fake.One<T>()`, `Fake.Many<T>()`, `Fake.For<T>()` and `Fake.Person…` give different data on every run, and **when the test fails, its seed is printed in the test output**:

```csharp
[Test, FakeSeed]
public async Task Totals_add_up()
{
    var orders = Fake.Many<Order>(100);                 // different on every run
    await Assert.That(Totals.Of(orders)).IsEqualTo(orders.Sum(o => o.Total));
}
// Phoney: this test ran with random seed 481516234. Replay it with [FakeSeed(481516234)] or PHONEY_SEED=481516234.
```

- **Replay** with `[FakeSeed(481516234)]` on the test (it pins the seed while you fix the bug), or `PHONEY_SEED=481516234 dotnet test` to rerun a whole run. A pinned seed wins over `PHONEY_SEED`; a `PHONEY_SEED` that is not a whole number is reported with a message that says so.
- On a method it wins over the class, and on a class over the assembly (`[assembly: FakeSeed]` makes every test random and replayable). Parallel tests don't affect each other.
- The seed covers the `Fake` facade of the test, not generators created earlier (a `static` field) or ones that set their own `.Seed(...)`. Skipped tests print nothing.
- It works with other test executors (`[TestExecutor<T>]`): the executor the test already has runs inside the seeded scope.
- Other frameworks can do the same with `FakeSeed.Pick()` (`PHONEY_SEED`, else a new seed), `Fake.Seeded(seed)` and printing the seed when the test fails.

The same machinery is available to other frameworks through `Fake.Arguments(method)`, which returns rows of values for a parameter list.

## Test data in xUnit, NUnit and MSTest

`Phoney.Xunit` ([xUnit.net](https://xunit.net) v3), `Phoney.NUnit` and `Phoney.MSTest` give the same `[FakeData]` as [Phoney.TUnit](#test-data-in-tunit), with the same options, seeds and rules, so a test gets the same values whichever framework runs it:

```bash
dotnet add package Phoney.Xunit     # or Phoney.NUnit, Phoney.MSTest
```

```csharp
// xUnit.net v3
[Theory, FakeData]
public void Places_order(Customer customer, string email, [Range(1, 10)] int quantity) { … }

// NUnit: [FakeData] replaces [Test], like [TestCase]
[FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
public void Formats_address(string street, string city, string zipCode) { … }

// MSTest
[TestMethod, FakeData]
public void Places_order(Customer customer, string email, [Range(1, 10)] int quantity) { … }
```

- Parameters are filled by name and type, objects and collections recursively, data annotations are honoured, and the parameters of one test case agree like the members of one object.
- **Reproducible by default** (the seed is derived from the test), with `Count`, `Seed`, `Locale`, `NullProbability` and `ReferenceDate`; `PHONEY_SEED=123 dotnet test` moves every test at once. The shared rules live in `FakeDataOptions` in the main package.
- These attributes fill **method parameters**. Constructor and property injection (TUnit) and `[FakeSeed]` have no counterpart in the other frameworks.
- Test data is created while the framework discovers tests, so register custom conventions (`Fake.Conventions.Add`) and subtypes before that, for example in a module initializer.
- Packages target .NET 8 and .NET 10 and depend on xUnit.net v3 `xunit.v3.extensibility.core` 4.0.2, NUnit 5.0.0 and MSTest 4.5.1.

## Locales

```csharp
Locales.All;                      // 77 locales with code, title, endonym, script, fallback chain
new Faker("de-AT");               // de_AT → de → en → base
new Faker(CultureInfo.CurrentCulture); // "sv-SE" → sv, "sr-Latn-RS" → sr_RS_latin
Fake.Locale = "fr";               // default for Fake.* and generators
```

<details>
<summary>Supported locales</summary>

<!-- locales:start -->
76 locales from faker.js 10.6.0. Codes accept `-` or `_` and any casing (`de-AT`, `de_at`). .NET culture names resolve to the closest locale, including script-tagged ones (`sv-SE` → `sv`, `sr-Latn-RS` → `sr_RS_latin`, `zh-Hant-TW` → `zh_TW`, `ckb-IQ` → `ku_ckb`); a requested script is never substituted.

| Code | Language | Native name | Falls back to |
|---|---|---|---|
| `af_ZA` | Afrikaans (South Africa) | Afrikaans (Suid-Afrika) | en |
| `ar` | Arabic | اَلْعَرَبِيَّةُ | en |
| `az` | Azerbaijani | azərbaycan dili | en |
| `bn_BD` | Bengali (Bangladesh) | বাংলা (বাংলাদেশ) | en |
| `cs_CZ` | Czech (Czechia) | čeština (Česká republika) | en |
| `cy` | Welsh | Cymraeg | en |
| `da` | Danish | Dansk | en |
| `de` | German | Deutsch | en |
| `de_AT` | German (Austria) | Deutsch (Österreich) | de → en |
| `de_CH` | German (Switzerland) | Deutsch (Schweiz) | de → en |
| `dv` | Maldivian | ދިވެހި | en |
| `el` | Greek | Ελληνικά | en |
| `en` | English | English | — |
| `en_AU` | English (Australia) | English (Australia) | en |
| `en_AU_ocker` | English (Australia Ocker) | English (Australia) | en_AU → en |
| `en_BORK` | English (Bork) | English (Bork) | en |
| `en_CA` | English (Canada) | English (Canada) | en |
| `en_GB` | English (Great Britain) | English (Great Britain) | en |
| `en_GH` | English (Ghana) | English (Ghana) | en |
| `en_HK` | English (Hong Kong) | English (Hong Kong) | en |
| `en_IE` | English (Ireland) | English (Ireland) | en |
| `en_IN` | English (India) | English (India) | en |
| `en_NG` | English (Nigeria) | English (Nigeria) | en |
| `en_NP` | English (Nepal) | English (Nepal) | en |
| `en_US` | English (United States) | English (United States) | en |
| `en_ZA` | English (South Africa) | English (South Africa) | en |
| `eo` | Esperanto | Esperanto | en |
| `es` | Spanish | Español | en |
| `es_MX` | Spanish (Mexico) | Español (México) | es → en |
| `fa` | Farsi/Persian | فارسی | en |
| `fi` | Finnish | suomi | en |
| `fr` | French | Français | en |
| `fr_BE` | French (Belgium) | Français (Belgique) | fr → en |
| `fr_CA` | French (Canada) | Français (Canada) | fr → en |
| `fr_CH` | French (Switzerland) | Français (Suisse) | fr → en |
| `fr_LU` | French (Luxembourg) | Français (Luxembourg) | fr → en |
| `fr_SN` | French (Senegal) | Français (Sénégal) | fr → en |
| `he` | Hebrew | עברית | en |
| `hr` | Croatian | Hrvatski | en |
| `hu` | Hungarian | magyar | en |
| `hy` | Armenian | Հայերեն | en |
| `id_ID` | Indonesian (Indonesia) | Bahasa Indonesia (Indonesia) | en |
| `it` | Italian | Italiano | en |
| `ja` | Japanese | 日本語 | en |
| `ka_GE` | Georgian (Georgia) | ქართული (საქართველო) | en |
| `ko` | Korean | 한국어 | en |
| `ku_ckb` | Kurdish (Sorani) | کوردی (سۆرانی) | en |
| `ku_kmr_latin` | Kurdish (Kurmanji, Latin) | Kurdî (Kurmancî) | en |
| `lv` | Latvian | latviešu valoda | en |
| `mk` | Macedonian | македонски јазик | en |
| `mn_MN_cyrl` | Mongolian (Mongolia, Cyrillic) | Монгол (Монгол Улс, Кирилл) | en |
| `nb_NO` | Norwegian (Norway) | Norsk bokmål (Norge) | en |
| `ne` | Nepali | नेपाली | en |
| `nl` | Dutch | Nederlands | en |
| `nl_BE` | Dutch (Belgium) | Nederlands (België) | nl → en |
| `pl` | Polish | Polski | en |
| `pt_BR` | Portuguese (Brazil) | Português (Brasil) | en |
| `pt_PT` | Portuguese (Portugal) | Português (Portugal) | en |
| `ro` | Romanian | Română | en |
| `ro_MD` | Romanian (Moldova) | Română (Moldova) | ro → en |
| `ru` | Russian | Русский | en |
| `sk` | Slovak | slovenčina | en |
| `sl_SI` | Slovenian (Slovenia) | Slovensko (Slovenia) | en |
| `sr_RS_latin` | Serbian (Serbia, Latin) | srpski (Srbija, latinica) | en |
| `sv` | Swedish | Svenska | en |
| `ta_IN` | Tamil (India) | தமிழ் (இந்தியா) | en |
| `th` | Thai | ไทย | en |
| `tr` | Turkish | Türkçe | en |
| `uk` | Ukrainian | Українська | en |
| `ur` | Urdu | اردو | en |
| `uz_UZ_latin` | Uzbek (Uzbekistan, Latin) | O'zbekcha | en |
| `vi` | Vietnamese | Tiếng Việt | en |
| `yo_NG` | Yoruba (Nigeria) | Yoruba (Naijiria) | en |
| `zh_CN` | Chinese (China) | 中文 (中国) | en |
| `zh_TW` | Chinese (Taiwan) | 中文 (臺灣) | en |
| `zu_ZA` | Zulu (South Africa) | isiZulu (Iningizimu Afrika) | en |
<!-- locales:end -->

</details>

Fallback works like faker.js: each data group (`person.first_name`, `location.city_pattern`…) comes from the first locale in the chain that defines it; data a locale marks as not applicable (e.g. name prefixes in Azerbaijani) is never borrowed from another locale.

### Custom data

```csharp
// A new locale on top of an existing one
Locales.Register("sv_dalarna", l => l
    .FallbackTo("sv")                                  // sv → en → base
    .Cities("Mora", "Falun", "Rättvik")
    .FirstNames(Sex.Female, "Kerstin", "Margit")      // male names still come from sv
    .EmailDomains("dalarna.example")
    .PhoneFormats("0250-## ## ##"));

// Extra or replaced data in an existing locale
Locales.Extend("en", l => l.CompanyNames("{{Person.LastName}} & Sons", "Acme {{Commerce.Department}}"));
```

Typed methods cover the common data: `FirstNames`, `LastNames`, `JobTitles`, `Cities`, `Streets`, `States`, `Countries`, `ZipCodeFormats`, `PhoneFormats`, `EmailDomains`, `CompanyNames` and `LoremWords`. Each replaces only that data; everything else is inherited. For anything else, use a faker.js data path, or import a faker.js locale file:

```csharp
Locales.Extend("en", l => l
    .Set("commerce.department", "Toys", "Garden")
    .Set("person.nickname", "Ace", "Buddy"));          // new paths are allowed: faker.Pick("person.nickname")

Locales.Register("en_PIRATE", File.OpenRead("pirate.json"), "en");
```

## Performance

Locale data is compiled ahead of time into compact Brotli-compressed resources (about 1 MB for all locales), loaded lazily per locale. Fallback chains are resolved once at load into a flat array, so a lookup is one array index and picking a name returns a stored string without allocating. Templates are parsed once and cached.

Indicative numbers (Apple Silicon, .NET 10, BenchmarkDotNet short run):

| Operation | Time | Allocated |
|---|---:|---:|
| `Person.FirstName()` | 3.6 ns | 0 B |
| `Randomizer.Int(0, 1000)` | 2.2 ns | 0 B |
| `Helpers.FromRegExp("[A-Z]{3}-\\d{4}")` | 52 ns | 144 B |
| `Parse("{{person.firstName}} {{person.lastName}} lives in {{location.city}}")` | 174 ns | 418 B |
| `Person.FullName()` | 186 ns | 348 B |
| `Finance.Iban()` | 304 ns | 388 B |
| 1,000 customers with address and 1–3 orders | 1.8 ms | 2 MB |
| … with `GenerateParallel` | 0.96 ms | 2.8 MB |
| Loading the `en` locale (once per process) | 3.9 ms | 1.4 MB |

Run the benchmarks with:

```bash
dotnet run -c Release --project benchmarks/Phoney.Benchmarks -- --filter '*'
```

## How the faker.js data gets in

```
@faker-js/faker (npm, pinned)  →  tools/extract (Node)      →  data/raw/*.json
data/raw/*.json                →  tools/Phoney.DataCompiler  →  src/Phoney/Resources/Locales/*.bin.br
                                                               src/Phoney/Data/Generated/*.g.cs
                                                               data/report.md
```

The compiler flattens faker.js definitions into typed entries, builds a key catalog (so modules use integer keys), and **validates every template** against faker.js' method list and each locale's fallback chain; broken references fail the build. A test checks that every faker.js method referenced by locale data is implemented.

The **Sync faker.js data** workflow runs weekly: when npm has a newer `@faker-js/faker` it re-imports, recompiles and tests, then opens a pull request with the raw JSON diff and the data report, or an issue if the new data needs code changes. To update manually:

```bash
cd tools/extract && npm install --save-exact @faker-js/faker@latest && node export-locales.mjs ../../data/raw && cd ../..
dotnet run --project tools/Phoney.DataCompiler
dotnet test
```

## Building

```bash
dotnet build
dotnet test
dotnet publish tests/Phoney.AotSmoke -c Release   # Native AOT smoke test
```

## License

MIT. Locale data and parts of the generation logic come from [faker.js](https://github.com/faker-js/faker) (MIT); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
