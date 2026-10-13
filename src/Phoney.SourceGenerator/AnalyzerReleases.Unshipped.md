; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PHONEY001 | Phoney | Error | Class with [FakeFor] must be partial
PHONEY002 | Phoney | Warning | Type cannot be generated
PHONEY003 | Phoney | Error | [FakeFor] inside a generic type
PHONEY004 | Phoney.Usage | Warning | Strict generator leaves members without a value
PHONEY005 | Phoney.Usage | Warning | Result of an immutable Phoney call is discarded
PHONEY006 | Phoney.Usage | Warning | Seeded scope is never disposed
PHONEY007 | Phoney.Usage | Warning | Rule can't be applied
PHONEY008 | Phoney.Usage | Warning | Rule is replaced by a later rule
PHONEY009 | Phoney | Error | Subtype cannot be registered
PHONEY010 | Phoney.Usage | Warning | Constant seed in a loop repeats the same objects
PHONEY011 | Phoney.Usage | Warning | Argument is out of range
PHONEY012 | Phoney.Usage | Info | Type is generated with reflection
