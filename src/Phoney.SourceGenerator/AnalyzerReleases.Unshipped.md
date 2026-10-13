; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PHONEY001 | Phoney | Error | Class with [FakeFor] must be partial
PHONEY002 | Phoney | Warning | Type cannot be generated
PHONEY003 | Phoney | Error | [FakeFor] inside a generic type
PHONEY004 | Phoney | Warning | Strict generator leaves members without a value
PHONEY005 | Phoney | Warning | Result of an immutable Phoney call is discarded
PHONEY006 | Phoney | Warning | Seeded scope is never disposed
PHONEY007 | Phoney | Warning | Rule can't be applied
PHONEY008 | Phoney | Warning | Rule is replaced by a later rule
PHONEY009 | Phoney | Error | Subtype cannot be registered
