---
applyTo: "tests/**/*.cs"
---

- Tests use TUnit (`[Test]`, `[Arguments]`) with Shouldly.
- Seeded tests must assert exact, reproducible values. Flag `System.Random`, `DateTime.Now` and
  `Guid.NewGuid` in tests that are meant to be deterministic.
- New checksum or format logic (for example national id schemes) must be verified against an independent
  implementation, not only against itself.
- Features that touch object generation need a test that covers both the reflection and the source-generated
  model, with identical data for a seed.
