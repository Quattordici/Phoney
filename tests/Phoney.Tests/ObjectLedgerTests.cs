using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>
/// What one object records about its own members while it is made: values Populate kept, comparisons an Invalid case breaks, the parts of a unique combination,
/// and the unique identity values it claims only once it is accepted.
/// </summary>
public sealed class ObjectLedgerTests
{
    private static RuleSlot UniqueEmail() =>
        new(new FakeMember("Email", typeof(string), ConventionKind.Email), new SlotDecision(RuleKind.Default, null), new SlotChecks(UniqueAttempts: 10, null, 0, RetriesObject: true, false));

    private static readonly object Gate = new();

    [Test]
    public void Remembers_the_members_Populate_kept()
    {
        var ledger = new ObjectLedger();

        ledger.Keep(2);

        ledger.IsKept(2).ShouldBeTrue();
        ledger.IsKept(1).ShouldBeFalse();
    }

    [Test]
    public void Remembers_the_comparisons_an_Invalid_case_breaks()
    {
        var ledger = new ObjectLedger();

        ledger.BreakComparison(3);

        ledger.IsComparisonBroken(3).ShouldBeTrue();
        ledger.IsComparisonBroken(0).ShouldBeFalse();
    }

    [Test]
    public void Remembers_the_parts_of_a_unique_combination()
    {
        var ledger = new ObjectLedger();

        ledger.RecordKeyPart(1, "a");

        ledger.TryGetKeyPart(1, out var value).ShouldBeTrue();
        value.ShouldBe("a");
        ledger.TryGetKeyPart(0, out _).ShouldBeFalse();
    }

    [Test]
    public void An_object_without_a_ledger_or_combinations_is_accepted()
    {
        ObjectLedger.TryClaim(null, Gate, null).ShouldBeTrue();
    }

    [Test]
    public void Claims_a_unique_value_only_when_the_object_is_accepted()
    {
        var slot = UniqueEmail();
        var ledger = new ObjectLedger();

        ledger.Produced(slot, "a@b.c");
        slot.IsClaimed("a@b.c").ShouldBeFalse();

        ObjectLedger.TryClaim(ledger, Gate, null).ShouldBeTrue();
        slot.IsClaimed("a@b.c").ShouldBeTrue();
    }

    [Test]
    public void A_value_produced_before_rejects_the_object()
    {
        var slot = UniqueEmail();
        slot.TryClaim("a@b.c");
        var ledger = new ObjectLedger();

        ledger.Produced(slot, "a@b.c");

        ObjectLedger.TryClaim(ledger, Gate, null).ShouldBeFalse();
    }

    [Test]
    public void A_value_claimed_by_another_object_in_between_rejects_the_object_and_claims_none_of_its_values()
    {
        var first = UniqueEmail();
        var second = UniqueEmail();
        var ledger = new ObjectLedger();
        ledger.Produced(first, "a");
        ledger.Produced(second, "b");

        second.TryClaim("b"); // another thread got there first

        ObjectLedger.TryClaim(ledger, Gate, null).ShouldBeFalse();
        first.IsClaimed("a").ShouldBeFalse();
    }

    [Test]
    public void Claims_combinations_with_the_identity_values()
    {
        var combination = new UniqueCombination([0, 1], ["A", "B"], [false, false], 10);
        object?[] values = [1, 2];

        ObjectLedger.TryClaim(null, Gate, [(combination, values)]).ShouldBeTrue();
        ObjectLedger.TryClaim(null, Gate, [(combination, new object?[] { 1, 2 })]).ShouldBeFalse();
    }
}
