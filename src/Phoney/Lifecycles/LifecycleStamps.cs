namespace Phoney.Generation;

/// <summary>
/// The stamps of the lifecycles on one object: the date members that get the time the object entered a state (tied with <c>Stamp</c>, or named after the state),
/// and the "updated" members, which get the time of the last transition. <see cref="MemberPlan"/> asks it for a member's source after an explicit rule
/// (<see cref="Explicit"/>) and after the feed (<see cref="Named"/>).
/// </summary>
internal sealed class LifecycleStamps
{
    private readonly IReadOnlyList<FakeMember> _members;
    private readonly List<LifecycleDefinition> _lifecycles;
    private readonly string _typeName;
    private readonly Dictionary<int, (LifecycleDefinition Lifecycle, int State)> _explicit = [];

    private LifecycleStamps(IReadOnlyList<FakeMember> members, List<LifecycleDefinition> lifecycles, string typeName)
    {
        _members = members;
        _lifecycles = lifecycles;
        _typeName = typeName;
        foreach (var lifecycle in lifecycles)
        {
            foreach (var (state, name) in lifecycle.Stamps)
            {
                var index = IndexOf(members, name);
                if (index < 0)
                    throw new InvalidOperationException($"{typeName}.{name} can't be stamped: it is not a public settable member or constructor parameter.");
                _explicit.TryAdd(index, (lifecycle, state));
            }
        }
    }

    /// <summary>The stamps of the lifecycles among <paramref name="rules"/> (the sources of one object's members), or <see langword="null"/> when it has none.</summary>
    public static LifecycleStamps? Of(IReadOnlyList<FakeMember> members, IReadOnlyDictionary<int, SlotDecision>? rules, string typeName)
    {
        if (rules is null)
            return null;
        List<LifecycleDefinition>? lifecycles = null;
        for (var i = 0; i < members.Count; i++)
        {
            if (rules.TryGetValue(i, out var rule) && rule.Lifecycle is { } lifecycle)
                (lifecycles ??= []).Add(lifecycle); // in member order, so the first lifecycle of the object wins a member two of them name
        }

        return lifecycles is null ? null : new LifecycleStamps(members, lifecycles, typeName);
    }

    /// <summary>The source of a member tied to a state with <c>Stamp</c>, or <see langword="null"/>.</summary>
    public SlotDecision? Explicit(int member) =>
        _explicit.TryGetValue(member, out var stamp) ? Entered(member, stamp.Lifecycle, stamp.State) : null;

    /// <summary>The source of a date member named after a state (<c>PaidAt</c>, <c>PaidOn</c>, <c>PaidDate</c>), or of an "updated" member; <see langword="null"/> otherwise.</summary>
    public SlotDecision? Named(int member)
    {
        var target = _members[member];
        if (Converter(target.Type) is null)
            return null;
        foreach (var lifecycle in _lifecycles)
        {
            if (lifecycle.StateNamed(target.Name) is var state and >= 0)
                return Entered(member, lifecycle, state);
        }

        if (target.Convention != ConventionKind.RecentDate)
            return null;
        var convert = Converter(target.Type)!;
        var lifecycles = _lifecycles;
        return Stamp(scope =>
        {
            var last = scope.LastTransition(lifecycles[0]);
            for (var i = 1; i < lifecycles.Count; i++)
                last = FakeScope.Later(last, scope.LastTransition(lifecycles[i]));
            return convert(last);
        });
    }

    /// <summary>
    /// Whether <paramref name="type"/> can hold a stamp: a <see cref="DateTimeOffset"/>, <see cref="DateTime"/> or <see cref="DateOnly"/> (or a date type of an
    /// add-on such as <c>Phoney.NodaTime</c>), nullable or not.
    /// </summary>
    public static bool CanHold(Type type) => Converter(type) is not null;

    /// <summary>How a moment becomes a value of <paramref name="type"/>, or <see langword="null"/> when it can't hold one. A <see cref="DateTime"/> is UTC.</summary>
    private static Func<DateTimeOffset, object>? Converter(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(DateTimeOffset))
            return static moment => moment;
        if (underlying == typeof(DateTime))
            return static moment => moment.UtcDateTime;
        if (underlying == typeof(DateOnly))
            return static moment => DateOnly.FromDateTime(moment.UtcDateTime);
        return ScalarAdapters.TryGetMoment(underlying, out var adapted) ? adapted : null;
    }

    /// <summary>The time the object last entered <paramref name="state"/>, or <see langword="null"/>; refused when the member can't hold <see langword="null"/> and an object may skip the state.</summary>
    private SlotDecision Entered(int member, LifecycleDefinition lifecycle, int state)
    {
        var target = _members[member];
        var convert = Converter(target.Type)
            ?? throw new InvalidOperationException($"{_typeName}.{target.Name} can't be stamped with {lifecycle.StateText(state)}: it is a {target.Type.Name}, not a DateTimeOffset, DateTime or DateOnly.");
        if (target.Type.IsValueType && Nullable.GetUnderlyingType(target.Type) is null && lifecycle.IsSkippable(state))
        {
            throw new InvalidOperationException(
                $"{_typeName}.{target.Name} is the time an object entered {lifecycle.StateText(state)}, but some objects never get there, and a {target.Type.Name} can't be null. "
                + $"Make it nullable ({target.Type.Name}?), or leave it out with .Ignore(x => x.{target.Name}).");
        }

        return Stamp(scope =>
        {
            var entered = scope.Entered(lifecycle, state); // the valid value first, so an Invalid case draws what a valid object draws
            if (scope.LifecyclePath(lifecycle).Break is { } broken && broken.Member == member)
                entered = broken.Moment(scope.Created);
            return entered is { } moment ? convert(moment) : null;
        });
    }

    /// <summary>
    /// The members of an object that <paramref name="lifecycle"/> stamps, by member index and state: those tied with <c>Stamp</c>, then the date members named
    /// after a state, in member order; members <paramref name="hasRule"/> gives a rule of their own are left out.
    /// </summary>
    public static List<(int Member, int State)> StampsOf(IReadOnlyList<FakeMember> members, LifecycleDefinition lifecycle, Func<int, bool> hasRule)
    {
        var stamps = new List<(int Member, int State)>();
        foreach (var (state, name) in lifecycle.Stamps)
        {
            var index = IndexOf(members, name);
            if (index >= 0 && !hasRule(index))
                stamps.Add((index, state));
        }

        for (var i = 0; i < members.Count; i++)
        {
            var at = i;
            if (!hasRule(i) && Converter(members[i].Type) is not null && lifecycle.StateNamed(members[i].Name) is var state and >= 0 && !stamps.Exists(s => s.Member == at))
                stamps.Add((i, state));
        }

        return stamps;
    }

    // Drawn once per object with the lifecycle's path, so a Where or Unique on a stamp makes the whole object again.
    private static SlotDecision Stamp(Func<FakeScope, object?> value) => new(RuleKind.Convention, value, ObjectBound: true);

    private static int IndexOf(IReadOnlyList<FakeMember> members, string name)
    {
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Name == name)
                return i;
        }

        return -1;
    }
}
