namespace Phoney;

/// <summary>
/// The draws of one generated object, for <c>ObjectGenerator&lt;T&gt;.Check</c>: while recording, every 64-bit draw of the randomizers that share the
/// tape is kept in order; while replaying, the draws come from the tape instead, so changing an entry changes the object the same generator makes.
/// </summary>
/// <remarks>
/// Every value Phoney makes goes through <see cref="SeededRandom.NextUInt64"/>, and smaller draws give smaller values (a lower index, a shorter
/// length, a smaller number, the first element), so a smaller tape is a simpler object made by the same rules. Past its end a replayed tape gives
/// <see cref="Simplest"/>, and a replay that draws far beyond it (a loop that rejects every value) stops with <see cref="ChoiceTapeOverrunException"/>.
/// </remarks>
internal sealed class ChoiceTape
{
    /// <summary>
    /// The simplest draw: 1 maps to 0 in every range and to 0.0 as a double. Zero itself would map to 0 as well, but the unbiased bounded draw rejects it
    /// for ranges that aren't a power of two, so a tape padded with zeros could loop.
    /// </summary>
    public const ulong Simplest = 1;

    private readonly ulong[]? _replay;
    private readonly List<ulong>? _recorded;
    private readonly int _limit;

    private ChoiceTape(ulong[]? replay)
    {
        _replay = replay;
        if (replay is null)
            _recorded = [];
        else
            _limit = (replay.Length * 2) + 10_000;
    }

    /// <summary>A tape that records the draws of the randomizers it is attached to.</summary>
    public static ChoiceTape Record() => new(null);

    /// <summary>A tape that replays <paramref name="draws"/>, then <see cref="Simplest"/>.</summary>
    public static ChoiceTape Replay(ulong[] draws) => new(draws);

    /// <summary>How many draws have been made so far.</summary>
    public int Position { get; private set; }

    /// <summary>The draws recorded so far, or the part of a replayed tape that was used (with the padding it drew past its end).</summary>
    public ulong[] Draws()
    {
        if (_recorded is not null)
            return [.. _recorded];
        var used = new ulong[Position];
        var copied = Math.Min(Position, _replay!.Length);
        Array.Copy(_replay, used, copied);
        used.AsSpan(copied).Fill(Simplest);
        return used;
    }

    /// <summary>The next draw: <paramref name="source"/>'s own while recording, the tape's while replaying.</summary>
    public ulong Next(SeededRandom source)
    {
        if (_recorded is not null)
        {
            var value = source.NextState();
            _recorded.Add(value);
            Position++;
            return value;
        }

        var position = Position++;
        if (position < _replay!.Length)
            return _replay[position];
        if (position > _limit)
            throw new ChoiceTapeOverrunException();
        return Simplest;
    }
}

/// <summary>A replayed tape was drawn far past its end: the object it describes can't be made, so it is not a counterexample.</summary>
#pragma warning disable CA1032, RCS1194, S3871 // internal control flow, caught by the check and never thrown to callers
internal sealed class ChoiceTapeOverrunException() : Exception("The replayed draws ran out.");
#pragma warning restore CA1032, RCS1194, S3871
