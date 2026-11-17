namespace Phoney.Generation;

/// <summary>
/// The walk of one object through a lifecycle: the states it went through (the start first, its status last) and the gap drawn before each transition. The
/// times of the states follow from the object's creation time and the reference date (<see cref="Times"/>), worked out once.
/// </summary>
internal sealed class LifecycleWalk(int[] states, long[] gaps)
{
    private DateTimeOffset[]? _times;

    /// <summary>The states the object went through, the start first.</summary>
    public int[] States { get; } = states;

    /// <summary>The state the object is in.</summary>
    public int Status => States[^1];

    /// <summary>
    /// When the object entered each of <see cref="States"/>: the start at <paramref name="created"/>, each later state its gap after the one before. Gaps that would
    /// end past <paramref name="reference"/> are scaled by one factor so the last transition lands on it at the latest, and an object created at or after it moves
    /// on at once. Integer arithmetic, so the times are the same on every platform.
    /// </summary>
    public DateTimeOffset[] Times(DateTimeOffset created, DateTimeOffset reference)
    {
        if (_times is not null)
            return _times;

        var times = new DateTimeOffset[States.Length];
        times[0] = created;
        Int128 total = 0;
        foreach (var gap in gaps)
            total += gap;
        Int128 available = Math.Max(0, (reference - created).Ticks);
        Int128 elapsed = 0;
        for (var i = 0; i < gaps.Length; i++)
        {
            elapsed += gaps[i];
            var ticks = total <= available ? elapsed : elapsed * available / total;
            times[i + 1] = created.AddTicks((long)ticks);
        }

        return _times = times;
    }

    /// <summary>When the object last entered <paramref name="state"/>, or <see langword="null"/> when it never did.</summary>
    public DateTimeOffset? Entered(int state, DateTimeOffset created, DateTimeOffset reference)
    {
        var last = Array.LastIndexOf(States, state);
        return last < 0 ? null : Times(created, reference)[last];
    }

    /// <summary>When the object took its last transition, or <paramref name="created"/> when it took none.</summary>
    public DateTimeOffset LastTransition(DateTimeOffset created, DateTimeOffset reference) => Times(created, reference)[^1];
}
