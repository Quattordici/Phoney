# Review rules

Source of truth for conventions is `CLAUDE.md` at the repository root. These are the rules most worth
checking in review.

## Reproducibility

- Code under `src/**` must not use `System.Random`, `DateTime.Now`/`UtcNow`, `DateTimeOffset.Now`,
  `Guid.NewGuid` or `Environment.TickCount`. Use the faker's `Randomizer` and `ReferenceDate`.
- No `foreach`, spreads or order-sensitive LINQ over `HashSet`, `Dictionary` or the concurrent,
  immutable and frozen sets and dictionaries in generation paths. Sort first or keep an ordered list.
- The order of `Randomizer` draws is part of the contract: existing seeded output must not change.
  New randomness goes after existing draws.

## Reflection and source-generated parity

- Anything that touches object generation must work through both `ReflectionModel` and the models
  emitted by `Phoney.SourceGenerator`, and produce identical data for a seed.
- Rules, conventions and constraints are applied in `FakeScope.Get`; both paths must call it for every
  member.
- `Generation/Shared/ConventionRules.cs` is also compiled into the netstandard2.0 generator, so it must
  not use newer APIs. New `ConventionKind` values are appended at the end of the enum.
- If the `FakeModel<T>` contract changes, `ReflectionModel.cs`, `Emitter.cs` and `ModelBuilder.cs` must
  all be updated, with snapshots reviewed.

## AOT, trimming and performance

- No new trim or AOT warnings. Reflection-only entry points carry `RequiresUnreferencedCode` and
  `RequiresDynamicCode`.
- Lookups are array-indexed through `DataKeys.*`, hot paths are allocation-free, templates are parsed once.
- Performance rules (CA18xx) and culture rules (CA1304/1305/1309-1311) apply to `src/**`.

## API surface and docs

- Public members need XML docs (warnings are errors).
- `IFaker` mirrors every public member of `Faker`; new modules and methods go in both.
- New module methods are registered in `TemplateFunctions.Modules.cs`.
- Every user-facing feature is documented in the README; add-on packages also in their own README.
- New analyzer diagnostics are registered in `AnalyzerReleases.Unshipped.md` and the README table.

## Tests

- Tests use TUnit and Shouldly, and a new behaviour needs a test that fails without it.
- Checksum and format logic (for example national ids) must be verified against an independent
  implementation, not only against itself.

## Do not review or edit by hand

`data/`, `src/Phoney/Resources`, `src/Phoney/Data/Generated` and the locale table in the README are
generated from faker.js. Flag any hand edit to them.
