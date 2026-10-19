<!-- An absolute URL so the logo also shows on nuget.org, which renders this README but not relative images. -->
# [![Phoney](https://raw.githubusercontent.com/Quattordici/Phoney/main/images/phoney-logo.png)](https://github.com/Quattordici/Phoney)

[![NuGet](https://img.shields.io/nuget/v/Phoney.svg?label=NuGet)](https://www.nuget.org/packages/Phoney)
[![NuGet downloads](https://img.shields.io/nuget/dt/Phoney.svg?label=Downloads)](https://www.nuget.org/packages/Phoney)

[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=Quattordici_Phoney&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Quattordici_Phoney)
[![Reliability](https://sonarcloud.io/api/project_badges/measure?project=Quattordici_Phoney&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=Quattordici_Phoney)
[![Security](https://sonarcloud.io/api/project_badges/measure?project=Quattordici_Phoney&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=Quattordici_Phoney)
[![Maintainability](https://sonarcloud.io/api/project_badges/measure?project=Quattordici_Phoney&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=Quattordici_Phoney)
[![Snyk](https://snyk.io/test/github/Quattordici/Phoney/badge.svg)](https://github.com/Quattordici/Phoney/actions/workflows/ci.yml?query=branch%3Amain)

**Fast, easy and flexible fake data for .NET**, powered by the locale data of [faker.js](https://github.com/faker-js/faker).

- **76 locales** imported from faker.js (`@faker-js/faker` 10.6.0), kept up to date by an automated sync pipeline.
- **One line to a value or a fully populated object**: `Fake.Person.FullName()`, `Fake.Many<Customer>(100)`. Name conventions fill `FirstName`, `Email`, `BillingCity`, `Price`, `CreatedAt`… and the names, email and avatar of one object belong to the same person.
- **Flexible**: fluent, immutable generators, rules that depend on other members or reach into nested ones (`.With(x => x.Lines.Each().Sku, …)`), uniqueness, conditions, numbering, weighted choices, subtypes and `[JsonDerivedType]`, strict mode checked at compile time, custom conventions and locales, faker.js-style templates.
- **Test-focused**: invalid and boundary data from your data annotations, `[FakeData]` for xUnit, NUnit, MSTest and TUnit, replayable random tests (`PHONEY_SEED=… dotnet test`), a `TimeProvider` for "now", and stable stand-ins for real data (`Fake.Keyed("customer:42")`, `Pseudonymize`).
- **Data from files and to files**: fill members from the rows of a CSV or JSON file, or write generated objects as CSV or JSON.
- **Fast**: array-indexed, allocation-free lookups, templates parsed once, and a **source generator** that makes generation reflection free, trim and Native AOT safe ([benchmarks](#performance)).
- **Deterministic**: the same seed gives the same data, dates included, on every OS, CPU and .NET version, with or without ICU, and every day you run it; item *n* is the same whether you generate 1 or 1,000,000 items, sequentially or in parallel.

| Package | What it adds | |
|---|---|---|
| `Phoney` | fakers, generators, the source generator and the analyzers | [![NuGet](https://img.shields.io/nuget/v/Phoney.svg?label=)](https://www.nuget.org/packages/Phoney) |
| `Phoney.MSTest` | [`[FakeData]` for MSTest](#test-data-in-xunit-nunit-mstest-and-tunit) | [![NuGet](https://img.shields.io/nuget/v/Phoney.MSTest.svg?label=)](https://www.nuget.org/packages/Phoney.MSTest) |
| `Phoney.NUnit` | [`[FakeData]` for NUnit](#test-data-in-xunit-nunit-mstest-and-tunit) | [![NuGet](https://img.shields.io/nuget/v/Phoney.NUnit.svg?label=)](https://www.nuget.org/packages/Phoney.NUnit) |
| `Phoney.TUnit` | [`[FakeData]` and `[FakeSeed]` for TUnit](#test-data-in-xunit-nunit-mstest-and-tunit) | [![NuGet](https://img.shields.io/nuget/v/Phoney.TUnit.svg?label=)](https://www.nuget.org/packages/Phoney.TUnit) |
| `Phoney.Xunit` | [`[FakeData]` for xUnit.net v3](#test-data-in-xunit-nunit-mstest-and-tunit) | [![NuGet](https://img.shields.io/nuget/v/Phoney.Xunit.svg?label=)](https://www.nuget.org/packages/Phoney.Xunit) |
| `Phoney.DependencyInjection` | [`services.AddPhoney()`, `IFaker`, injectable generators](#dependency-injection-and-aspnet-core) | [![NuGet](https://img.shields.io/nuget/v/Phoney.DependencyInjection.svg?label=)](https://www.nuget.org/packages/Phoney.DependencyInjection) |
| `Phoney.EntityFrameworkCore` | [`HasFakeData` seed data](#ef-core-seeding) | [![NuGet](https://img.shields.io/nuget/v/Phoney.EntityFrameworkCore.svg?label=)](https://www.nuget.org/packages/Phoney.EntityFrameworkCore) |
| `Phoney.NodaTime` | [NodaTime values and members](#nodatime) | [![NuGet](https://img.shields.io/nuget/v/Phoney.NodaTime.svg?label=)](https://www.nuget.org/packages/Phoney.NodaTime) |
| `Phoney.WireMock` | [Fake JSON bodies for WireMock.Net mocks](#wiremocknet-mocks) | [![NuGet](https://img.shields.io/nuget/v/Phoney.WireMock.svg?label=)](https://www.nuget.org/packages/Phoney.WireMock) |

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

// A whole, coherent person: names, email, username, phone, birthdate and age, address, national id
PersonProfile p = Fake.Person.Profile();

// A specific locale and a seed
var faker = new Faker("sv", seed: 42);
faker.Person.FullName();                        // "Madeleine Öberg Nordström"
faker.Finance.Iban("SE", formatted: true);      // valid check digits

// Objects
Customer customer = Fake.One<Customer>();
List<Customer> customers = Fake.Many<Customer>(1_000);
List<Customer> someCustomers = Fake.Many<Customer>(3, 10);   // a count from 3 to 10, chosen from the seed
```

## Values

A `Faker` groups values by topic:

| Module | Examples |
|---|---|
| `Person` | `Profile()`, `FirstName(Sex.Female)`, `LastName()`, `FullName()`, `Sex()`, `JobTitle()`, `Bio()`, `Prefix()` |
| `Location` | `Address()`, `StreetAddress()`, `City()`, `ZipCode()`, `State()`, `Country()`, `CountryCode()`, `Latitude()`, `NearbyGpsCoordinate()` |
| `Internet` | `Email()`, `Username()`, `Url()`, `DomainName()`, `Ipv4(IPv4Network.PrivateC)`, `Ipv6()`, `Mac()`, `Password()`, `UserAgent()`, `Jwt()`, `Emoji()`, `Color()` |
| `Phone` | `Number(PhoneStyle.International)`, `Imei()` |
| `Company` / `Commerce` | `Name()`, `CatchPhrase()`, `ProductName()`, `Price()`, `Isbn()`, `Upc()` |
| `Finance` | `Iban()`, `Bic()`, `CreditCardNumber(CardIssuer.Visa)`, `Currency()`, `Amount()`, `RoutingNumber()`, `BitcoinAddress()` |
| `NationalId` | `Person()`, `Company()`, `Vat()`: a Swedish personnummer, a US SSN, a Brazilian CPF… with valid check digits ([more](#national-and-tax-ids)) |
| `Date` | `Past()`, `Future()`, `Recent()`, `Soon()`, `Between(...)`, `Birthdate(18, 65)`, `Month()`, `Weekday()` |
| `Lorem` / `Word` | `Sentence()`, `Paragraphs()`, `Slug()`, `Noun(minLength: 5)` |
| `String` / `Number` | `Uuid()`, `UuidV7()`, `Ulid()`, `NanoId()`, `AlphaNumeric(10)`, `Int(1, 6)`, `Decimal(0, 100, 2)`, `Normal(175, 8)`, and one call per numeric type (`Byte()`, `UShort()`, `Int128()`, `Half()`, `BigInteger(min, max)`…) |
| `Color` | `Human()`, `Hex()`, `Rgb()` (typed, with `ToCss()`/`ToHex()`), `Hsl()`, `Lab()` |
| `Image` | `Url(640, 480)`, `Avatar(Sex.Female)`, `AvatarGitHub()`, `DataUri()` (self-contained SVG) |
| `Helpers` | `FromRegExp("[A-Z]{3}-\\d{4}")`, `Slugify()`, `LuhnCheck()`, `Multiple(f => …, 5)`, `UniqueValues(…, 10)` |
| `Git` | `Branch()`, `CommitMessage()`, `CommitSha()`, `CommitDate()`, `CommitEntry()` (a commit as `git log` prints it) |
| `Airline` | `Airline()`, `Airport()`, `Airplane()`, `FlightNumber()`, `RecordLocator()`, `Seat()` |
| also | `Animal`, `Book`, `Music`, `Food`, `Hacker`, `Vehicle` (with valid VINs), `Science`, `Database`, `System` |

- **Checksums are real**: credit cards and IMEIs pass Luhn, IBANs pass MOD 97, and ISBN, UPC, VIN and ABA routing numbers have valid check digits.
- **`Person.Profile()`** returns a `PersonProfile` whose parts belong together: the names match the sex, email and username come from the names, the age matches the birthdate, and the national id encodes birthdate and sex. `faker.Person.Profile(Sex.Female, minAge: 25, maxAge: 40)` narrows it, and `Location.Address()` gives just an address, as parts.
- **`faker.Random`** is the seeded source of randomness (xoshiro256\*\*) for your own values: `Int`, `Long`, `Double`, `Decimal`, `Bool(0.2)`, `Element(items)`, `Elements(list, 3)`, `Shuffled(items)`, `Weighted(("gold", 1), ("silver", 3))`, `Enum<T>()`, `Guid()`, `ReplaceSymbols("##??")`.
- **Arguments are checked**: a negative length or count, a `NaN` or infinite bound or a negative age throws an argument exception that names the parameter (zero asks for none: `String.Alpha(0)` is `""`). Ranges reach the ends of their types (`Number.Double(double.MinValue, double.MaxValue)`), and relative dates stop at the ends of the calendar.
- **Image URLs** come from replaceable templates, so data doesn't have to point at public placeholder services: `ImageSources.UseHost("https://images.test")`, or `ImageSources.Photo = "https://cdn.example.com/{width}x{height}?s={seed}"`.
- `Fake.*` is a thread-safe static facade over a per-thread `Faker`; a `Faker` itself is not thread-safe.

### Reproducible data

A seed fixes values *and* dates: seeded fakers and generators compute relative dates (past, recent, birthdates, ages) from `Faker.DefaultSeededReferenceDate` (2025-01-01 UTC) unless you set `ReferenceDate`, so seeded output doesn't change from one day to the next.

```csharp
var faker = new Faker("de", seed: 2024);                         // same values and dates on every run
var later = new Faker("de", seed: 2024) { ReferenceDate = DateTimeOffset.UtcNow }; // opt back into "now"

using var _ = Fake.Seeded(42);           // Fake.*, Fake.One/Many/For are reproducible in this scope only, even with tests in parallel
Fake.Seed(42);                           // or seed the facade app-wide (every thread starts the same sequence)

Parallel.For(0, 1_000, i =>
{
    using var fork = Fake.Fork(i);       // a faker of item i's own, from the seed and the index: the same whatever the thread timing
    Save(Fake.Person.FullName(), Fake.One<Order>());
});
```

`ObjectGenerator<T>.Seed(n)` does the same for a generator, and `GenerateParallel` forks for you. A `Fake.Seeded` scope flows into the tasks and workers started inside it, so they would share one faker without `Fake.Fork(i)` (or `faker.Fork(i)`). One exception: `Unique(...)` members combined with `GenerateParallel` depend on thread timing. For tests that run on random data but can be replayed, see [`[FakeSeed]` and `FakeSeed.Run`](#replayable-random-data).

### Controlling "now" with `TimeProvider`

Relative dates can follow a .NET `TimeProvider`, such as `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`, so they move with the clock your tests already control:

```csharp
var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

var faker = new Faker("sv") { TimeProvider = time };            // faker.Date.Past() is before 2030-01-01
var orders = Fake.For<Order>().TimeProvider(time).Generate(10);   // CreatedAt, ages, timelines follow the clock
var rows = Fake.Arguments(method).TimeProvider(time).Generate(5); // argument rows too
Fake.TimeProvider = time;                                         // the static facade, Fake.For/One/Many and Fake.Arguments
time.Advance(TimeSpan.FromDays(30));                              // read on every use, so dates move along
```

The reference date is, in order: an explicit `ReferenceDate`, the `TimeProvider`, `DefaultSeededReferenceDate` for seeded fakers and generators, and otherwise the system clock.

## National and tax IDs

Personal identity numbers, company registration numbers and VAT numbers come with **valid check digits** and use the locale's country unless you pass one:

```csharp
var faker = new Faker("sv", seed: 1);
faker.NationalId.Person();                                   // "19560703-6190" (personnummer)
faker.NationalId.Person("NO", birthDate: new DateOnly(1990, 5, 17), sex: Sex.Female, formatted: false);
faker.NationalId.Company();                                  // "214200-3165" (organisationsnummer)
faker.NationalId.Vat("GB");                                  // "GB 154 4706 67"

NationalIds.IsValid(NationalIdKind.Person, "SE", "19560703-6190");   // true: every scheme also validates
NationalIds.Supported(NationalIdKind.Vat);                           // ["DK", "FI", "FR", "GB", "NO", "PL", "SE"]
NationalIds.Register(new MyEmployeeNumbers());                       // your own, derived from NationalIdScheme
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

- Where the number encodes a birth date and sex (SE, NO, DK, FI, PL, FR) it encodes the ones you pass, so a `PersonProfile` or a generated object has an id that agrees with its `BirthDate` and `Sex`.
- Members named `Personnummer`, `Ssn`, `NationalId`, `Cpf`, `Pesel`… (person), `OrgNr`, `Cnpj`, `Ein`… (company) and `VatNumber` get these values by convention. A locale whose country has no scheme leaves them empty, and `NationalId` throws a `PhoneyDataUnavailableException`, so pass the country there.
- The numbers are structurally valid, not issued: they can coincide with real numbers, so use them for test data only. Danish CPR numbers satisfy the classic mod-11 rule, so old and new validators accept them.

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

Fallback works like faker.js: each data group (`person.first_name`, `location.city_pattern`…) comes from the first locale in the chain that defines it, and data a locale marks as not applicable (name prefixes in Azerbaijani) is never borrowed from another locale.

### Custom data

```csharp
// A new locale on top of an existing one
Locales.Register("sv_dalarna", l => l
    .FallbackTo("sv")                                  // sv → en → base
    .Cities("Mora", "Falun", "Rättvik")
    .FirstNames(Sex.Female, "Kerstin", "Margit")      // male names still come from sv
    .EmailDomains("dalarna.example")
    .PhoneFormats("0250-## ## ##"));

// Extra or replaced data in an existing locale, by typed method or faker.js data path
Locales.Extend("en", l => l
    .CompanyNames("{{Person.LastName}} & Sons", "Acme {{Commerce.Department}}")
    .Set("commerce.department", "Toys", "Garden")
    .Set("person.nickname", "Ace", "Buddy"));          // new paths are allowed: faker.Pick("person.nickname")

Locales.Register("en_PIRATE", File.OpenRead("pirate.json"), "en");   // a faker.js locale file
```

Typed methods cover `FirstNames`, `LastNames`, `JobTitles`, `Cities`, `Streets`, `States`, `Countries`, `ZipCodeFormats`, `PhoneFormats`, `EmailDomains`, `CompanyNames` and `LoremWords`. Each replaces only that data; everything else is inherited.

## Templates

Templates use the same names as the API:

```csharp
faker.Parse("{{Person.FirstName}} ordered {{Commerce.ProductName}} from {{Company.Name}}");
faker.Parse("Ticket {{Helpers.FromRegExp([A-Z]{3}-[0-9]{4})}}, PIN {{String.Numeric(4)}}");
TemplateFunctions.Register("Shop.Sku", (f, _) => f.Random.ReplaceSymbols("SKU-#####"));   // your own functions
```

- **faker.js templates work unchanged**: names are case-insensitive (`{{person.firstName}}`), and locale data paths and record fields (`{{airline.airport.iataCode}}`) work too.
- **Options** are an options object or the plain value: `{{number.int({"min":1,"max":9})}}`, `{{string.alpha({"length":8,"casing":"upper","exclude":["O","I"]})}}`, `{{person.firstName({"sex":"female"})}}`, `{{internet.mac("-")}}`. A wrong value (`"sex":"robot"`) throws a `FormatException` that says what is accepted, and so does a length above 100,000 characters or a count above its limit.
- Typed results (`airline.airport`, `finance.currency`, `color.hsl`…) are written in their usual text form. `TemplateFunctions.Names` lists every function.

## Objects

```csharp
var customers = Fake.For<Customer>()
    .Locale("sv")
    .Seed(42)                                                     // reproducible
    .With(x => x.LoyaltyPoints, f => f.Random.Int(0, 5_000))      // value from a factory
    .With(x => x.Status, CustomerStatus.Active)                    // fixed value
    .With(x => x.Email, (f, x) => f.Internet.Email(x.FirstName, x.LastName, provider: "example.se")) // depends on the object
    .With(x => x.Address, Fake.For<Address>().Locale("de"))        // nested generator
    .Unique(x => x.Email)                                          // never the same email twice
    .Ignore(x => x.InternalNotes)
    .Generate(100);                                                // also: Generate(), Stream(), GenerateParallel(n)
```

- **Supported shapes**: classes, records and structs (the constructor with most parameters is used), `init` and `required` members, public fields, nested objects (closed generic classes such as `Paged<Order>` too), interfaces and abstract classes (through [subtypes](#interfaces-and-subtypes)), arrays, lists, sets, dictionaries and [more collection types](#collection-types), enums, nullable types, `byte[]`, `Memory<byte>`, `T[,]`, tuples, `KeyValuePair<K,V>`, and `Guid`, the date and time types, `Uri`, `IPAddress`, `Version`, `Rune` and every numeric type (`Half`, `Int128`, `BigInteger` and `nint` included).
- **Asked for directly**, a value type, a collection or an interface gets a value too: `Fake.One<Guid>()`, `Fake.Many<int>(10)`, `Fake.One<List<Customer>>()` (1–3 customers), `Fake.For<IShape>().Subtype<IShape, Circle, Square>().Generate()`. `Fake.One(type)` and `Fake.Many(type, count)` take a `Type` known only at runtime.
- **What is left alone**: framework types without a generator keep their default (a struct such as `JsonElement` its zero value, a class such as `Stream`, `Task<T>` or `JsonNode` `null`), as do arrays of more than two dimensions. `CultureInfo` and `TimeZoneInfo` depend on the machine, which would break the promise that a seed gives the same data everywhere; set them with `.With(...)`.
- **Cycles** (`Customer → Order → Customer`) stop with `null`, or point back at the containing object with [`WithBackReferences()`](#back-references). Elements that can't be created are left out of collections. `MaxDepth(n)` (default 4) bounds nesting, `CollectionSize(min, max)` (default 1–3) sizes collections, `NullProbability(p)` (default 0) leaves nullable members null.
- **Uniqueness**: `Unique(x => x.Email, maxAttempts)` keeps a member unique across everything the generator instance creates. A member that comes from the object's identity (names, email, address, birthdate…) is not drawn again on its own: on a duplicate the whole object is generated again, so the email still belongs to the name.
- **Generators are immutable**: every method returns a new generator, so you can share them and derive variants: `var admins = users.With(u => u.Role, "Admin");`.
- **Order**: constructor arguments, then members (base class first; fields, then properties, in declaration order; read-only collections last), then rules that depend on the object (in the order added), then `AfterCreate` actions. `CreateWith(f => new Customer(...))` replaces construction.
- **Strict mode**: `.Strict()` throws when a scalar member has neither a rule, a convention nor a data annotation, so new members can't silently get meaningless data. The analyzer reports the same at compile time (**PHONEY004**) for chains like `Fake.For<T>().Strict()…Generate()`; it can't see app-wide `Fake.Conventions`.

### Values that agree

Within one object, related members describe the same thing:

- **One person**: `FirstName`, `LastName`, `Email`, `Username`, `Avatar` and `Gender`/`Sex` enums belong together, and `Age` matches `BirthDate`/`DateOfBirth`.
- **One address**: `Street`, `City`, `ZipCode`, `State`, `Country` and `CountryCode` come from one address; for country locales (`de_AT`, `en_US`…) the country is the locale's own.
- **One timeline**: the first "created" date (`CreatedAt`, `RegisteredAt`, `StartDate`…) is when the object was created, "updated" dates (`UpdatedAt`, `LastLogin`…) fall between it and the reference date, and future dates (`ExpiresAt`, `DueDate`) come after both.

Across objects, `WithOneOf` links to data you generated already, optionally with weights, and `WithSubsetOf` gives a collection member its own random subset of values:

```csharp
var orders = Fake.For<Order>()
    .WithOneOf(o => o.CustomerId, customers, c => c.Id)                                                        // an existing customer's id
    .WithOneOf(o => o.Status, (OrderStatus.Paid, 70), (OrderStatus.Shipped, 25), (OrderStatus.Cancelled, 5))   // 70 %, 25 %, 5 %
    .WithOneOf(o => o.Country, countries, populations)                                                         // values and weights
    .WithOneOf(o => o.SalesRepId, reps, r => r.Id, r => r.Seniority)                                             // existing objects, weighted
    .Generate(1000);

var users = Fake.For<User>().Seed(1)
    .WithSubsetOf(u => u.Roles, roles, 2)                                // two of the roles, never the same one twice
    .WithSubsetOf(u => u.Tags, tags, (1, 3))                             // one to three; arrays, sets and list interfaces work
    .WithSubsetOf(u => u.Badges, badges, new[] { 80.0, 15, 4, 1 }, 2)   // heavier values are more likely to be in it
    .Generate(100);
```

- **Weights** are relative (70/25/5 is 14/5/1) and a weight of zero is never picked; a negative, non-finite or missing weight throws when the rule is added. A pick is one random number and a binary search.
- **A subset** is made of distinct positions of the list, in list order, with every value equally likely (Floyd's algorithm). With weights, values are taken one at a time by weight among those left, so a subset of one is a plain weighted pick; this costs one pass over the list per value, unnoticeable up to a few thousand values.
- Item *n* is the same however you generate, and the size can't be larger than the values (with a weight above zero).

### Collection types

Besides arrays, `List<T>`, `HashSet<T>`, `Dictionary<K,V>` and their interfaces, members of the immutable, frozen, sorted, concurrent, observable and read-only collections, queues, stacks and `LinkedList<T>` are generated too:

<details>
<summary>All collection types</summary>

| Built like a list | Built like a set | Built like a dictionary |
|---|---|---|
| `ImmutableArray<T>`, `ImmutableList<T>`, `IImmutableList<T>` | `ImmutableHashSet<T>`, `IImmutableSet<T>`, `ImmutableSortedSet<T>` | `ImmutableDictionary<K,V>`, `IImmutableDictionary<K,V>`, `ImmutableSortedDictionary<K,V>` |
| `ReadOnlyCollection<T>`, `Collection<T>`, `ObservableCollection<T>` | `SortedSet<T>` | `SortedDictionary<K,V>`, `SortedList<K,V>` |
| `Queue<T>`, `Stack<T>`, `LinkedList<T>` | `FrozenSet<T>` | `FrozenDictionary<K,V>` |
| `ConcurrentBag<T>`, `ConcurrentQueue<T>`, `ConcurrentStack<T>` | | `ConcurrentDictionary<K,V>`, `ReadOnlyDictionary<K,V>` |
| `ImmutableQueue<T>`, `IImmutableQueue<T>`, `ImmutableStack<T>`, `IImmutableStack<T>` | | |

</details>

Each is built exactly like its `List<T>`, `HashSet<T>` or `Dictionary<K,V>` counterpart and converted, so a seed gives the same elements whichever type holds them, and sizes, annotations, rules and `Populate` work the same.

**Read-only collections are filled in place.** A collection with a getter only (`public List<Line> Lines { get; } = [];`), a private setter or a `readonly` field gets its generated elements added to the collection the object already holds:

- Works for the collections that can be added to (`List<T>`, `IList<T>`, `ICollection<T>`, `HashSet<T>`, `ISet<T>`, `Collection<T>`, `ObservableCollection<T>`, `SortedSet<T>`, `LinkedList<T>`, `Dictionary<K,V>`, `IDictionary<K,V>`, `SortedDictionary<K,V>`, `SortedList<K,V>`, `ConcurrentDictionary<K,V>`); others, a property that holds `null` and a collection that is read-only at runtime are left alone.
- Rules work as for any member, but `With(x => x.Lines, list)` **adds** the elements, and a rule that depends on the object is rejected because there is nothing to set.
- They are drawn **after all other members**, so existing seeds are unchanged. `Populate` fills the empty ones; `PopulateMode.All` replaces their elements.

### Rules for nested members

A rule can name a member of a nested object with a path, so you don't need a generator for the whole object just to change one value:

```csharp
var invoices = Fake.For<Invoice>()
    .With(i => i.Billing.City, "Oslo")                          // this address only: Shipping is another position
    .With(i => i.Billing.Contact.Phone, f => f.Random.ReplaceSymbols("+47 ## ## ## ##"))
    .With(i => i.Lines.Each().Sku, f => f.Commerce.Ean13())     // every element of a list, array or set
    .With(i => i.ByCode.Values.Each().Quantity, 1)              // every value of a dictionary
    .Ignore(i => i.Lines.Each().Supplier!.Email)
    .Invalid(i => i.Lines.Each().Quantity)                      // see "Invalid and boundary data"
    .Generate(10);
```

- **Which rules**: `With`, `WithOneOf`, `WithSubsetOf`, `WithNormalDistribution`, `Ignore`, `Invalid` and `Boundary`. `Unique` and rules that depend on the object belong to the generated object, so a path throws an `ArgumentException` (and the analyzer reports **PHONEY007**).
- **A position, not a type**: `Billing.City` and `Shipping.City` are both addresses, and only the first is changed. For every object of a type, use [`WithGeneratorFor`](#type-wide-generators). `Each()` is only a marker for "every element" and can't be called; `x.Lines[0]` is rejected.
- **Checked when the generator is first used**: a member that doesn't exist or can't be set throws an `InvalidOperationException` that names the path. Below an interface or abstract class the picked subtype decides.
- **Precedence**: a rule for the member itself (`With(i => i.Billing, generator)`), `WithGeneratorFor` and `Fake.Generators` create the whole object with their own rules, so path rules inside it don't apply. `Populate` uses them for the nested objects it creates.

### Interfaces and subtypes

Members declared as an interface, an abstract class or a base class are generated as one of their subtypes:

```csharp
var drawings = Fake.For<Drawing>()
    .Subtype<IShape, Circle>()               // every IShape in the object graph is a Circle
    .Subtype<IAnimal, Cat, Dog>()            // one of Cat or Dog, picked at random (reproducible with a seed)
    .With(d => d.Logo, Fake.For<Square>())   // one member with its own generator of a subtype
    .Generate(10);

Fake.Subtypes.Add<IShape, Circle, Square>(); // app-wide, for generators created afterwards (also Remove<IShape>() and Clear())
```

- **`[JsonDerivedType]` works out of the box**: the derived types listed for System.Text.Json are the default subtypes.
- **Precedence**: the generator's `Subtype`, then `Fake.Subtypes`, then `[JsonDerivedType]`. Without any, an interface or abstract member stays `null` (and is left out of collections), and `Fake.One<IShape>()` throws with how to register one. A concrete base class is generated as itself unless you map it (`.Subtype<Vehicle, Truck>()`).
- Up to four subtypes fit the type arguments; pass more as types: `.Subtype<IShape>(typeof(Circle), typeof(Square), …)`. `Populate` fills an existing object as its runtime type.
- With the source generator, `[FakeSubtype<IShape, Circle>]` registers the subtype app-wide and generates its model. In Native AOT apps every subtype needs a generated model.

### Type-wide generators

`WithGeneratorFor` configures a **type**: every nested object of that type in the graph, in a member, a collection or a dictionary, at any depth, comes from the generator you give it:

```csharp
var orders = Fake.For<Order>()
    .WithGeneratorFor(Fake.For<Address>().Locale("de").With(a => a.Country, "Germany"))   // Order.Billing, Order.Shipping, Customer.Home…
    .WithGeneratorFor<IPayment, Card>(Fake.For<Card>().With(c => c.Network, "Visa"))       // an interface, from a generator of a subtype
    .Generate(100);

Fake.Generators.Add(Fake.For<Address>().Locale("sv"));   // app-wide, also for [FakeData] parameters (Remove<T>(), Clear())
```

- **Precedence**: a rule for the member wins over the type's generator, which wins over subtypes and the type's model. A generator's own `WithGeneratorFor` wins over `Fake.Generators`, and the innermost registration wins inside a nested generator's graph.
- **Self-contained**: the generator brings its own rules, locale, conventions and `Unique` sets, and inherits the reference date. The root object always comes from the generator you call `Generate` on.
- Only objects can be registered; scalars come from rules and conventions, so `WithGeneratorFor<string>` throws and points to `WithConvention`. `Fake.ForGenerated<Address>()` gives the AOT-safe generator to register.

### Back-references

With navigation properties in both directions (`Order.Lines`, `OrderLine.Order`) a cycle stops with `null` by default. `WithBackReferences()` closes it, like an ORM would:

```csharp
var orders = Fake.For<Order>().WithBackReferences().Generate(10);
orders[0].Lines[0].Order;   // the very same order (ReferenceEquals), not null
```

- Every member still `null` after creation, whose type an object above it satisfies, gets the **nearest** such object, through settable members, collections and dictionary values. Members that have a value are kept, and constructor-only members are left alone.
- Nothing is drawn, so **the rest of the data is exactly that of the same seed without it**. `Where` and `AfterCreate` see the finished graph, and `Populate` fills the missing references of an existing graph.
- The graph has real cycles afterwards: record `ToString`/`Equals` and serializers without cycle handling (`ReferenceHandler.Preserve`) may not cope.

### Conditions with `Where`

`Where` keeps only the objects that satisfy a condition, and `Where(x => x.Member, condition)` draws just that member again, which is cheaper:

```csharp
var bigOrders = Fake.For<Order>().Seed(1)
    .Where(o => o.Total > 100)                                   // the whole object is generated again until it holds
    .Where(o => o.Status != OrderStatus.Cancelled)               // several conditions must all hold
    .Where(o => o.Reference, r => !r.StartsWith("TEST"))         // only Reference is drawn again
    .Generate(50);
```

- **Same seed, same data**: an object that already satisfies the condition is the one you get without it, and retries draw from a seed derived from the seed, the position and the attempt, so item *n* stays the same however you generate.
- **The object condition sees the finished object** (dependent rules and `AfterCreate` have run). It is meant for conditions most objects meet: after `maxAttempts` (default 1000) it throws and suggests setting the members with `With(...)`. Rejected objects have used up their `Unique` values.
- **Member conditions** apply to the generated object's own members (not paths). A member that is ignored, set from the object or created by `CreateWith` has nothing to draw again, and identity members (names, email, address, birthdate, national id…) have one value per object, so both throw: filter the object instead.
- Nested generators filter what they create. `Populate` supports object conditions with `PopulateMode.All` only; `InvalidCases()` and `BoundaryCases()` ignore conditions.

### Checking that rules apply

A rule that names a position in the graph can silently do nothing: `.With(x => x.Lines.Each().Sku, "A")` when the lists are empty, a path below a member that another rule replaced, or a `Subtype` for an interface nothing uses. `VerifyRules()` turns that into a failure:

```csharp
var invoices = Fake.For<Invoice>().Seed(1)
    .With(i => i.Billing.City, "Oslo")
    .Subtype<IPayment, Card>()
    .WithConvention<string>(n => n.EndsWith("Code"), f => f.Random.ReplaceSymbols("??-###"))
    .VerifyRules()
    .Generate(20);      // InvalidOperationException listing every rule that applied to none of the 20 invoices
```

- **Checked**: path rules, `Subtype`, `WithGeneratorFor` and `WithConvention`, the rules whose reach depends on the graph. A rule counts once it applied to any object of this generator instance, over all its calls.
- **When generation ends**: after `Generate`, `GenerateParallel` and `Populate` (not for zero objects). `Stream()` never ends, so it throws `NotSupportedException`, and a verifying generator can't be nested (call `VerifyRules(false)` on it).

### Normally distributed numbers

Measurements, scores, ages and durations cluster around an average. `WithNormalDistribution` gives a number member that shape:

```csharp
var people = Fake.For<Person>().Seed(1)
    .WithNormalDistribution(p => p.Height, mean: 175, standardDeviation: 8)             // 175.34, 168.9, 181.72…
    .WithNormalDistribution(p => p.Iq, 100, 15)                                         // whole numbers for int members
    .WithNormalDistribution(p => p.Lines.Each().Pulse, 70, 8, min: 40, max: 120)        // a value outside the limits is drawn again
    .Generate(1000);

var iq = faker.Number.Normal<int>(100, 15);          // also Normal(), faker.Random.Normal() and {{number.normal(50, 5)}}
```

- **Any numeric type** and its nullable form. Whole-number types are rounded, others get two decimals (`decimals: 0` to 15), and a value the type can't hold saturates, so give limits for `byte` near 255.
- **Limits cut the bell off, they don't flatten it**: values outside are drawn again, and after 100 misses the nearest limit is returned. `[Range]` is not read, because rules win over annotations.
- **Reproducible everywhere**: two draws per value and no state, and the Box–Muller logarithm and cosine use `+ - * /` only, so a seed gives the same numbers on every platform (a test pins exact values).

### Numbering and cycling

```csharp
var tickets = Fake.For<Ticket>().Seed(1)
    .Sequence(t => t.Code, i => $"T{i + 1:D4}")                       // T0001, T0002, T0003…
    .Cycle(t => t.Queue, ["north", "south", "east"])                  // north, south, east, north…
    .Generate(100);
```

The value depends on the object's position: the first object is 0 and the numbers continue over calls, whatever the seed, the batch size, `GenerateParallel` or `Stream()`, and a `Where` retry keeps its position. Only members of the generated object take these rules, and a generator with them can't be nested (every nested object would be 0). The object's own `Id` already counts 1, 2, 3… by [convention](#conventions).

### Filling in existing objects

`Populate` fills in an object you already have, with the same conventions, rules and annotations as `Generate`:

```csharp
var order = Fake.Populate(new Order { CustomerId = 42 });         // CustomerId stays 42, everything else is filled
generator.Populate(existing, PopulateMode.All);                   // regenerate every member that isn't ignored
List<Customer> filled = generator.Populate(customers);            // many at once, each from its own place in the seeded sequence
```

- **`MissingOnly`** (the default) fills members that hold their default (`null`, `0`, `false`, `Guid.Empty`, an empty string or collection), keeps the others and populates set nested objects member by member. A deliberate `false` or `0` counts as unset: protect it with `.Ignore(...)`.
- Kept values shape the generated ones: with a set `FirstName` or `BirthDate`, the email, username and age match them.
- Dependent rules and `AfterCreate` run afterwards, `CreateWith` is ignored, records are filled through `init`, and a struct is populated as a copy (use the return value). With a seed, `Populate(new T(), PopulateMode.All)` gives exactly what `Generate()` gives.

### Data tied to a key

The same key gives the same values in every run, process and machine, whatever was generated before and whether or not anything is seeded. That makes stable stand-ins for real data, for example to copy production data to a test system without the personal details while joins still work:

```csharp
string name = Fake.Keyed("customer:42").Person.FullName();     // always the same name for customer 42
Faker faker = new Faker("sv").Keyed(user.Email);                // from any faker
Customer c = Fake.For<Customer>().GenerateFor("customer:42");   // the whole object for a key

List<Customer> masked = Fake.For<Customer>()
    .Ignore(x => x.Id)                                           // keep what isn't personal
    .Pseudonymize(customers, x => x.Email);                      // the same real email gets the same fake person in every table
```

- `Pseudonymize` reads the key, then regenerates every member that isn't ignored, so the stand-in's names, email, address and birthdate agree.
- The key becomes a seed with `SeededRandom.KeySeed` (FNV-1a and SplitMix64, never the per-process `string.GetHashCode`), mixed with the generator's `Seed` when there is one; keyed dates are relative to `DefaultSeededReferenceDate`.
- **Reproducible, not secret**: anyone who knows a key can compute its stand-in. When it must not lead back, key on a keyed hash with a secret kept out of the data (`HMACSHA256.HashData(secret, …)`).
- `Unique` can't be combined with keys (a key that comes again must give the same object), and a keyed object is position 0 for `Sequence` and `Cycle`.

### Data from CSV and JSON

`WithFeed` fills members from the rows of a CSV or JSON file, for data you want to control (a product catalogue, real-looking test accounts) while the rest is generated. The generator can also write what it generates:

```csharp
var feed = DataFeed.FromCsvFile("customers.csv");        // also FromCsv(text), FromJson(text), FromJsonFile(path)
var customers = Fake.For<Customer>().WithFeed(feed).Generate(100);                         // rows 1, 2, 3… and round again
var sample = Fake.For<Customer>().Seed(7).WithFeed(feed, FeedOrder.Random).Generate(100);  // a reproducible random row each

Fake.For<Customer>().Seed(1).WriteCsvFile("customers.csv", 500);
string json = Fake.For<Customer>().ToJson(10);                   // also WriteJsonFile(path, n), WriteJson(stream, n)
string csv = Fake.For<Customer>().ToCsv(100, delimiter: ';', columns: ["Id", "Email", "Country"]);
```

- **One row per object**, picked by position (or by seed and position), so item *n* is the same however you generate. **Columns fill members by name**, ignoring case, spaces and underscores (`first_name` fills `FirstName`), constructor parameters included, converted with the invariant culture. A `DateTimeOffset` without an offset is UTC and a `DateTime` with one becomes UTC, so a feed reads the same on every machine; an enum takes a name or the number of a defined value.
- **Strict about mistakes**: an unmatched column (unless `ignoreUnknownColumns: true`), a value that doesn't fit (the message names the row, column, value and type), ragged rows, duplicate columns and unterminated quotes throw when the feed is read or first used, not mid-run.
- **Precedence**: a rule for the member wins over the feed, and the feed over conventions. Members without a column are generated independently of the row, so add the columns that must agree. A generator with a feed can't be nested.
- **Writing** gives one column per readable scalar member (nested objects and collections are left out) in UTF-8, with invariant, round-trip formats, so `DataFeed.FromCsvFile` reads the values back. CSV follows RFC 4180, and JSON is an array of flat objects.

### Validation attributes

Generated objects satisfy their data annotations, so they pass `Validator.TryValidateObject`, EF Core and ASP.NET Core validation. Annotations work on properties, fields and record parameters (`[property: Range(1, 5)]`).

| Annotation | Effect |
|---|---|
| `[StringLength]`, `[MinLength]`, `[MaxLength]`, `[Length]` | string length; element count for collections; length of `byte[]`. When several apply, the strictest limits win; an email that has to be shortened or lengthened stays an email |
| `[Range]` | numbers, dates, times of day and durations within the range (`[Range(typeof(DateTime), "2020-01-01", "2020-12-31")]`, `[Range(typeof(TimeOnly), "09:00", "17:00")]`), and numbers as text for a string or as a character for a `char` (`[Range('a', 'f')]`); `MinimumIsExclusive`/`MaximumIsExclusive` leave the limit out, a decimal range narrower than 0.01 gets more decimals, and a range with no value of the member's type inside throws when generating |
| `[RegularExpression]` | a value matching the pattern: classes, groups (also named), alternation, quantifiers, anchors and `\p{L}`/`\p{Lu}`/`\p{Ll}`/`\p{N}`; with lookarounds (`(?=.*\d)`) values are generated until one matches. A pattern Phoney can't generate (backreferences, other escapes) throws with the pattern in the message |
| `[EmailAddress]`, `[Phone]`, `[Url]`, `[CreditCard]`, `[Base64String]` | a value of that format, whatever the member is called, also with `WithNameConventions(false)` |
| `[AllowedValues]`, `[DeniedValues]` | one of / none of the listed values (for enums also their names); when the denied values leave nothing to pick, generating throws |
| `[Required]` | never `null` (even with `NullProbability`) and never empty |

### Invalid and boundary data

The annotations that make objects valid also tell Phoney how to break them, so you can test that validation rejects what it should and accepts what sits right on the edge:

```csharp
var order = Fake.For<Order>().Invalid(o => o.Quantity).Generate();            // 0 or 11 for [Range(1, 10)], everything else valid
var edge = Fake.For<Order>().Boundary(o => o.Quantity).Generate();            // 1 or 10

foreach (var c in Fake.For<Order>().Seed(1).InvalidCases())                   // every violation of every annotated member
    validator.Validate(c.Value).IsValid.ShouldBeFalse(c.ToString());          // "Quantity: above the maximum (10)"

foreach (var c in Fake.For<Order>().Seed(1).BoundaryCases())                  // every value exactly on a limit, all valid
    validator.Validate(c.Value).IsValid.ShouldBeTrue(c.ToString());           // "Name: the maximum length (50)"
```

| Annotation | `Invalid` gives | `Boundary` gives |
|---|---|---|
| `[Required]` | `null`, and `""` for strings | strings: at least one character |
| `[StringLength]`, `[MinLength]`, `[MaxLength]`, `[Length]` | one character or element too few / too many | exactly the minimum / maximum |
| `[Range]` on numbers and dates | one unit (a day for dates) below the minimum / above the maximum, or the limit itself when it is exclusive | the minimum / the maximum, or the nearest value inside an exclusive limit |
| `[RegularExpression]`, `[EmailAddress]`, `[Phone]`, `[Url]`, `[CreditCard]`, `[Base64String]` | text the real validator rejects | none: they define a value, not an edge |
| `[AllowedValues]`, `[DeniedValues]` | a value that is not allowed / a denied one | none |

- **One change per case**: a case is the object `Generate()` gives for the seed with only that member replaced. Every case can be a test case of its own, named after what it is, through your framework's data source (`MethodDataSource` in TUnit, `MemberData` in xUnit…).
- `Invalid(member)` picks one violation per object, reproducibly, so `Generate(50)` covers them all. A member without annotations to break throws and lists the supported ones; limits beyond 100,000 are not built.

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

`Name` and `Title` look at the declaring type (`Company.Name` is a company name, `Book.Title` a title). Anything else gets a value based on its type. Add your own:

```csharp
Fake.Conventions.Add<string>(name => name.EndsWith("Sku"), f => f.Random.ReplaceSymbols("SKU-#####")); // app-wide
Fake.For<Order>().WithConvention<decimal>(n => n == "Vat", f => 0.25m);                          // one generator
Fake.For<Order>().WithEveryMemberOfType<DateOnly>(f => f.Date.Past().Date);                     // every DateOnly member, whatever its name
```

Conventions apply to every object in the graph, so `Order.Lines[0].Sku` is covered as well as `Order.Sku`. Explicit rules come first, then the generator's conventions, then the app-wide ones. `WithEveryMemberOfType` matches the nullable form too, doesn't apply annotations, and counts as coverage for `.Strict()`.

## Source generator and Native AOT

`Fake.For<T>()` uses reflection (with compiled delegates) unless a generated model exists. Declare the types you generate on a partial class:

```csharp
[FakeFor<Customer>, FakeFor<Order>]
internal static partial class TestData;

var customers = TestData.Customer.Seed(1).Generate(100);                         // an ObjectGenerator<Customer>, no reflection
var customer = Fake.ForGenerated<Customer>().Populate(new Customer { Id = 42 }); // the Fake facade, trim and AOT safe
```

- The generator writes a model for each listed type and every type it contains and registers them at startup, so `Fake.For<Customer>()` uses them too. **Generated and reflection models produce identical data for the same seed.**
- `Fake.For`, `One`, `Many` and `Populate` fall back to reflection and are annotated (`RequiresUnreferencedCode`/`RequiresDynamicCode`), so the compiler flags them in trimmed and AOT apps. **`Fake.ForGenerated<T>()`** is the same facade without reflection, and throws with the fix when the type has no model.
- Subtypes need models too: `[JsonDerivedType]` types get them automatically, and `[FakeSubtype<IShape, Circle>]` generates one and registers the subtype. NodaTime members are generated without reflection when `Phoney.NodaTime` is referenced.

<details>
<summary>Diagnostics</summary>

| Id | Severity | Meaning |
|---|---|---|
| PHONEY001 | Error | the class with `[FakeFor]` must be `partial` |
| PHONEY002 | Warning | a type can't be generated (abstract, no public constructor, inaccessible); its members stay default |
| PHONEY003 | Error | `[FakeFor]` is inside a generic type |
| PHONEY004 | Warning | a `.Strict()` generator has scalar members without a rule, convention or annotation |
| PHONEY005 | Warning | the result of a generator method is discarded (`gen.With(...);`: generators are immutable), or of `Populate` on a struct (it returns the populated copy) |
| PHONEY006 | Warning | `Fake.Seeded(...)` is never disposed, so everything after it stays seeded; use `using var _ = Fake.Seeded(1);` |
| PHONEY007 | Warning | a rule would throw when the generator is first used: the member isn't a public settable member or constructor parameter, the selector isn't `x => x.Member`, or an object-dependent rule targets a constructor parameter |
| PHONEY008 | Warning | a rule replaces an earlier rule for the same member in the same chain, or a `WithGeneratorFor` generator / `Subtype` mapping replaces an earlier one for the same type in the same chain |
| PHONEY009 | Error | `[FakeSubtype<TBase, TSubtype>]` names an abstract type or interface, which can't be created; the registration is left out |
| PHONEY010 | Warning | a new generator with a constant `.Seed(...)` is created inside a loop, so every iteration generates the same objects |
| PHONEY011 | Warning | a constant argument is out of range and the call throws (`NullProbability(1.5)`, `CollectionSize(5, 2)`, `Generate(-1)`, `Unique(x => x.Id, 0)`, `Where(x => …, 0)`…) |
| PHONEY012 | Info | `Fake.For<T>()`, `One`, `Many` or `Populate` generate a type with reflection because no `[FakeFor<T>]` covers it |

The IDE fixes **PHONEY001** (make the class and its containing types `partial`) and **PHONEY012** (add `[FakeFor<T>]` to the class that already lists types, or create one). PHONEY012 is only a suggestion, since reflection is fine unless the app is trimmed or AOT compiled. The advisory rules (PHONEY004–008 and 010–012) are in the category `Phoney.Usage`, and the generator's (001–003 and 009) in `Phoney`, so you can tone down all advisory rules at once:

```ini
[*.cs]
dotnet_analyzer_diagnostic.category-Phoney.Usage.severity = suggestion
```

</details>

## Test data in xUnit, NUnit, MSTest and TUnit

`Phoney.Xunit` ([xUnit.net](https://xunit.net) v3), `Phoney.NUnit`, `Phoney.MSTest` and `Phoney.TUnit` ([TUnit](https://tunit.dev)) add `[FakeData]`, which fills test parameters the way Phoney fills the members of an object. They share their options, seeds and rules, so a test gets the same values whichever framework runs it.

```bash
dotnet add package Phoney.Xunit     # or Phoney.NUnit, Phoney.MSTest, Phoney.TUnit
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

// TUnit
[Test, FakeData]
public async Task Places_order(Customer customer, string email, [Range(1, 10)] int quantity) { … }
```

- Parameters are filled by name and type, objects and collections recursively, data annotations are honoured, and the parameters of one test case agree like the members of one object: `firstName`, `email`, `birthDate` and `age` describe one person, `street`, `city` and `zipCode` one address.
- **Reproducible by default**: the seed is derived from the test's class, method and parameters, so every run and machine gets the same values and test names. Options: `Count`, `Seed`, `Locale`, `NullProbability` and `ReferenceDate`.
- **`PHONEY_SEED=123 dotnet test`** moves the seed of every `[FakeData]` test at once, to run the suite on other data; the same value replays it, and an explicit `Seed` stays as it is.
- Interface and abstract parameters get a subtype from `Fake.Subtypes` or `[JsonDerivedType]`. Test data is created while the framework discovers tests, so register conventions and subtypes before that: in a module initializer, or in TUnit a `[Before(TestDiscovery)]` hook.
- xUnit, NUnit and MSTest fill method parameters; TUnit also fills constructor parameters and `required` properties.
- Supported versions: **xUnit.net v3 3.0** and later (3.x and 4.x), **NUnit 4.0** and later (4.x and 5.x), **MSTest 4.0** and later (4.0 changed the data source interface, so 3.x is not supported) and **TUnit 1.10** and later.

### Replayable random data

Run a test on random data and, **when it fails, get the seed that replays it**. TUnit has an attribute, `[FakeSeed]`; the other frameworks wrap the body in `FakeSeed.Run` (or `RunAsync`), which gives your sink a line with the seed before it rethrows:

```csharp
// TUnit
[Test, FakeSeed]
public async Task Totals_add_up()
{
    var orders = Fake.Many<Order>(100);                       // different on every run
    await Assert.That(Totals.Of(orders)).IsEqualTo(orders.Sum(o => o.Total));
}

// xUnit.net v3
[Fact]
public void Totals_add_up() => FakeSeed.Run(() =>
{
    var orders = Fake.Many<Order>(100);
    Totals.Of(orders).ShouldBe(orders.Sum(o => o.Total));
}, TestContext.Current.SendDiagnosticMessage);

// NUnit:  FakeSeed.Run(() => { ... }, TestContext.Out.WriteLine);
// MSTest: FakeSeed.Run(() => { ... }, TestContext.WriteLine);       // the TestContext property of the class
// async:  await FakeSeed.RunAsync(async () => { ... }, sink);
// Phoney: this test ran with random seed 481516234. Replay it with [FakeSeed(481516234)] or PHONEY_SEED=481516234.
```

- **Replay** one test with `[FakeSeed(481516234)]` or `FakeSeed.Run(..., seed: 481516234)`, or a whole run with `PHONEY_SEED=481516234 dotnet test`. A pinned seed wins over `PHONEY_SEED`, and a `PHONEY_SEED` that isn't a whole number is reported.
- Only the `Fake` facade of the test is seeded, not generators created earlier (a `static` field) or with their own `.Seed(...)`. Parallel tests don't affect each other.
- `[FakeSeed]` on a method wins over the class, and on a class over the assembly (`[assembly: FakeSeed]`). It works with other test executors (`[TestExecutor<T>]`), and skipped tests print nothing. `FakeSeed.Run` reports every exception, so a test that skips by throwing is reported too.
- Other frameworks can build on the same machinery with `Fake.Arguments(method)`, which returns rows of values for a parameter list.

## Dependency injection and ASP.NET Core

The `Phoney.DependencyInjection` package registers fakers and generators in `IServiceCollection`:

```csharp
builder.Services.AddPhoney(o => { o.Locale = "sv"; o.Seed = 42; })
    .AddGenerator<Customer>(g => g.With(c => c.Country, "SE"))  // singleton ObjectGenerator<Customer>
    .AddGenerator(TestData.Order);                              // source-generated: no reflection, AOT safe

app.MapGet("/demo/customers", (ObjectGenerator<Customer> customers) => customers.Generate(10));
app.MapGet("/demo/name", (IFaker faker) => faker.Person.FullName());
```

- `IFaker`/`Faker` are scoped, `IFakerFactory` creates fakers for other locales and seeds, and generators are singletons. `IFaker` itself is in the core package, for your own abstractions and test doubles.
- `PhoneyOptions`: `Locale`, `Seed` (every scope starts at the seed, so each request or test scope is reproducible), `ReferenceDate` and `TimeProvider` (also taken from the container, such as a `FakeTimeProvider`).
- In integration tests, call `AddPhoney(o => o.Seed = 1)` again in `WebApplicationFactory.ConfigureTestServices`; it configures the same options.

## EF Core seeding

The `Phoney.EntityFrameworkCore` package turns generated data into `HasData` seed data:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Customer>().HasFakeData(50);
    modelBuilder.Entity<Order>().HasFakeData(200, g => g.With(o => o.Status, OrderStatus.Paid));
}
```

- The data is the same on every model build (the seed is derived from the entity name), so migrations stay stable.
- Navigations and owned objects are left empty, strings respect `HasMaxLength`, a single integer key is numbered 1…n when values clash, and foreign keys point at seeded principals (seed principals first, or link with `WithOneOf`).
- Call it after the entity's other configuration, since it reads keys, maximum lengths and navigations from the model.

## NodaTime

The `Phoney.NodaTime` package adds NodaTime values and members:

```csharp
Instant created = faker.Noda().Instant.Past();
LocalDate birthday = faker.Noda().LocalDate.Birthdate();
Interval stay = faker.Noda().Interval();       // also DateInterval, YearMonth, AnnualDate, Offset
var subscriptions = Fake.Many<Subscription>(10); // Instant CreatedAt, LocalDate BirthDate… follow the conventions
```

`Instant`, `OffsetDateTime`, `ZonedDateTime`, `LocalDateTime`, `LocalDate`, `LocalTime` and `Duration` members are generated like their built-in counterparts, so `CreatedAt`, `ExpiresAt`, `BirthDate` and `Age` agree, with reflection and with source-generated models. See the [package README](src/Phoney.NodaTime/README.md).

## WireMock.Net mocks

The `Phoney.WireMock` package answers WireMock.Net requests with fake JSON that belongs to the URL:

```csharp
server.Given(Request.Create().WithPath("/customers/*").UsingGet())
      .RespondWith(Response.Create().WithFakeJson(Fake.For<Customer>()));       // /customers/42 is always the same customer
server.Given(Request.Create().WithPath("/customers").UsingGet())
      .RespondWith(Response.Create().WithFakeJsonList(Fake.For<Customer>(), 5, 20)); // a stable 5 to 20 per URL
```

Nothing is stored: each body is `GenerateFor` of the request's key (the path and query by default, or your own, such as the id), so your assertions can compute the expected object. Rules and seeds apply, and a `JsonTypeInfo<T>` overload keeps it reflection free. See the [package README](src/Phoney.WireMock/README.md).

## Performance

Locale data is compiled ahead of time into compact Brotli-compressed resources (about 1 MB for all locales) and loaded lazily: opening a locale only indexes it, and each entry is decoded the first time it is read. Fallback chains are resolved once into a flat array, so a lookup is one array index and picking a name returns a stored string without allocating. Templates are parsed once and cached.

Indicative numbers (Intel Xeon 2.1 GHz with 4 cores, Linux, .NET 10.0.13, BenchmarkDotNet short run):

| Operation | Time | Allocated |
|---|---:|---:|
| `SeededRandom.Int(0, 1000)` | 1.0 ns | 0 B |
| `Person.FirstName()` | 4.8 ns | 0 B |
| `Helpers.FromRegExp("[A-Z]{3}-\\d{4}")` | 90 ns | 144 B |
| `Person.FullName()` | 138 ns | 57 B |
| `Parse("{{person.firstName}} {{person.lastName}} lives in {{location.city}}")` | 228 ns | 98 B |
| `Finance.Iban()` | 457 ns | 388 B |
| 1,000 customers with address and 1–3 orders | 1.9 ms | 1.3 MB |
| … with `GenerateParallel` | 0.75 ms | 1.3 MB |
| Opening the `en` locale (once per process) | 2.7 ms | 780 KB |

```bash
dotnet run -c Release --project benchmarks/Phoney.Benchmarks -- --filter '*'
```

### Compared with other libraries

The same scenarios against the best-known .NET libraries, every one seeded: [Bogus](https://github.com/bchavez/Bogus) 35.6.5, [AutoBogus](https://github.com/nickdodd79/AutoBogus) 2.13.1, [AutoFixture](https://github.com/AutoFixture/AutoFixture) 4.18.1, [NBuilder](https://github.com/nbuilder/nbuilder) 6.1.0 and [Faker.Net](https://github.com/slowmonkey/Faker.NET) 2.0.163. Each cell is the mean time and the memory allocated per operation (BenchmarkDotNet 0.15.8, 15 iterations after 5 warm-up iterations, .NET 10.0.13, Intel Xeon 2.1 GHz with 4 cores, Linux). The fastest time in each row is bold, and "–" means the library has no such feature.

| Scenario | Phoney | Phoney (reflection) | Bogus | AutoBogus | AutoFixture | NBuilder | Faker.Net |
|---|---:|---:|---:|---:|---:|---:|---:|
| First name | **5.0 ns / 0 B** | – | 87.0 ns / 37 B | – | – | – | 58.4 µs / 139 KB |
| Full name | **137 ns / 57 B** | – | 207 ns / 127 B | – | – | – | 84.4 µs / 194 KB |
| Email | **176 ns / 73 B** | – | 1.56 µs / 1.5 KB | – | – | – | 91.2 µs / 196 KB |
| Street address | **159 ns / 61 B** | – | 2.92 µs / 4.4 KB | – | – | – | 67.7 µs / 91 KB |
| Sentence | **303 ns / 481 B** | – | 890 ns / 847 B | – | – | – | 45.0 µs / 77 KB |
| Flat objects (×1,000) | 1.41 ms / 731 KB | **1.38 ms / 833 KB** | 10.7 ms / 8.9 MB | 366 ms / 648 MB | 85.4 ms / 49 MB | 3.59 ms / 3.8 MB | – |
| Object graphs (×1,000) | 5.80 ms / 5.4 MB | **5.55 ms / 5.4 MB** | 18.2 ms / 14 MB | 2.02 s / 3.32 GB | 318 ms / 204 MB | 25.0 ms / 19 MB | – |
| **Data it makes** | realistic, and the parts of an object agree (the email matches the name) | same | realistic, with a rule written by hand for every member | random unrelated words and numbers | the member name and a GUID | the member name and a counter | realistic values, but every call is independent |

"Phoney" is the source-generated model (`[FakeFor<T>]`) and "Phoney (reflection)" the reflection model; the value rows have one column because they don't involve a model. A flat object has 11 members; a graph is a customer with an address and two purchases of two lines each, plus an enum.

- **The libraries don't produce the same data.** Only Phoney and Bogus make realistic values that agree with each other, and Bogus needs a rule written by hand for every member (the nested `Faker<T>`s composed by hand), where Phoney gets the same shape from name conventions. Run `check` (below) to see what each library makes.
- **NBuilder has no object-graph support**, so its graph is composed by hand with `.All().Do(...)`. **Faker.Net only offers single values**, one static call each, which is why it has no object rows.

<details>
<summary>Native AOT and start-up</summary>

The same scenarios built and run with Native AOT (ILCompiler of .NET 10.0.13, instruction set x86-64-v3). Before a library is timed, a check makes sure it really makes what is timed: "fails" means it throws, or for a graph makes incomplete objects. The reflection model of Phoney isn't available with Native AOT (it is marked `RequiresDynamicCode`).

| Scenario | Phoney | Phoney (reflection) | Bogus | AutoBogus | AutoFixture | NBuilder | Faker.Net |
|---|---:|---:|---:|---:|---:|---:|---:|
| First name | **13.1 ns / 0 B** | – | 100 ns / 37 B | – | – | – | 57.4 µs / 139 KB |
| Full name | **184 ns / 57 B** | – | 241 ns / 127 B | – | – | – | 85.4 µs / 193 KB |
| Email | **229 ns / 73 B** | – | 1.87 µs / 1.5 KB | – | – | – | 92.1 µs / 196 KB |
| Street address | **190 ns / 61 B** | – | 5.79 µs / 4.7 KB | – | – | – | 50.6 µs / 92 KB |
| Sentence | **283 ns / 481 B** | – | 1.25 µs / 831 B | – | – | – | 43.8 µs / 77 KB |
| Flat objects (×1,000) | **1.67 ms / 731 KB** | fails | fails | 1.01 s / 864 MB | 396 ms / 130 MB | 9.76 ms / 8.8 MB | – |
| Object graphs (×1,000) | **6.16 ms / 5.4 MB** | fails | fails | fails | fails | 38.2 ms / 28 MB | – |

Bogus works with Native AOT for single values but not for objects (it builds its property setters with `MakeGenericMethod`, which has no native code). AutoBogus works for flat objects but not for graphs, and AutoFixture makes graphs without their collections. Phoney's source-generated model makes the same data as with the JIT, for the same seed.

**Start-up**: the time from `Main` to the first generated object of a fresh process (median of 15 processes). "JIT" is a plain `dotnet` run, "ReadyToRun" the app published with `-p:PublishReadyToRun=true`, and "Native AOT" the app published with Native AOT:

| Library | JIT | ReadyToRun | Native AOT |
|---|---:|---:|---:|
| Phoney (reflection) | 127 ms | 71 ms | fails |
| Phoney (source generated) | 86 ms | 40 ms | 4.8 ms |
| Bogus | 121 ms | 83 ms | fails |
| AutoBogus | 95 ms | 67 ms | 19 ms |
| AutoFixture | 53 ms | 36 ms | 2.1 ms |
| NBuilder | 19 ms | 17 ms | 0.6 ms |
| Faker.Net | 14 ms | 14 ms | 3.1 ms |

Most of Phoney's first call is JIT compilation of its own code, paid once per process, so it matters for short-lived tools and not for services or test runs. The Phoney package therefore also carries `Phoney.dll` precompiled with ReadyToRun for `win-x64`, `linux-x64`, `linux-arm64` and `osx-arm64` (under `runtimes/<rid>/lib/<tfm>`). The .NET host picks the copy for its platform by itself, also without a runtime identifier, so the first object comes about twice as fast with no setting. It is the same IL, so the data is identical, and Native AOT and trimming compile from the IL.

Reproduce the tables with:

```bash
dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- --filter '*' --artifacts out   # the timed matrix
dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- summarize out                   # prints it as a markdown table
PHONEY_BENCH_AOT=1 dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- --filter '*' --artifacts out-aot   # the same with Native AOT
dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold                           # start-up, one process per measurement
dotnet publish benchmarks/Phoney.Benchmarks.Comparison -c Release -r linux-x64 -p:PublishReadyToRun=true -o out-r2r-app   # the app with ReadyToRun:
out-r2r-app/Phoney.Benchmarks.Comparison cold                                                              # its start-up
dotnet publish benchmarks/Phoney.Benchmarks.Comparison -c Release -r linux-x64 -p:AotComparison=true -o out-aot-app   # the app with Native AOT:
out-aot-app/Phoney.Benchmarks.Comparison cold                                                              # its start-up
out-aot-app/Phoney.Benchmarks.Comparison sanity                                                            # 1,000 objects per library: how many differ, how many are incomplete
dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold-phases                    # where Phoney's first call spends its time (and JIT time)
dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- check                          # what each library actually produces
```

</details>

## How the faker.js data gets in

```
@faker-js/faker (npm, pinned)  →  tools/extract (Node)      →  data/raw/*.json
data/raw/*.json                →  tools/Phoney.DataCompiler  →  src/Phoney/Resources/Locales/*.bin.br
                                                               src/Phoney/Data/Generated/*.g.cs
                                                               data/report.md
```

The compiler flattens faker.js definitions into typed entries, builds a key catalog (so modules use integer keys), and **validates every template** against faker.js' method list and each locale's fallback chain; broken references fail the build. The **Sync faker.js data** workflow runs weekly: when npm has a newer `@faker-js/faker` it re-imports, recompiles and tests, then opens a pull request with the data diff and report, or an issue if the new data needs code changes. To update manually:

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
