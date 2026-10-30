using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>
/// Which members a strict generator wants a source for: the one rule <c>MemberPlan</c> (when generating) and the strict analyzer (PHONEY004, while typing)
/// both apply, so the warning and the exception name the same members.
/// </summary>
public sealed class StrictCoverageTests
{
    [Test]
    [Arguments(true, false, false, false, true)]   // a scalar nothing gives a value
    [Arguments(false, false, false, false, false)] // a nested object, a collection: made anyway
    [Arguments(true, true, false, false, false)]   // an annotation defines the value
    [Arguments(true, false, true, false, false)]   // a rule, the feed, a declared default or a custom convention
    [Arguments(true, false, false, true, false)]   // a name convention that is switched on
    public void A_scalar_needs_a_source_unless_an_annotation_or_a_name_convention_gives_its_value(
        bool isScalar, bool definesValue, bool hasSource, bool nameConventionApplies, bool uncovered)
    {
        StrictCoverage.LeavesUncovered(isScalar, definesValue, hasSource, nameConventionApplies).ShouldBe(uncovered);
    }

    [Test]
    public void The_exception_and_the_warning_give_the_same_advice()
    {
        StrictCoverage.Advice.ShouldBe("Add .With(...) or .Ignore(...) for them");
    }
}
