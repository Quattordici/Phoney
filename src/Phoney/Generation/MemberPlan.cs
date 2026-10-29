namespace Phoney.Generation;

/// <summary>What produces a member's value, before it is bound to the member as a <see cref="RuleSlot"/>.</summary>
internal readonly record struct SlotDecision(RuleKind Kind, object? Value, Func<Faker, object?>? Boxed = null);

/// <summary>
/// How a member's produced values are checked, besides who produces them: uniqueness (attempts per value, 0 for none), a <c>Where</c> condition, whether a duplicate
/// generates the whole object again, and whether the value is kept for a unique combination.
/// </summary>
internal readonly record struct SlotChecks(int UniqueAttempts = 0, Func<object?, bool>? Filter = null, int FilterAttempts = 0, bool RetriesObject = false, bool RecordsKeyPart = false);

/// <summary>A condition from <c>Where(x =&gt; x.Member, …)</c> and the attempts allowed per value.</summary>
internal sealed record MemberCondition(Func<object?, bool> Accepts, int MaxAttempts);

/// <summary>
/// What a <see cref="MemberPlan"/> is built from: the sources that can give a member its value, by member index (which one wins is decided by the plan), and the
/// policies of the root object. Nested objects and path positions give only sources.
/// </summary>
internal sealed class MemberPlanInputs
{
    /// <summary>Explicit rules (<c>With</c>, <c>Ignore</c>, <c>Sequence</c>…) or, for a position in the graph, path rules.</summary>
    public IReadOnlyDictionary<int, SlotDecision>? Rules { get; init; }

    /// <summary>The values of a data feed, already converted to the members' types.</summary>
    public IReadOnlyDictionary<int, SlotDecision>? Feed { get; init; }

    /// <summary>Members that must be unique, with the attempts allowed per value.</summary>
    public IReadOnlyDictionary<int, int>? Unique { get; init; }

    /// <summary>Whether optional constructor parameters get the default they declare.</summary>
    public bool ParameterDefaults { get; init; }

    /// <summary>Custom conventions, the generator's own before the app-wide ones.</summary>
    public IReadOnlyList<CustomConvention>? Conventions { get; init; }

    /// <summary>Records which registrations were used, when rules are verified.</summary>
    public RuleUsage? Usage { get; init; }

    /// <summary>The <c>Where(x =&gt; x.Member, …)</c> conditions by member name; a name that is not a member is reported.</summary>
    public IReadOnlyDictionary<string, MemberCondition>? Conditions { get; init; }

    /// <summary>The combinations of <c>Unique(x =&gt; new { … })</c>: the member names joined by commas, with the attempts allowed per object.</summary>
    public IReadOnlyDictionary<string, int>? UniqueKeys { get; init; }

    /// <summary>Whether every scalar member needs a source, a name convention or an annotation that defines its value.</summary>
    public bool Strict { get; init; }

    /// <summary>Whether members get values from their names (<c>WithNameConventions</c>).</summary>
    public bool NameConventions { get; init; } = true;

    /// <summary>Whether <c>CreateWith</c> creates the objects, so members can't be drawn again or read back as they are produced.</summary>
    public bool CreatesWithFactory { get; init; }

    /// <summary>The name of the generated type, for messages.</summary>
    public string TypeName { get; init; } = "";
}

/// <summary>
/// Decides, for every member of an object, which source gives it its value and how it is checked, and binds both to a finished <see cref="RuleSlot"/> in one pass.
/// One order for the root object, nested objects and path positions alike, the first source that has a say wins: a rule, then the feed, then the default the member
/// declares, then a custom convention. A member that is <c>Unique</c> or has a <c>Where</c> condition is decided by that and gets no declared default (its value is drawn,
/// then checked), but still takes a convention. A member that nothing decides gets no slot, unless a policy needs one (uniqueness, a condition, a key part).
/// </summary>
/// <remarks>
/// The checks run in the order users have always seen the errors: the strict check (on the sources alone, so a condition does not count as coverage), the conditions,
/// then the unique combinations.
/// </remarks>
internal sealed class MemberPlan
{
    private MemberPlan(RuleSlot?[] slots, int objectRetries, UniqueCombination[] combinations)
    {
        Slots = slots;
        ObjectRetries = objectRetries;
        Combinations = combinations;
    }

    /// <summary>The slots of the members, by index; <see langword="null"/> where nothing decides or checks the member.</summary>
    public RuleSlot?[] Slots { get; }

    /// <summary>How often a whole object may be generated again for a unique identity member or combination (the smallest limit; 0 for none).</summary>
    public int ObjectRetries { get; }

    /// <summary>The unique combinations, in name order.</summary>
    public UniqueCombination[] Combinations { get; }

