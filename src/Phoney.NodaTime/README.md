# Phoney.NodaTime

[NodaTime](https://nodatime.org) for [Phoney](https://github.com/Quattordici/Phoney) fake data.

```csharp
using Phoney.NodaTime;

var faker = new Faker("sv", seed: 42);
Instant created = faker.Noda().Instant.Past();
LocalDate birthday = faker.Noda().LocalDate.Birthdate(18, 65);
ZonedDateTime meeting = faker.Noda().ZonedDateTime.Soon(7, DateTimeZoneProviders.Tzdb["Europe/Stockholm"]);
DateTimeZone zone = faker.Noda().TimeZone();
```

| Values | Methods |
|---|---|
| `Instant` | `Anytime`, `Past`, `Future`, `Recent`, `Soon`, `Between` |
| `LocalDate` | `Past`, `Future`, `Recent`, `Soon`, `Between`, `Birthdate` |
| `LocalDateTime` (UTC) | `Past`, `Future`, `Recent`, `Soon`, `Between` |
| `ZonedDateTime` (UTC or a given zone) | `Past`, `Future`, `Recent`, `Soon` |
| other | `TimeOfDay()`, `TimeZone()`, `Duration(min, max)`, `Period(minDays, maxDays)` |

- Values are relative to `Faker.ReferenceDate` (or its `TimeProvider`) and drawn exactly like their built-in counterparts (`faker.Date.Past()`…), so seeds reproduce them.
- `Noda()` works on `Faker` and `IFaker` and allocates nothing.

## Objects with NodaTime members

With the package installed, generated objects get NodaTime members that follow Phoney's name conventions, with the reflection model and with `[FakeFor<T>]` source-generated models (AOT safe):

```csharp
public class Subscription
{
    public Instant CreatedAt { get; set; }      // in the past
    public Instant UpdatedAt { get; set; }      // after CreatedAt
    public ZonedDateTime ExpiresAt { get; set; } // in the future
    public LocalDate BirthDate { get; set; }    // matches Age
    public int Age { get; set; }
}

var subscriptions = Fake.Many<Subscription>(10);
```

`Instant`, `OffsetDateTime` and `ZonedDateTime` (UTC) are generated like `DateTimeOffset`, `LocalDateTime` like `DateTime`, `LocalDate` like `DateOnly`, `LocalTime` like `TimeOnly` and `Duration` like `TimeSpan`. They count as values for `Strict()`.
