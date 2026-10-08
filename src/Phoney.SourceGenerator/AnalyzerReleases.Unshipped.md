; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PHONEY001 | Phoney | Error | Class with [FakeFor] must be partial
PHONEY002 | Phoney | Warning | Type cannot be generated
PHONEY003 | Phoney | Error | [FakeFor] inside a generic type