    /// <summary>The plan for <paramref name="members"/>.</summary>
    /// <exception cref="InvalidOperationException">A strict member has no source, or a condition or combination can't apply to its member.</exception>
    public static MemberPlan Build(IReadOnlyList<FakeMember> members, MemberPlanInputs inputs)
    {
        var conditions = inputs.Conditions is { Count: > 0 } c ? c.OrderBy(f => f.Key, StringComparer.Ordinal).ToList() : null;
        var keys = inputs.UniqueKeys is { Count: > 0 } k ? k.OrderBy(f => f.Key, StringComparer.Ordinal).ToList() : null;
        var known = conditions is null && keys is null ? null : IndexByName(members);

        var policies = Policies(members.Count, inputs, known, conditions);
        var decisions = new SlotDecision?[members.Count];
        for (var i = 0; i < decisions.Length; i++)
            decisions[i] = Decide(members[i], i, policies[i], inputs);

        if (inputs.Strict)
            CheckStrict(members, decisions, policies, inputs);
        foreach (var (name, filter) in conditions ?? [])
            BindCondition(members, known!, decisions, policies, inputs, name, filter);
        var combinations = keys is null ? [] : BindCombinations(members, known!, policies, inputs, keys);

        var slots = MakeSlots(members, decisions, policies, inputs, out var objectRetries);
        foreach (var combination in combinations)
            objectRetries = Smallest(objectRetries, combination.MaxAttempts);
        return new MemberPlan(slots, objectRetries, combinations);
    }

    /// <summary>What is known about each member's checks before its source is decided: uniqueness, and whether a condition names it.</summary>
    private static Policy[] Policies(int count, MemberPlanInputs inputs, Dictionary<string, int>? known, List<KeyValuePair<string, MemberCondition>>? conditions)
    {
        var policies = new Policy[count];
        if (inputs.Unique is not null)
        {
            foreach (var (index, attempts) in inputs.Unique)
                policies[index].UniqueAttempts = attempts;
        }

        foreach (var (name, _) in conditions ?? [])
        {
            if (known!.TryGetValue(name, out var index))
                policies[index].Conditioned = true;
        }

        return policies;
    }

    /// <summary>The finished slots, and the smallest limit of the unique identity members that generate the whole object again (0 for none).</summary>
    private static RuleSlot?[] MakeSlots(IReadOnlyList<FakeMember> members, SlotDecision?[] decisions, Policy[] policies, MemberPlanInputs inputs, out int objectRetries)
    {
        var slots = new RuleSlot?[members.Count];
        objectRetries = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            var policy = policies[i];
            if (decisions[i] is not { } decision)
            {
                if (!policy.NeedsSlot)
                    continue;
                decision = new SlotDecision(RuleKind.Default, null);
            }

            // A unique member read from the object's identity can't be drawn again on its own (the identity is cached, and name, email and address must agree),
            // so a duplicate generates the whole object again.
            var retriesObject = decision.Kind == RuleKind.Default && policy.Unique && inputs.NameConventions && ConventionValues.IsIdentityBound(members[i].Convention);
            if (retriesObject)
                objectRetries = Smallest(objectRetries, policy.UniqueAttempts);

            slots[i] = new RuleSlot(members[i], decision, new SlotChecks(policy.UniqueAttempts, policy.Filter?.Accepts, policy.Filter?.MaxAttempts ?? 0, retriesObject, policy.RecordsKeyPart));
        }

