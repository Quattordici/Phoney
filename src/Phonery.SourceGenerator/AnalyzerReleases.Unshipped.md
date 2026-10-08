; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PHONERY001 | Phonery | Error | Class with [FakeFor] must be partial
PHONERY002 | Phonery | Warning | Type cannot be generated
PHONERY003 | Phonery | Error | [FakeFor] inside a generic type
