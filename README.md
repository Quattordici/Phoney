# Phony

**Fast, easy and flexible fake data for .NET**, powered by the locale data of [faker.js](https://github.com/faker-js/faker).

- **77 locales** imported from faker.js (`@faker-js/faker` 10.6.0), kept up to date by an automated sync pipeline.
- **One line to a value or a fully populated object**: `Fake.Person.FullName()`, `Fake.Many<Customer>(100)`.
- **Convention-based population** of classes, records and structs: `FirstName`, `Email`, `BillingCity`, `Price`, `CreatedAt`… just work, and names, emails and avatars of one object belong to the same person.
- **Flexible**: fluent, immutable generators, rules that depend on other members, uniqueness, strict mode, custom conventions, custom locales, faker.js-style templates.
- **Fast**: lookups are array indexed and allocation free, templates are parsed once, and a **source generator** makes generation reflection free.
- **Deterministic**: same seed means same data on every OS and .NET version, item *n* is the same whether you generate 1 or 1,000,000 items, sequentially or in parallel.
- **Trim and Native AOT safe** with the source generator.

Targets .NET 8 and .NET 10.

```bash
dotnet add package Phony
```

## Quick start

```csharp
using Phony;

// Values
Fake.Person.FullName();                         // "Madisen Collins"
Fake.Internet.Email();                          // "Ryann_Beahan@yahoo.com"
Fake.Location.StreetAddress(useFullAddress: true);

// A specific locale and a seed
var faker = new Faker("sv", seed: 42);
faker.Person.FullName();                        // "Madeleine Öberg Nordström"
faker.Finance.Iban("SE", formatted: true);      // valid check digits

// Objects
Customer customer = Fake.One<Customer>();
List<Customer> customers = Fake.Many<Customer>(1_000);
```

## Values

A `Faker` exposes faker.js' modules with .NET naming:

| Module | Examples |
|---|---|
| `Person` | `FirstName(Sex.Female)`, `LastName()`, `FullName()`, `JobTitle()`, `Bio()`, `Prefix()` |
| `Location` | `StreetAddress()`, `City()`, `ZipCode()`, `State()`, `Country()`, `CountryCode()`, `Latitude()`, `NearbyGpsCoordinate()` |
| `Internet` | `Email()`, `Username()`, `Url()`, `DomainName()`, `Ipv4(IPv4Network.PrivateC)`, `Ipv6()`, `Mac()`, `Password()`, `UserAgent()`, `Jwt()`, `Emoji()` |
| `Phone` | `Number(PhoneStyle.International)`, `Imei()` |
| `Company` / `Commerce` | `Name()`, `CatchPhrase()`, `ProductName()`, `Price()`, `Isbn()`, `Upc()` |
| `Finance` | `Iban()`, `Bic()`, `CreditCardNumber("visa")`, `Currency()`, `Amount()`, `RoutingNumber()`, `BitcoinAddress()` |
| `Date` | `Past()`, `Future()`, `Recent()`, `Soon()`, `Between(...)`, `Birthdate(18, 65)`, `Month()`, `Weekday()` |
| `Lorem` / `Word` | `Sentence()`, `Paragraphs()`, `Slug()`, `Noun(minLength: 5)` |
| `String` / `Number` | `Uuid()`, `UuidV7()`, `Ulid()`, `NanoId()`, `AlphaNumeric(10)`, `Int(1, 6)`, `Decimal(0, 100, 2)` |
| `Helpers` | `FromRegExp("[A-Z]{3}-\\d{4}")`, `ReplaceSymbols("##-??")`, `ReplaceCreditCardSymbols()`, `Slugify()` |
| also | `Animal`, `Book`, `Music`, `Food`, `Hacker`, `Vehicle` (with valid VINs), `Airline`, `Science`, `Color`, `Database`, `System`, `Git`, `Image` |

Checksums are real: credit cards and IMEIs pass Luhn, IBANs pass MOD 97, ISBN, UPC, VIN and ABA routing numbers have valid check digits.

`Fake.*` is a thread-safe static facade over a per-thread `Faker`. For reproducible values create a `Faker` with a seed, and set `ReferenceDate` if you use relative dates:

```csharp
var faker = new Faker("de", seed: 2024) { ReferenceDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) };
```

`faker.Random` is the seeded source of randomness (xoshiro256\*\*) for your own values: `Int`, `Long`, `Double`, `Decimal`, `Bool(0.2)`, `Element(list)`, `Elements(list, 3)`, `Enum<T>()`, `Guid()`, `Replace("##??")`.

## Templates

Phony understands faker.js templates, so faker.js data and patterns work unchanged:

```csharp
faker.Parse("{{person.firstName}} ordered {{commerce.productName}} from {{company.name}}");
faker.Parse("Ticket {{helpers.fromRegExp([A-Z]{3}-[0-9]{4})}}, PIN {{string.numeric(4)}}");
faker.Parse("{{person.last_name.generic}} flies from {{airline.airport.iataCode}}"); // data paths and record fields
```

An expression is a faker.js method (`person.firstName`, `number.int({"min":1,"max":9})`) or a locale data path. Register your own functions with `TemplateFunctions.Register("shop.sku", (f, _) => f.Random.Replace("SKU-#####"))`.

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

- **Supported shapes**: classes with a parameterless constructor, records and classes with constructor parameters (the constructor with most parameters is used), `init` and `required` members, public fields, structs, nested objects, `T[]`, `List<T>` and its interfaces, `HashSet<T>`, `Dictionary<K,V>`, enums, nullable types, `byte[]`, and `Guid`, `DateTime(Offset)`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri` plus every numeric type.
- **Cycles** (`Customer → Order → Customer`) stop with `null`. `MaxDepth(n)` (default 4) bounds nesting, `CollectionSize(min, max)` (default 1–3) sizes collections, `NullProbability(p)` (default 0) leaves nullable members null.
- **Generators are immutable**: every method returns a new generator, so you can share them and derive variants: `var admins = users.With(u => u.Role, "Admin");`.
- **Order**: constructor arguments, then members in declaration order, then rules that depend on the object (in the order added), then `AfterCreate` actions. `CreateWith(f => new Customer(...))` replaces construction.
- **Strict mode**: `.Strict()` throws when a scalar member has neither a rule nor a convention, so new members can't silently get meaningless data.

### Conventions

Member names (case and `_` ignored, trailing words matched, so `BillingCity` is a city and `HomeEmailAddress` an email) pick the generator, for example:

| Member | Value |
|---|---|
| `FirstName`, `LastName`, `FullName`, `Name` on a person-like type | names of one generated identity |
| `Email`, `Username`, `Avatar`, `Gender`/`Sex` enums | consistent with that identity |
| `Street`, `Address1`, `City`, `Zip`/`PostalCode`, `State`, `Country`, `CountryCode`, `Latitude` | location data of the locale |
| `Phone`, `Mobile`, `Company`, `JobTitle`, `Department`, `ProductName`, `Description`, `Notes` | matching module |
| `Url`, `Website`, `Ip`, `Mac`, `UserAgent`, `Iban`, `Bic`, `CreditCard`, `Isbn`, `Vin`, `Sku`, `Token`, `Version` | matching module |
| `Id` (`int`/`long`) | 1, 2, 3… in generation order; `CustomerId` is a random reference |
| `Age`, `Price`/`Amount`/`Total`, `Quantity`, `Rating`, `Year`, `Percentage` | sensible ranges |
| `BirthDate`/`DateOfBirth`, `CreatedAt`, `UpdatedAt`, `ExpiresAt`/`DueDate` | birthdate, past, recent, future |

`Name` and `Title` look at the declaring type (`Company.Name` is a company name, `Product.Name` a product, `Book.Title` a title). Anything else gets a value based on its type. Add your own:

```csharp
Fake.Conventions.Add<string>(name => name.EndsWith("Sku"), f => f.Random.Replace("SKU-#####")); // app-wide
Fake.For<Order>().WithConvention<decimal>(n => n == "Vat", f => 0.25m);                          // one generator
```

## Source generator and Native AOT

`Fake.For<T>()` uses reflection (with compiled delegates) unless a generated model exists. Declare the types you generate:

```csharp
[FakeFor<Customer>, FakeFor<Order>]
internal static partial class TestData;

var customers = TestData.Customer.Seed(1).Generate(100); // a Generator<Customer>, no reflection
```

The generator (included in the package) writes a model for each listed type and every type it contains, and registers them at startup, so `Fake.For<Customer>()` uses them as well. Generated and reflection models produce **identical data for the same seed**. With generated models Phony is trim and Native AOT safe; reflection entry points are annotated (`RequiresUnreferencedCode`/`RequiresDynamicCode`) so the compiler tells you where they're used.

## Locales

```csharp
Locales.All;                      // 77 locales with code, title, endonym, script, fallback chain
new Faker("de-AT");               // de_AT → de → en → base
new Faker(CultureInfo.CurrentCulture); // "sv-SE" resolves to sv
Fake.Locale = "fr";               // default for Fake.* and generators
```

Fallback works like faker.js: each data group (`person.first_name`, `location.city_pattern`…) comes from the first locale in the chain that defines it; data a locale marks as not applicable (e.g. name prefixes in Azerbaijani) is never borrowed from another locale.

### Custom data

```csharp
// A new locale on top of an existing one
Locales.Register("sv_dalarna", l => l
    .FallbackTo("sv")                                  // sv → en → base
    .Set("location.city_pattern", "Mora", "Falun", "Rättvik")
    .Set("person.nickname", "Kalle", "Lisa"));          // new keys are allowed

// Extra or replaced data in an existing locale
Locales.Extend("en", l => l.Set("commerce.department", "Toys", "Garden"));

// faker.js-shaped JSON (the same format as faker.js locale files)
Locales.Register("en_PIRATE", File.OpenRead("pirate.json"), "en");

new Faker("sv_dalarna").Pick("person.nickname");
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
dotnet run -c Release --project benchmarks/Phony.Benchmarks -- --filter '*'
```

## How the faker.js data gets in

```
@faker-js/faker (npm, pinned)  →  tools/extract (Node)      →  data/raw/*.json
data/raw/*.json                →  tools/Phony.DataCompiler  →  src/Phony/Resources/Locales/*.bin.br
                                                               src/Phony/Data/Generated/*.g.cs
                                                               data/report.md
```

The compiler flattens faker.js definitions into typed entries, builds a key catalog (so modules use integer keys), and **validates every template** against faker.js' method list and each locale's fallback chain; broken references fail the build. A test checks that every faker.js method referenced by locale data is implemented.

The **Sync faker.js data** workflow runs weekly: when npm has a newer `@faker-js/faker` it re-imports, recompiles and tests, then opens a pull request with the raw JSON diff and the data report, or an issue if the new data needs code changes. To update manually:

```bash
cd tools/extract && npm install --save-exact @faker-js/faker@latest && node export-locales.mjs ../../data/raw && cd ../..
dotnet run --project tools/Phony.DataCompiler
dotnet test
```

## Coming from Bogus

| Bogus | Phony |
|---|---|
| `new Faker<Customer>().RuleFor(c => c.Name, f => f.Name.FullName())` | `Fake.For<Customer>().With(c => c.Name, f => f.Person.FullName())`, or nothing at all: conventions fill `Name` |
| `.RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.FirstName, c.LastName))` | `.With(c => c.Email, (f, c) => f.Internet.Email(c.FirstName, c.LastName))` |
| `.UseSeed(42)`, `Randomizer.Seed = new Random(42)` | `.Seed(42)` (per generator, stable across platforms) |
| `.StrictMode(true)` | `.Strict()` |
| `.RuleSet("admin", ...)` | derive a generator: `var admins = customers.With(c => c.Role, "Admin")` |
| `.Generate(10)` | `.Generate(10)`, `.Stream()`, `.GenerateParallel(10)` |
| `f.Name`, `f.Address` | `f.Person`, `f.Location` (faker.js naming) |

## Building

```bash
dotnet build
dotnet test
dotnet publish tests/Phony.AotSmoke -c Release   # Native AOT smoke test
```

## License

MIT. Locale data and parts of the generation logic come from [faker.js](https://github.com/faker-js/faker) (MIT); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