        return slots;
    }

    private static int Smallest(int limit, int attempts) => limit == 0 ? attempts : Math.Min(limit, attempts);

    private static Dictionary<string, int> IndexByName(IReadOnlyList<FakeMember> members)
    {
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < members.Count; i++)
            known[members[i].Name] = i;
        return known;
    }

    private static SlotDecision? Decide(FakeMember member, int index, Policy policy, MemberPlanInputs inputs)
    {
        if (inputs.Rules is not null && inputs.Rules.TryGetValue(index, out var rule))
            return rule;
        if (inputs.Feed is not null && inputs.Feed.TryGetValue(index, out var fed))
            return fed;
        if (inputs.ParameterDefaults && !policy.Unique && !policy.Conditioned && ParameterDefaults.Decision(member) is { } declared)
            return declared;

        var convention = inputs.Conventions?.FirstOrDefault(c => c.Matches(member));
        if (convention is null)
            return null;

        inputs.Usage?.Convention(convention);
        return new SlotDecision(RuleKind.Convention, convention.Factory);
    }

    /// <summary>A strict generator needs a source, a name convention or an annotation that defines the value for every scalar member; uniqueness alone counts.</summary>
    private static void CheckStrict(IReadOnlyList<FakeMember> members, SlotDecision?[] decisions, Policy[] policies, MemberPlanInputs inputs)
    {
        var uncovered = members
            .Where((m, i) => decisions[i] is null && !policies[i].Unique && m.IsScalar && m.Constraints?.DefinesValue != true
                && (m.Convention == ConventionKind.None || !inputs.NameConventions))
            .Select(m => m.Name)
            .ToList();
        if (uncovered.Count > 0)
        {
            throw new InvalidOperationException(
                $"Strict generator for {inputs.TypeName}: no rule or convention for {string.Join(", ", uncovered)}. Add .With(...) or .Ignore(...) for them.");
        }
    }

    /// <summary>A member condition needs a member that is drawn on its own and can be drawn again.</summary>
    private static void BindCondition(
        IReadOnlyList<FakeMember> members, Dictionary<string, int> known, SlotDecision?[] decisions, Policy[] policies, MemberPlanInputs inputs, string name, MemberCondition filter)
    {
        var type = inputs.TypeName;
        if (!known.TryGetValue(name, out var index))
            throw new InvalidOperationException($"{type}.{name} can't be generated: it is not a public settable member or constructor parameter, so a Where condition on it has nothing to check.");

        var kind = decisions[index]?.Kind ?? (policies[index].Unique ? RuleKind.Default : (RuleKind?)null);
        if (kind == RuleKind.Ignore)
            throw new InvalidOperationException($"{type}.{name} is ignored, so its Where condition has nothing to check. Remove the Ignore or the condition.");
        if (kind == RuleKind.Dependent)
            throw new InvalidOperationException($"{type}.{name} depends on the object, so it is set after the object is created and a Where condition on it can't draw it again. Check it with .Where(x => …) on the object instead.");
        if (inputs.CreatesWithFactory)
            throw new InvalidOperationException($"{type}.{name} has a Where condition, but CreateWith decides the values of the objects it creates. Check the object with .Where(x => …) instead.");

        if (inputs.NameConventions && kind is null or RuleKind.Default && ConventionValues.IsIdentityBound(members[index].Convention))
        {
            throw new InvalidOperationException(
                $"{type}.{name} comes from the object's identity (one name, email, address and birthdate per object, so that they agree), and drawing it again gives the same value. "
                + $"Give it a value with .With(x => x.{name}, f => …) and filter that, or check the whole object with .Where(x => …).");
        }

        policies[index].Filter = filter;
    }

    /// <summary>
    /// Binds the combinations of <c>Unique(x =&gt; new { … })</c> to member indices. Settable members are read from the finished object; constructor parameters
    /// can't be read back, so their slot keeps the value it produces.
    /// </summary>
    private static UniqueCombination[] BindCombinations(
        IReadOnlyList<FakeMember> members, Dictionary<string, int> known, Policy[] policies, MemberPlanInputs inputs, List<KeyValuePair<string, int>> keys)
    {
        var combinations = new UniqueCombination[keys.Count];
        for (var c = 0; c < keys.Count; c++)
        {
            var names = keys[c].Key.Split(',');
            var indices = new int[names.Length];
            var recorded = new bool[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                if (!known.TryGetValue(names[i], out indices[i]))
                    throw new InvalidOperationException($"{inputs.TypeName}.{names[i]} is not a generated member, so it can't be part of a unique combination.");
                var member = members[indices[i]];
                recorded[i] = !member.IsSettable && !member.FillsExisting;
                if (!recorded[i])
                    continue;
                if (inputs.CreatesWithFactory)
                {
                    throw new InvalidOperationException(
                        $"{inputs.TypeName}.{names[i]} is a constructor parameter, and CreateWith creates the objects, so its value can't be read for the unique combination. Make the member settable, or leave it out of the combination.");
                }

                policies[indices[i]].RecordsKeyPart = true;
            }

            combinations[c] = new UniqueCombination(indices, names, recorded, keys[c].Value);
        }

        return combinations;
    }

    /// <summary>How one member is checked, collected before its slot is made.</summary>
    private struct Policy
    {
        /// <summary>Attempts per value when the member is unique, else 0.</summary>
        public int UniqueAttempts;

        /// <summary>Whether a condition names the member (known before the condition is checked, because it takes away the declared default).</summary>
        public bool Conditioned;

        /// <summary>The member's condition, once it is checked.</summary>
        public MemberCondition? Filter;

        /// <summary>Whether the produced value is kept for a unique combination.</summary>
        public bool RecordsKeyPart;

        public readonly bool Unique => UniqueAttempts > 0;

        public readonly bool NeedsSlot => Unique || Filter is not null || RecordsKeyPart;
    }
}
