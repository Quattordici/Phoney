# Phoney

A .NET library that produces fake data, from single values to whole object graphs, reproducibly and in the style of a given locale.

## Language

### Entry points

**Faker**:
A source of values for one locale and one seed, grouped into modules (person, location, finance, …).
_Avoid_: fake instance

**Module**:
A topic-grouped set of value methods on a Faker, such as `faker.Person` or `faker.Finance`.
_Avoid_: category, provider

**Fake**:
The static facade over a per-thread Faker. It is the entry point most users type, not a separate concept.

**Generator**:
Produces whole objects of one type (an `ObjectGenerator<T>`) and is immutable: every change returns a new Generator. Used inside another Generator it is either a **member generator** (given to one member) or a **type-wide generator** (given for every object of a type).
_Avoid_: builder, factory

### What decides a value

**Rule**:
An explicit instruction for a member (`With`, `WithOneOf`, `Ignore`, `Unique`, `Where`, `Sequence`, …). A Rule wins over everything below it; a Rule for a member also wins over a type-wide generator.

**Path**:
A dotted member selector such as `Billing.City` or `Lines.Each().Sku` that points a Rule at a member of a nested object. A Path rule applies to that spot only, not to every object of that type.
_Avoid_: position

**Feed**:
Rows of CSV or JSON from which members are filled, one row per object. Wins over conventions, loses to Rules.

**Convention**:
A default chosen from a member's name or type (`Email`, `City`, `CreatedAt`, every `DateOnly`). A Generator's own Conventions come before the app-wide ones.

**Annotation**:
A data annotation (`[Range]`, `[StringLength]`, …) on a member. It constrains whatever value was chosen and tells Phoney how to make that member invalid or hit its boundary.

### Reproducibility

**Seed**:
The number that fixes the values and dates a Faker or Generator produces.

**Key**:
A string that stands for a seed, giving stable stand-ins for real data (`Keyed("customer:42")`). Reproducible, not secret: anyone who knows the key can compute its stand-in.

**Pseudonymize**:
Replace real data with stand-ins derived from a Key, so the same real value always becomes the same fake one, in every table. The stand-in is pseudonymous, not anonymous: anyone who knows the Key can recompute it.
_Avoid_: anonymize, mask

**Fork**:
A Faker derived from a seed and an Index, so each item has its own and the result does not depend on thread timing.

**Index**:
The place of an object in a generated sequence, starting at 0. Numbering, cycling and feed rows follow it, and an object's data depends on seed and Index only.
_Avoid_: position, item *n*

**Reproducible**:
The guarantee that a seed or Key gives the same data on every OS, CPU and .NET version, on every day it is run.
_Avoid_: deterministic

**Replay**:
Rerunning a failed test with the seed it reported (`PHONEY_SEED`), so random test data becomes the same data.

**Reference date**:
The "now" that relative dates (past, recent, birthdates, ages) are computed from. A seeded Faker or Generator has a fixed one, so its dates do not change from day to day.

**Run clock**:
What a Generator gives the Faker of each run to take its Reference date from: an explicit date, else a clock (`TimeProvider`). The order is explicit date, then clock, then (for a nested Generator) the parent's date, then the fixed seeded date, then the system clock.

### Values that agree

**Identity**:
The person, address and timeline shared by all members of one generated object, so its names, email, age, national id and dates agree.

**Profile**:
A standalone coherent value you ask a Faker for (`PersonProfile`, `AddressProfile`).

**Timeline**:
The creation time of one generated object, drawn once, that its updated and later dates are built around, so a last-updated date is never before the created date. Part of the Identity.

**Causal timeline**:
A Timeline across a whole graph, in which each nested object is created after the object that contains it.

**National id**:
A personal, company or VAT number with valid check digits for a country. Structurally valid, not issued.
_Avoid_: identity (reserved for Identity)

**Scheme**:
The generator and validator for one country and kind of national id.
_Avoid_: calling a Scheme a generator

### Locales and templates

**Locale**:
A language or country variant of the data, with a fallback chain (`de_AT` falls back to `de`, then `en`, then the base data).
_Avoid_: culture, language, region

**Country locale**:
A Locale for one country (`de_AT`, `en_US`). Addresses made in it are in that country.

**Language-wide locale**:
A Locale for a language as a whole (`en`, `de`). Addresses made in it draw their country.

**Template**:
A string with `{{module.method}}` placeholders that a Faker fills in (`{{Person.FirstName}} lives at {{Location.Address}}`).
_Avoid_: calling an image URL pattern a template; those are **image sources**

### Objects and graphs

**Populate**:
Fills in an object you already have, using the same Conventions, Rules and Annotations as generating one. By default it fills only unset members.

**Unset**:
A member that holds its type's default (`null`, `0`, `false`, `Guid.Empty`, an empty string or collection). A default you set on purpose is unset too; protect it with `Ignore`.
_Avoid_: empty, missing

**Back-reference**:
A member of a nested object that points back at the nearest containing object of its type, closing a cycle such as `Line.Order`.

**Parent key**:
A key copied down from an object into the foreign keys of the objects nested in it, so a generated aggregate joins up.

**Subtype**:
The concrete type chosen for a member declared as an interface, abstract class or base class.

### Testing

**Property check**:
A test that generates objects, runs a property on each and reports a failing one (`Check`).

**Shrink**:
Reduce a failing object to the simplest one that still fails, by regenerating it from simpler draws so it stays realistic and obeys the Generator's Rules.

**Counterexample**:
The shrunk object a Property check reports. The failing object before shrinking is the **original**.
_Avoid_: failing case

**Invalid data**:
A value that the real validators of a member's Annotations reject.

**Boundary data**:
A value exactly on a limit of a member's Annotations. It is always valid.

**Case**:
One object that differs from a valid one in a single member, made invalid or put on its limit. All Cases of a Generator come from the same base seed.
