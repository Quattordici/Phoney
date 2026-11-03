# Phoney.NodaTime

[NodaTime](https://nodatime.org) for [Phoney](https://github.com/Quattordici/Phoney) fake data.

With `using Phoney.NodaTime;`:

<!-- snippet: noda-values -->
```csharp
var faker = new Faker("sv", seed: 42);
Instant created = faker.Noda().Instant.Past();
LocalDate birthday = faker.Noda().LocalDate.Birthdate(18, 65);
ZonedDateTime meeting = faker.Noda().ZonedDateTime.Soon(7, DateTimeZoneProviders.Tzdb["Europe/Stockholm"]);
DateTimeZone zone = faker.Noda().TimeZone();
```
<!-- end snippet -->

| Values | Methods |
|---|---|
| `Instant` | `Anytime`, `Past`, `Future`, `Recent`, `Soon`, `Between` |
| `LocalDate` | `Past`, `Future`, `Recent`, `Soon`, `Between`, `Birthdate` |
| `LocalDateTime` (UTC) | `Past`, `Future`, `Recent`, `Soon`, `Between` |
| `ZonedDateTime` (UTC or a given zone) | `Past`, `Future`, `Recent`, `Soon` |
| other | `TimeOfDay()`, `TimeZone()`, `Offset()`, `AnnualDate()`, `Duration(min, max)`, `Period(minDays, maxDays)` |
| ranges | `Interval(years)` / `Interval(from, to)`, `DateInterval(years)` / `DateInterval(from, to)`, `YearMonth(years)` / `YearMonth(from, to)` |

- Values are relative to `Faker.ReferenceDate` (or its `TimeProvider`) and drawn exactly like their built-in counterparts (`faker.Date.Past()`…), so seeds reproduce them.
- `Noda()` works on `Faker` and `IFaker` and allocates nothing.

## Objects with NodaTime members

With the package installed, generated objects get NodaTime members that follow Phoney's name conventions, with the reflection model and with `[FakeFor<T>]` source-generated models (AOT safe):

<!-- snippet: noda-subscription -->
```csharp
public class Subscription
{
    public Instant CreatedAt { get; set; }      // in the past
    public Instant UpdatedAt { get; set; }      // after CreatedAt
    public ZonedDateTime ExpiresAt { get; set; } // in the future
    public LocalDate BirthDate { get; set; }    // matches Age
    public int Age { get; set; }
}
```
<!-- end snippet -->

<!-- snippet: noda-subscription-use -->
```csharp
var subscriptions = Fake.Many<Subscription>(10);
```
<!-- end snippet -->

`Instant`, `OffsetDateTime` and `ZonedDateTime` (UTC) are generated like `DateTimeOffset`, `LocalDateTime` like `DateTime`, `LocalDate` like `DateOnly`, `LocalTime` like `TimeOnly` and `Duration` like `TimeSpan`. They count as values for `Strict()`.
