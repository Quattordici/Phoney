namespace Phoney.Generation;

/// <summary>
/// Shrinks the draws of a failing object (a <see cref="ChoiceTape"/>) to the smallest draws that still fail, the way Hypothesis shrinks its choice
/// sequences: it proposes simpler tapes, the caller replays each and reports whether the object still fails, and a failing tape that is smaller
/// (shorter, or as long and lexicographically lower) becomes the best one. Deterministic: the same tape and answers give the same result.
/// </summary>
/// <remarks>
/// Because the objects are made again by the generator, a simpler tape is always an object the generator can make: rules, annotations and the
/// agreement of names, emails and dates hold for every candidate. Deleting draws removes collection elements and characters; lowering a draw lowers the
/// number, length or index it becomes.
/// </remarks>
internal sealed class TapeShrinker(ulong[] tape, int maxAttempts)
{
    private static readonly int[] BlockSizes = [8, 4, 2, 1];

    private bool _accepted;

    /// <summary>The smallest failing tape so far.</summary>
    public ulong[] Best { get; private set; } = tape;

    /// <summary>How many candidates were tried.</summary>
    public int Attempts { get; private set; }

    /// <summary>How many candidates replaced the best tape.</summary>
    public int Steps { get; private set; }

    private bool Exhausted => Attempts >= maxAttempts;

    /// <summary>
    /// Records the outcome of the candidate just proposed: <paramref name="used"/> are the draws its replay used when the object still fails the same
    /// way, <see langword="null"/> when it doesn't (or couldn't be made). Returns whether it became the best tape.
    /// </summary>
    public bool Report(ulong[]? used)
    {
        Attempts++;
        _accepted = used is not null && Compare(used, Best) < 0;
        if (_accepted)
        {
            Best = used!;
            Steps++;
        }

        return _accepted;
    }

    /// <summary>The candidates to replay, each followed by a call to <see cref="Report"/>; passes repeat until none of them improves the tape.</summary>
    public IEnumerable<ulong[]> Candidates()
    {
        var improved = true;
        while (improved && !Exhausted)
        {
            var steps = Steps;
            foreach (var candidate in DeleteBlocks())
                yield return candidate;
            foreach (var candidate in SimplifyBlocks())
                yield return candidate;
            foreach (var candidate in LowerDraws())
                yield return candidate;
            improved = Steps > steps;
        }
    }

    /// <summary>Removes runs of draws, from long runs to single ones: fewer elements, shorter texts, fewer retries.</summary>
    private IEnumerable<ulong[]> DeleteBlocks()
    {
        foreach (var size in BlockSizes)
        {
            for (var start = 0; start + size <= Best.Length && !Exhausted;)
            {
                yield return [.. Best.AsSpan(0, start), .. Best.AsSpan(start + size)];
                if (!_accepted)
                    start++; // after a deletion the next run starts at the same place
            }
        }
    }

    /// <summary>Sets runs of draws to the simplest draw at once, for values that only fail together (two lengths that must add up).</summary>
    private IEnumerable<ulong[]> SimplifyBlocks()
    {
        foreach (var size in BlockSizes)
        {
            for (var start = 0; start + size <= Best.Length && !Exhausted; start++)
            {
                if (!Best.AsSpan(start, size).ContainsAnyExcept(ChoiceTape.Simplest))
                    continue;
                var candidate = Best.ToArray();
                candidate.AsSpan(start, size).Fill(ChoiceTape.Simplest);
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Finds the smallest value of each draw that still fails by bisection. Draws become values through their high bits, so the search stops once the
    /// interval is a 2^-32 fraction of the value: exact for every range of up to 2^32 values.
    /// </summary>
    private IEnumerable<ulong[]> LowerDraws()
    {
        for (var i = 0; i < Best.Length && !Exhausted; i++)
        {
            var failing = Best[i];
            var passing = ChoiceTape.Simplest - 1; // 0, below every candidate: the simplest draw is tried first
            while (failing - passing > 1 && failing - passing > failing >> 32 && !Exhausted && i < Best.Length)
            {
                var value = passing == ChoiceTape.Simplest - 1 ? ChoiceTape.Simplest : passing + ((failing - passing) / 2);
                var candidate = Best.ToArray();
                candidate[i] = value;
                yield return candidate;
                if (!_accepted)
                {
                    passing = value;
                }
                else if (i < Best.Length && Best[i] <= value)
                {
                    failing = Best[i];
                }
                else
                {
                    break; // the replay shortened or reshaped the tape: this position means something else now
                }
            }
        }
    }

    /// <summary>Shortlex order: a shorter tape is smaller; tapes of one length compare draw by draw.</summary>
    internal static int Compare(ulong[] left, ulong[] right) =>
        left.Length != right.Length ? left.Length.CompareTo(right.Length) : left.AsSpan().SequenceCompareTo(right);
}
