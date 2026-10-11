# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Phoney is a .NET fake-data library (targets net8.0 and net10.0) built on the locale data of faker.js. Priorities, in this order of concern: **performance, ease of use, robustness**, with generation that works through **source generation and Native AOT** wherever possible. Follow SOLID and DRY, and work **test first**: write the failing test, make it pass, then refactor.

## Commands

```bash
dotnet build -c Release                  # warnings are errors (Directory.Build.props); public members need XML docs
dotnet test                              # all test projects; runner is Microsoft.Testing.Platform (global.json), tests use TUnit + Shouldly
dotnet test --project tests/Phoney.Tests -- --treenode-filter "/*/*/PopulateTests/Keeps_what_is_set_and_fills_the_rest"   # one test
dotnet test --project tests/Phoney.Tests -- --treenode-filter "/*/*/NationalIdTests/*"                                      # one class
dotnet publish tests/Phoney.AotSmoke -c Release -p:TreatWarningsAsErrors=true   # Native AOT: trim/AOT warnings must stay at zero
dotnet run -c Release --project benchmarks/Phoney.Benchmarks -- --filter '*'
```

- Tests target only net10.0 locally; CI (`GITHUB_ACTIONS=true`) adds net8.0. CI also runs the AOT publish and executes the binary. On a Mac the link step can fail with `library 'ssl' not found` (then `brotlienc`) because Homebrew's libs are not on the linker path; prefix the publish with `LIBRARY_PATH=/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib` (needs `brew install openssl@3 brotli`) and run `artifacts/aot/Phoney.AotSmoke`. The compile step before the link is what reports AOT warnings.
- Locale data is refreshed with `cd tools/extract && npm install --save-exact @faker-js/faker@latest && node export-locales.mjs ../../data/raw`, then `dotnet run --project tools/Phoney.DataCompiler`, then `dotnet test`. CI fails if `data/`, `src/Phoney/Resources`, `src/Phoney/Data/Generated` or the README locale table differ from a fresh import, so never hand-edit those.

## Architecture

**Data pipeline.** faker.js locales → `data/raw/*.json` (Node, `tools/extract`) → `tools/Phoney.DataCompiler` → Brotli resources `src/Phoney/Resources/Locales/*.bin.br` (embedded) + generated integer key catalog `src/Phoney/Data/Generated/DataKeys.g.cs`. The compiler validates every `{{…}}` template against each locale's fallback chain. At runtime `LocaleStore` loads a locale lazily and flattens its fallback chain (`locale → … → en → base`) into one array, so a lookup is an array index. Modules read data by `DataKeys.*` integer keys, not strings.

**Values.** `Faker` (per-locale, owns a `Randomizer`, a seeded xoshiro256**) exposes lazily created modules (`src/Phoney/Modules`, one per faker.js module plus `NationalIdModule`). `Fake` is the thread-safe static facade over a per-thread `Faker`. Seeded fakers fix `ReferenceDate` so relative dates don't drift with the clock. Anything that draws randomness must draw from the faker's `Randomizer`, and **the order of draws is part of the contract**: existing seeded output must not change, so new randomness goes after existing draws (see `PersonModule.Profile`).

**Object generation.** `Generator<T>` is immutable (every method returns a copy) and runs against a `FakeModel<T>`, which describes members and how to create/read/set them. Two implementations produce **identical data for the same seed**: `ReflectionModel` (compiled delegates, annotated `RequiresUnreferencedCode`/`RequiresDynamicCode`) and models emitted by `src/Phoney.SourceGenerator` for `[FakeFor<T>]` (registered through `ModelRegistry` by a module initializer; AOT/trim safe). Rules, conventions and nullability/annotation constraints are applied in `FakeScope.Get`, which both paths must call for every member. Name conventions live in `Generation/Shared/ConventionRules.cs`, which is **compiled into both the runtime and the netstandard2.0 generator** (keep it free of newer APIs); `ConventionValues` turns a `ConventionKind` into a value. Append new `ConventionKind` values at the end of the enum (generated code embeds the numeric values). `Identity` (in `FakeScope.cs`) is the lazy per-object person/address/timeline so first name, email, birthdate, age and national id of one object agree. `Populate` is one shared algorithm (`Populator`) over the model's `Get`/`IsUnset`/`Set`/`Fallback` hooks, so reflection and generated models fill objects the same way.

**When the model contract changes** (new `FakeModel<T>` member, `FakeMember` argument): update `ReflectionModel.cs`, `Emitter.cs`/`ModelBuilder.cs` in the generator, and review then accept the snapshot (`tests/Phoney.SourceGenerator.Tests/Snapshots/*.received.cs` → rename to `.verified.cs`). The test `Generated_and_reflection_models_produce_identical_data` and the Populate parity test guard the two paths.

**National ids.** `NationalIdScheme` pairs a generator with a validator per country and kind; `NationalIds` is the registry (schemes in `src/Phoney/NationalIds`). Every scheme must have a validator so tests can assert generated values.

**Templates.** `Template`/`TemplateFunctions` evaluate `{{module.method(args)}}` (case-insensitive, faker.js names plus .NET names); register new module methods in `TemplateFunctions.Modules.cs`.

**Packages.** `Phoney` (with the generator packed inside), `Phoney.TUnit` (`[FakeData]`, built on `ArgumentGenerator`).

## Conventions

- Tests use TUnit (`[Test]`, `[Arguments]`) with Shouldly. New checksum/format logic should be verified against an independent implementation (the national id schemes were cross-checked with `python-stdnum`), not only against itself.
- Generated values must be reproducible: no `System.Random`, `DateTime.Now`, `Guid.NewGuid` or unordered iteration in generation paths; use the faker's `Randomizer` and `ReferenceDate`.
- Keep hot paths allocation-free (array-indexed lookups, templates parsed once and cached); check `benchmarks/` when touching them.
- README is user-facing documentation for every feature and also contains the generated locale table between `<!-- locales:start/end -->` markers.
