# AGENTS.md

Phoney is a .NET fake-data library (net8.0 and net10.0) built on the locale data of faker.js. It supports
Native AOT and source generation. Architecture, commands and conventions are in [CLAUDE.md](CLAUDE.md);
read it before changing code.

## Review guidelines

- Generated values must be reproducible for a seed. In `src/**`, flag `System.Random`, `DateTime.Now`,
  `Guid.NewGuid`, `Environment.TickCount` and unordered iteration over hash-based collections.
- The order of `Randomizer` draws is part of the contract. Flag any change that alters existing seeded
  output; new randomness must come after existing draws.
- Reflection and source-generated models must produce identical data for the same seed. Flag changes that
  touch only one path.
- Flag new trim or AOT warnings, per-element reflection, and allocations on hot paths.
- Public members need XML docs, and `IFaker` must mirror every public member of `Faker`.
- New behaviour needs a test (TUnit and Shouldly), and checksum or format logic must be checked against an
  independent implementation.
- `data/`, `src/Phoney/Resources`, `src/Phoney/Data/Generated` and the README locale table are generated.
  Flag hand edits.
- Treat missing README documentation for a user-facing feature as a finding.
