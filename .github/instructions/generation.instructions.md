---
applyTo: "src/Phoney/Generation/**/*.cs,src/Phoney.SourceGenerator/**/*.cs"
---

- Rules, conventions and constraints are applied in `FakeScope.Get`; reflection and generated models must
  both call it for every member.
- `Generation/Shared/ConventionRules.cs` is also compiled into the netstandard2.0 generator, so it must not
  use newer APIs. Append new `ConventionKind` values at the end of the enum, because generated code embeds
  the numeric values.
- If the `FakeModel<T>` contract changes, `ReflectionModel.cs`, `Emitter.cs` and `ModelBuilder.cs` must all
  be updated and the generator snapshots reviewed.
- Analyzers must not call `Compilation.GetSemanticModel` (RS1030). Keep `StrictAnalyzer`, `UsageAnalyzer`
  and `Generator<T>.Compile` in sync, and register new diagnostics in `AnalyzerReleases.Unshipped.md` and
  the README diagnostics table.
