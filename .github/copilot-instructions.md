# Copilot instructions

Phoney is a .NET fake-data library (net8.0 and net10.0) built on the locale data of faker.js. It supports
Native AOT and source generation. Architecture, commands and conventions are in `CLAUDE.md`.

## Review priorities

- **Reproducibility:** in `src/**`, flag `System.Random`, `DateTime.Now`/`UtcNow`, `Guid.NewGuid`,
  `Environment.TickCount` and unordered iteration (`foreach`, spreads, order-sensitive LINQ) over
  `HashSet`, `Dictionary` and the concurrent, immutable and frozen collections. Randomness comes from the
  faker's `Randomizer` and dates from `ReferenceDate`.
- **Draw order is a contract:** existing seeded output must not change. New randomness goes after existing
  draws.
- **Reflection and source-generated parity:** both models must produce identical data for a seed. Flag
  changes that touch only one path.
- **AOT and trimming:** flag new trim/AOT warnings and per-element reflection. Reflection-only entry points
  carry `RequiresUnreferencedCode`/`RequiresDynamicCode`.
- **Performance:** lookups are array-indexed through `DataKeys.*`, hot paths are allocation-free, templates
  are parsed once.
- **API surface:** public members need XML docs, `IFaker` mirrors every public member of `Faker`, and
  user-facing features are documented in the README.
- **Tests:** TUnit and Shouldly. New behaviour needs a test that fails without it. Checksum and format
  logic must be checked against an independent implementation.

## Generated files

`data/`, `src/Phoney/Resources`, `src/Phoney/Data/Generated` and the locale table in the README are
generated from faker.js and must never be edited by hand. Flag any hand edit and do not review them for style.

## Style

Match the surrounding code: naming, comment density and idiom. Prefer a clear exception message that says
how to fix the problem over a silent failure.
