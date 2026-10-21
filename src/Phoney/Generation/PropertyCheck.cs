using System.Globalization;

namespace Phoney.Generation;

/// <summary>
/// One run of <c>ObjectGenerator&lt;T&gt;.Check</c>, apart from how the property is called (so the synchronous and asynchronous checks share it):
/// <see cref="Objects"/> yields the objects to test, the caller reports each result with <see cref="Report"/>, and when one fails the run shrinks it
/// by replaying simpler draws (<see cref="TapeShrinker"/>) and ends with <see cref="Failure"/>.
/// </summary>
/// <param name="record">Creates object <c>index</c> of the run while the tape records its draws.</param>
/// <param name="replay">Creates object <c>index</c> again from the draws of a tape.</param>
/// <param name="count">How many objects to test.</param>
/// <param name="maxShrinks">How many candidates shrinking may try.</param>
/// <param name="seed">The seed of the run, for the report.</param>
internal sealed class PropertyCheck<T>(Func<long, ChoiceTape, T> record, Func<long, ChoiceTape, T> replay, int count, int maxShrinks, CheckSeed seed)
{
    private Outcome _last;

    /// <summary>The failure to throw once <see cref="Objects"/> is done, or <see langword="null"/> when the property held for every object.</summary>
    public FakeCheckException<T>? Failure { get; private set; }

    /// <summary>Reports how the property did for the object just yielded.</summary>
    public void Report(Outcome outcome) => _last = outcome;

    /// <summary>The objects to test, in order; each must be followed by a <see cref="Report"/>.</summary>
    public IEnumerable<T> Objects()
    {
        for (var index = 0; index < count; index++)
        {
            var tape = ChoiceTape.Record();
            var item = record(index, tape);
            _last = Outcome.Passed;
            yield return item;
            if (!_last.Failed)
                continue;

            var original = (Item: item, Outcome: _last);
            var best = original;
            var steps = 0;
            if (maxShrinks > 0)
            {
                var shrinker = new TapeShrinker(tape.Draws(), maxShrinks);
                // The original object again from its own draws: when that differs (a Unique value that was taken by an earlier object), there is nothing to shrink from.
                if (TryReplay(index, shrinker.Best, out var again, out _))
                {
                    yield return again;
                    if (_last.SameFailureAs(original.Outcome))
                    {
                        best = (again, _last);
                        foreach (var candidate in shrinker.Candidates())
                        {
                            if (!TryReplay(index, candidate, out var simpler, out var used))
                            {
                                shrinker.Report(null);
                                continue;
                            }

                            yield return simpler;
                            if (shrinker.Report(_last.SameFailureAs(original.Outcome) ? used : null))
                                best = (simpler, _last);
                        }

                        steps = shrinker.Steps;
                    }
                }
            }

            Failure = new FakeCheckException<T>(Message(index, original.Item, best.Item, best.Outcome, steps), best.Outcome.Exception, seed.Value, index, index + 1, steps, original.Item, best.Item);
            yield break;
        }
    }

    /// <summary>Makes the object of a candidate tape; a candidate the generator can't make (its draws run out, a condition is never met) is no counterexample.</summary>
    private bool TryReplay(long index, ulong[] draws, out T item, out ulong[] used)
    {
        var tape = ChoiceTape.Replay(draws);
        try
        {
#pragma warning disable CA1031 // any failure to make the candidate only means it is not a counterexample
            item = replay(index, tape);
        }
        catch (Exception)
        {
            item = default!;
            used = [];
            return false;
        }
#pragma warning restore CA1031

        used = tape.Draws();
        return true;
    }

    private string Message(int index, T original, T counterexample, Outcome outcome, int steps)
    {
        var name = typeof(T).Name;
        var because = outcome.Exception is { } exception ? $"{exception.GetType().Name}: {exception.Message}" : "the property returned false";
        var shrunk = steps == 0 ? "could not be simplified" : $"shrunk in {steps} step{(steps == 1 ? "" : "s")}";
        var text = new System.Text.StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"The property failed for {name} number {index + 1} of {count} (seed {seed.Value}, {shrunk}).").AppendLine()
            .AppendLine("Counterexample:")
            .Append("  ").AppendLine(ObjectDescription.Of(counterexample))
            .Append("Because: ").AppendLine(because);
        if (steps > 0)
            text.AppendLine("Original:").Append("  ").AppendLine(ObjectDescription.Of(original));
        return text.Append(seed.Replay).ToString();
    }

    /// <summary>How the property did for one object: held, returned false, or threw <see cref="Exception"/>.</summary>
    internal readonly record struct Outcome(bool Failed, Exception? Exception)
    {
        public static Outcome Passed => default;

        public static Outcome ReturnedFalse => new(true, null);

        public static Outcome Threw(Exception exception) => new(true, exception);

        /// <summary>
        /// Whether this is the same kind of failure as <paramref name="original"/>: both returned false, or both threw the same exception type. Shrinking keeps
        /// to it, so it doesn't trade the bug it found for another one that a simpler object happens to hit.
        /// </summary>
        public bool SameFailureAs(Outcome original) => Failed && Exception?.GetType() == original.Exception?.GetType();
    }
}

/// <summary>The seed of a check and where it came from, for the replay hint.</summary>
internal readonly record struct CheckSeed(long Value, CheckSeed.Origin From)
{
    public enum Origin { Generator, Environment, Random }

    /// <summary>How to run the same check again.</summary>
    public string Replay => From switch
    {
        Origin.Generator => $"Replay: the generator's Seed({Value}) finds and shrinks it again.",
        Origin.Environment => $"Replay: the seed came from {FakeSeed.EnvironmentVariable}; run again with {FakeSeed.EnvironmentVariable}={Value}.",
        _ => $"Replay: {FakeSeed.EnvironmentVariable}={Value} dotnet test, or Seed({Value}) on the generator.",
    };
}
