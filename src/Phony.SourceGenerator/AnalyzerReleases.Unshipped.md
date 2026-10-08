; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PHONY001 | Phony | Error | Class with [FakeFor] must be partial
PHONY002 | Phony | Warning | Type cannot be generated
PHONY003 | Phony | Error | [FakeFor] inside a generic type
