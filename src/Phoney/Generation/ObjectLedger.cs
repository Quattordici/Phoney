namespace Phoney.Generation;

/// <summary>
/// What one object records about its own members while it is made, for the steps that run once it is finished: the values <c>Populate</c> kept, the
/// <c>[Compare]</c>s an <c>Invalid</c> case breaks, the parts of a unique combination that only the producer sees, and the unique identity values the object
/// claims once it is accepted. Most objects record none of this, so a scope creates its ledger on the first record (<see cref="FakeScope.Ledger"/>).
/// </summary>
internal sealed class ObjectLedger
{
    private List<int>? _kept;
    private List<int>? _brokenComparisons;
    private Dictionary<int, object?>? _keyParts;
    private List<(RuleSlot Slot, object? Value)>? _pendingClaims;
    private bool _hasDuplicate;

    /// <summary>Records that <c>Populate</c> keeps the value <paramref name="member"/> already has.</summary>
    public void Keep(int member) => (_kept ??= []).Add(member);

    /// <summary>Whether <c>Populate</c> kept the value of <paramref name="member"/>.</summary>
    public bool IsKept(int member) => _kept?.Contains(member) == true;

    /// <summary>Records that <paramref name="member"/> must differ from the member its <c>[Compare]</c> names (an <c>Invalid</c> case).</summary>
    public void BreakComparison(int member) => (_brokenComparisons ??= []).Add(member);

    /// <summary>Whether an <c>Invalid</c> case breaks the <c>[Compare]</c> of <paramref name="member"/>.</summary>
    public bool IsComparisonBroken(int member) => _brokenComparisons?.Contains(member) == true;

    /// <summary>Records the value produced for <paramref name="member"/>, a part of a unique combination (see <see cref="RuleSlot.RecordsKeyPart"/>).</summary>
    public void RecordKeyPart(int member, object? value) => (_keyParts ??= [])[member] = value;

    /// <summary>The value produced for <paramref name="member"/> when it is part of a unique combination.</summary>
    public bool TryGetKeyPart(int member, out object? value)
    {
        value = null;
        return _keyParts is not null && _keyParts.TryGetValue(member, out value);
    }

    /// <summary>
    /// Records a unique identity value the object produced (see <see cref="RuleSlot.RetriesObject"/>): one produced before rejects the object, a new one is
    /// claimed only when the object is accepted.
    /// </summary>
    public void Produced(RuleSlot slot, object? value)
    {
        if (slot.IsClaimed(value))
            _hasDuplicate = true;
        else
            (_pendingClaims ??= []).Add((slot, value));
    }

    /// <summary>
    /// Claims the unique identity values of <paramref name="ledger"/> and the <paramref name="combinations"/> of an accepted object, all or nothing; false when one
    /// was produced before or another thread claimed one first (the object is then generated again).
    /// </summary>
    /// <param name="ledger">The object's ledger, or <see langword="null"/> when it recorded nothing.</param>
    /// <param name="gate">The lock shared by every object of the run, which serialises their claims.</param>
    /// <param name="combinations">The unique combinations with the object's values, or <see langword="null"/>.</param>
    public static bool TryClaim(ObjectLedger? ledger, object gate, IReadOnlyList<(UniqueCombination Combination, object?[] Values)>? combinations)
    {
        if (ledger is { _hasDuplicate: true })
            return false;
        var pending = ledger?._pendingClaims;
        if (pending is null && combinations is null)
            return true;

        // All or nothing, so an object that loses one value to another thread doesn't keep the others claimed.
        lock (gate)
        {
            foreach (var (slot, value) in pending ?? [])
            {
                if (slot.IsClaimed(value))
                    return false;
            }

            foreach (var (combination, values) in combinations ?? [])
            {
                if (combination.IsClaimed(values))
                    return false;
            }

            foreach (var (slot, value) in pending ?? [])
                slot.TryClaim(value);
            foreach (var (combination, values) in combinations ?? [])
                combination.Claim(values);
        }

        return true;
    }
}
