namespace Phoney.Generation;

/// <summary>What produces a member's value, before it is bound to the member as a <see cref="RuleSlot"/>.</summary>
internal readonly record struct SlotDecision(RuleKind Kind, object? Value, Func<Faker, object?>? Boxed = null);

/// <summary>
/// The sources that can give a member its value, by member index. Which source wins is not decided here but by the order <see cref="SlotBinder"/> asks them in.
/// </summary>
internal sealed class SlotSources
{
    /// <summary>Explicit rules (<c>With</c>, <c>Ignore</c>, <c>Sequence</c>…) or, for a position in the graph, path rules.</summary>
    public IReadOnlyDictionary<int, SlotDecision>? Rules { get; init; }

    /// <summary>The values of a data feed, already converted to the members' types.</summary>
    public IReadOnlyDictionary<int, SlotDecision>? Feed { get; init; }

    /// <summary>Members that must be unique, with the attempts allowed per value.</summary>
    public IReadOnlyDictionary<int, int>? Unique { get; init; }

    /// <summary>Members with a <c>Where</c> condition.</summary>
    public IReadOnlySet<int>? Filtered { get; init; }

    /// <summary>Whether optional constructor parameters get the default they declare.</summary>
    public bool ParameterDefaults { get; init; }

    /// <summary>Custom conventions, the generator's own before the app-wide ones.</summary>
    public IReadOnlyList<CustomConvention>? Conventions { get; init; }

    /// <summary>Records which registrations were used, when rules are verified.</summary>
    public RuleUsage? Usage { get; init; }
}

/// <summary>
/// Decides, for every member of an object, which source gives it its value and binds that to a <see cref="RuleSlot"/>. One order for the root object, nested objects and
/// path positions alike, the first source that has a say wins: a rule, then the feed, then the default the member declares, then a custom convention. A member that is
/// <c>Unique</c> or has a <c>Where</c> condition is decided by that and gets no declared default (its value is drawn, then checked), but still takes a convention.
/// A member that nothing decides gets no slot, unless it is <c>Unique</c>: then its slot only carries the uniqueness.
/// </summary>
internal static class SlotBinder
{
    /// <summary>The slots of <paramref name="members"/>, by index; <see langword="null"/> where nothing decides the member.</summary>
    public static RuleSlot?[] Bind(IReadOnlyList<FakeMember> members, SlotSources sources)
    {
        var slots = new RuleSlot?[members.Count];
        for (var i = 0; i < slots.Length; i++)
        {
            var unique = sources.Unique is not null && sources.Unique.TryGetValue(i, out var attempts);
            attempts = unique ? sources.Unique![i] : 0;
            if (Decide(members[i], i, unique, sources) is { } decision)
                slots[i] = new RuleSlot(members[i], decision.Kind, decision.Value, unique, attempts, decision.Boxed);
            else if (unique)
                slots[i] = new RuleSlot(members[i], RuleKind.Default, null, unique: true, attempts);
        }

        return slots;
    }

    private static SlotDecision? Decide(FakeMember member, int index, bool unique, SlotSources sources)
    {
        if (sources.Rules is not null && sources.Rules.TryGetValue(index, out var rule))
            return rule;
        if (sources.Feed is not null && sources.Feed.TryGetValue(index, out var fed))
            return fed;
        if (sources.ParameterDefaults && !unique && sources.Filtered?.Contains(index) != true && ParameterDefaults.Decision(member) is { } declared)
            return declared;

        if (sources.Conventions is not null)
        {
            foreach (var convention in sources.Conventions)
            {
                if (convention.Matches(member))
                {
                    sources.Usage?.Convention(convention);
                    return new SlotDecision(RuleKind.Convention, convention.Factory);
                }
            }
        }

        return null;
    }
}
