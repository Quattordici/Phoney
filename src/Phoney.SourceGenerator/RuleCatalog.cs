using System;
using System.Collections.Generic;
using System.Linq;

namespace Phoney.SourceGenerator;

/// <summary>
/// The fluent methods of <c>ObjectGenerator&lt;T&gt;</c> that the analyzers know by name, in one place so <see cref="StrictAnalyzer"/> and <see cref="UsageAnalyzer"/> agree
/// on them. <c>RuleCatalogTests</c> compares the lists with the methods the generator really has, so a rule added to the generator fails a test until it is listed here.
/// </summary>
internal static class RuleCatalog
{
    /// <summary><c>Unique</c>, which alone accepts a combination of members and combines with the rule that fills the member.</summary>
    public const string Unique = "Unique";

    /// <summary>The methods that take a member selector (<c>x =&gt; x.Name</c>) and say how that member gets its value or that it has none.</summary>
    public static IReadOnlyList<string> MemberRules { get; } =
        ["With", "WithNormalDistribution", "WithOneOf", "WithSubsetOf", "Ignore", Unique, "Invalid", "Boundary", "Sequence", "Cycle"];

    /// <summary>The member rules that occupy a member's one rule slot; <see cref="Unique"/> is kept separately.</summary>
    public static IReadOnlyList<string> SlotRules { get; } = [.. MemberRules.Where(n => n != Unique)];

    /// <summary>The calls that register something for a type (<c>TypeArguments[0]</c>): a later one for the same type replaces an earlier one of the same kind.</summary>
    public static IReadOnlyList<string> TypeRules { get; } = ["WithGeneratorFor", "Subtype"];

    private static readonly HashSet<string> MemberRuleSet = new(MemberRules, StringComparer.Ordinal);
    private static readonly HashSet<string> SlotRuleSet = new(SlotRules, StringComparer.Ordinal);
    private static readonly HashSet<string> TypeRuleSet = new(TypeRules, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="name"/> is a member rule.</summary>
    public static bool IsMemberRule(string name) => MemberRuleSet.Contains(name);

    /// <summary>Whether <paramref name="name"/> is a member rule that occupies the member's rule slot.</summary>
    public static bool OccupiesSlot(string name) => SlotRuleSet.Contains(name);

    /// <summary>Whether <paramref name="name"/> registers something for a type.</summary>
    public static bool IsTypeRule(string name) => TypeRuleSet.Contains(name);
}
