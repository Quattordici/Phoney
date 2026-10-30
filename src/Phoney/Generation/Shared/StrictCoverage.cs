#nullable enable

// This file is compiled into the runtime and into the source generator (see Phoney.SourceGenerator.csproj), so a strict generator's exception and the strict
// analyzer's warning (PHONEY004) name the same members. Keep it free of APIs newer than netstandard2.0.
namespace Phoney.Generation;

/// <summary>Which members a strict generator wants a source for (<c>MemberPlan</c> when generating, <c>StrictAnalyzer</c> while typing).</summary>
internal static class StrictCoverage
{
    /// <summary>How to fix the members a strict generator leaves without a value, in the exception and in the warning.</summary>
    public const string Advice = "Add .With(...) or .Ignore(...) for them";

    /// <summary>
    /// Whether a member leaves a strict generator without a value: a scalar (objects and collections are made anyway) whose value no annotation defines, that has
    /// no source (a rule, <c>Unique</c>, the feed, a declared default, a custom convention) and no name convention that is switched on.
    /// </summary>
    public static bool LeavesUncovered(bool isScalar, bool definesValue, bool hasSource, bool nameConventionApplies) =>
        isScalar && !definesValue && !hasSource && !nameConventionApplies;
}
