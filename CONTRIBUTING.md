# Contributing to Phoney

Thanks for your interest in contributing! Contributions are welcome through pull requests from forks.

## How to contribute

1. **Open an issue first** for anything beyond a small fix, so we can agree on the approach before you
   spend time on it.
2. **Fork** the repository and create a branch from `main`.
3. Make your change, with tests where it makes sense.
4. Open a **pull request** against `main`.

## Building and testing

```bash
dotnet build -c Release
dotnet test -c Release --no-build
```

The test project targets both `net8.0` and `net10.0`, so both SDKs are needed locally to run the full
matrix. The CI also runs a Native AOT smoke test (`tests/Phoney.AotSmoke`) where trim and AOT warnings
fail the build, so avoid reflection-based patterns in library code.

## Locale data

Locale data under `data/`, `src/Phoney/Resources` and `src/Phoney/Data/Generated` is generated from a
pinned faker.js version by the tools in `tools/`. **Do not edit generated files by hand.** CI re-imports
the data and fails if the committed output differs from a fresh import.

## Pull request expectations

- Keep pull requests focused; unrelated changes belong in separate PRs.
- All CI checks must pass.
- Pull requests are **squash-merged**, so the PR title becomes the commit message. Please make it clear
  and descriptive.
- Resolve review conversations before asking for a re-review.

## About CI on pull requests

Because this is a public repository, CI workflows on pull requests from forks do not start
automatically. A maintainer has to approve the run first, and changes to workflow files, project files
(`*.csproj`, `Directory.Build.*`) and `tools/` are reviewed with extra care since they execute during
the build. A short delay before checks appear is expected.

## Reporting security issues

Please do not use public issues for security problems. See [SECURITY.md](SECURITY.md).
