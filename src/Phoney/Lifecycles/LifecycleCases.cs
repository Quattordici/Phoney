namespace Phoney.Generation;

/// <summary>
/// One way an <c>Invalid</c> rule on a lifecycle's status breaks a stamp: the walk goes into <paramref name="End"/>, and stamp <paramref name="Member"/> gets
/// <paramref name="Moment"/> of the object's creation time instead of the time it entered its state (<see langword="null"/>: no time at all).
/// </summary>
internal sealed record LifecycleBreak(int End, int Member, string Description, Func<DateTimeOffset, DateTimeOffset?> Moment);

/// <summary>
/// The invalid objects of a lifecycle, which break its stamps three ways: a stamp earlier than the object's creation, a reached state without its stamp
/// (nullable stamps only), and a stamp of a state the object never entered. Each object is made like a valid one, walking into the state the case needs, and
/// then one stamp is replaced; there are no boundary cases.
/// </summary>
internal static class LifecycleCases
{
    /// <summary>The ways to break the stamps of <paramref name="lifecycle"/> on an object with <paramref name="members"/>, in a fixed order.</summary>
    /// <param name="members">The members of the object that holds the status.</param>
    /// <param name="lifecycle">The status member's lifecycle.</param>
    /// <param name="hasRule">Whether a member has a rule of its own, which no case touches.</param>
    public static List<LifecycleBreak> Invalid(IReadOnlyList<FakeMember> members, LifecycleDefinition lifecycle, Func<int, bool> hasRule)
    {
        var stamps = LifecycleStamps.StampsOf(members, lifecycle, hasRule);
        var start = lifecycle.Start;
        var cases = new List<LifecycleBreak>(3);

        var late = stamps.FindIndex(s => s.State != start && lifecycle.Reaches(s.State));
        if (late >= 0)
        {
            var (member, state) = stamps[late];
            cases.Add(new(state, member, $"{members[member].Name} is earlier than the object's creation", static created => created.AddDays(-1))); // a day, so a DateOnly moves too
        }

        var missing = stamps.FindIndex(s => CanBeNull(members[s.Member].Type) && lifecycle.Reaches(s.State));
        if (missing >= 0)
        {
            var (member, state) = stamps[missing];
            cases.Add(new(state, member, $"{members[member].Name} is missing although the object entered {lifecycle.StateText(state)}", static _ => null));
        }

        var unreached = stamps.FindIndex(s => s.State != start);
        if (unreached >= 0)
        {
            var (member, state) = stamps[unreached];
            cases.Add(new(start, member, $"{members[member].Name} is set although the object never entered {lifecycle.StateText(state)}", static created => created));
        }

        return cases;
    }

    /// <summary>
    /// The source of a status with an <c>Invalid</c> rule: its lifecycle, with walks that break a stamp in the requested way (or in one of them per object).
    /// </summary>
    /// <exception cref="InvalidOperationException">The lifecycle stamps nothing on the object, so there is nothing to break.</exception>
    public static SlotDecision Resolve(string owner, IReadOnlyList<FakeMember> members, int status, LifecycleDefinition lifecycle, ConstraintRequest request, Func<int, bool> hasRule)
    {
        var cases = Invalid(members, lifecycle, hasRule);
        if (cases.Count == 0)
        {
            throw new InvalidOperationException(
                $"{owner}.{members[status].Name} has a lifecycle that stamps no member, so Invalid has nothing to break. Tie a date member to a state with .Stamp(state, x => x.PaidAt) "
                + "(or name it after the state, like PaidAt), or remove the Invalid.");
        }

        var broken = lifecycle.WithBreaks(request.Index is { } index ? [cases[index]] : [.. cases]);
        return new SlotDecision(RuleKind.Convention, (Func<FakeScope, object?>)broken.Status, ObjectBound: true, Lifecycle: broken);
    }

    private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
