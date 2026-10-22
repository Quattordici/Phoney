using System.Globalization;

namespace Phoney.Generation;

/// <summary>
/// One run of a property check (<c>ObjectGenerator&lt;T&gt;.Check</c>, <c>ArgumentGenerator.Check</c>), apart from what is generated and how the property
/// is called: <see cref="Objects"/> yields the objects to test, each result is reported with <see cref="Report"/>, and when one fails the run shrinks it
/// by replaying simpler draws (<see cref="TapeShrinker"/>) and ends with <see cref="Failure"/>. <see cref="Run"/> and <see cref="RunAsync"/> are that
/// loop for synchronous and asynchronous properties.
/// </summary>
/// <param name="record">Creates object <c>index</c> of the run while the tape records its draws.</param>
/// <param name="replay">Creates object <c>index</c> again from the draws of a tape.</param>
/// <param name="count">How many objects to test.</param>
/// <param name="maxShrinks">How many candidates shrinking may try.</param>
/// <param name="seed">The seed of the run, for the report.</param>
/// <param name="subject">What an object is called in the report (<c>Cart</c>, <c>row</c>).</param>
/// <param name="describe">Describes an object on one line for the report.</param>
internal sealed class PropertyCheck<T>(Func<long, ChoiceTape, T> record, Func<long, ChoiceTape, T> replay, int count, int maxShrinks, CheckSeed seed, string subject, Func<T, string> describe)
{
    /// <summary>A check of generated objects, reported by their type's name and described member by member.</summary>
    public PropertyCheck(Func<long, ChoiceTape, T> record, Func<long, ChoiceTape, T> replay, int count, int maxShrinks, CheckSeed seed)
        : this(record, replay, count, maxShrinks, seed, typeof(T).Name, item => ObjectDescription.Of(item))
    {
    }

#pragma warning disable S1450 // shared by the iterators while they are suspended (Report sets _last between their steps), so they can't be locals
    private Outcome _last;
    private (T Item, Outcome Outcome) _best;
#pragma warning restore S1450

    /// <summary>The failure to throw once <see cref="Objects"/> is done, or <see langword="null"/> when the property held for every object.</summary>
    public FakeCheckException<T>? Failure { get; private set; }

    /// <summary>Reports how the property did for the object just yielded.</summary>
    public void Report(Outcome outcome) => _last = outcome;

    /// <summary>Runs the check with a synchronous property; throws <see cref="Failure"/> when it failed.</summary>
    public void Run(Func<T, Outcome> evaluate)
    {
        foreach (var item in Objects())
            Report(evaluate(item));
        ThrowIfFailed();
    }

    /// <summary>Runs the check with an asynchronous property, which fails when its task faults; throws <see cref="Failure"/> when it failed.</summary>
    public async Task RunAsync(Func<T, Task> property, CancellationToken cancellationToken)
    {
        foreach (var item in Objects())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await property(item).ConfigureAwait(false);
                Report(Outcome.Passed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // any exception is the property failing; it becomes the inner exception of the report
            catch (Exception e)
            {
                Report(Outcome.Threw(e));
            }
#pragma warning restore CA1031
        }

        ThrowIfFailed();
    }

    private void ThrowIfFailed()
    {
        if (Failure is { } failure)
            throw failure;
    }

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

            foreach (var simpler in Shrink(index, tape.Draws(), (item, _last)))
                yield return simpler;
            yield break;
        }
    }

    /// <summary>Shrinks the failing object of <paramref name="index"/> from its <paramref name="draws"/>, then sets <see cref="Failure"/>.</summary>
    private IEnumerable<T> Shrink(int index, ulong[] draws, (T Item, Outcome Outcome) original)
    {
        _best = original;
        var steps = 0;
        // The original object again from its own draws: when that differs (a Unique value that was taken by an earlier object), there is nothing to shrink from.
        if (maxShrinks > 0 && TryReplay(index, draws, out var again, out _))
        {
            yield return again;
            if (_last.SameFailureAs(original.Outcome))
            {
                _best = (again, _last);
                var shrinker = new TapeShrinker(draws, maxShrinks);
                foreach (var simpler in Candidates(index, shrinker, original.Outcome))
                    yield return simpler;
                steps = shrinker.Steps;
            }
        }

        Failure = new FakeCheckException<T>(Message(index, original.Item, _best.Item, _best.Outcome, steps), _best.Outcome.Exception, seed.Value, index, index + 1, steps, original.Item, _best.Item);
    }

    /// <summary>The objects of the shrinker's candidates; one that still fails like <paramref name="failure"/> and is simpler becomes the best one.</summary>
    private IEnumerable<T> Candidates(int index, TapeShrinker shrinker, Outcome failure)
    {
        foreach (var candidate in shrinker.Candidates())
        {
            if (!TryReplay(index, candidate, out var simpler, out var used))
            {
                shrinker.Report(null);
                continue;
            }

            yield return simpler;
            if (shrinker.Report(_last.SameFailureAs(failure) ? used : null))
                _best = (simpler, _last);
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
        var because = outcome.Exception is { } exception ? $"{exception.GetType().Name}: {exception.Message}" : "the property returned false";
        var stepWord = steps == 1 ? "step" : "steps";
        var shrunk = steps == 0 ? "could not be simplified" : $"shrunk in {steps} {stepWord}";
        var text = new System.Text.StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"The property failed for {subject} number {index + 1} of {count} (seed {seed.Value}, {shrunk}).").AppendLine()
            .AppendLine("Counterexample:")
            .Append("  ").AppendLine(describe(counterexample))
            .Append("Because: ").AppendLine(because);
        if (steps > 0)
            text.AppendLine("Original:").Append("  ").AppendLine(describe(original));
        return text.Append(seed.Replay).ToString();
    }

    /// <summary>How the property did for one object: held, returned false, or threw <see cref="Exception"/>.</summary>
    internal readonly record struct Outcome(bool Failed, Exception? Exception)
    {
        public static Outcome Passed => default;

        public static Outcome ReturnedFalse => new(true, null);

        public static Outcome Threw(Exception exception) => new(true, exception);

        /// <summary>How <paramref name="property"/> does: an exception is a failure too, also for a property that returns a bool.</summary>
        public static Outcome Of(Func<bool> property)
        {
            try
            {
                return property() ? Passed : ReturnedFalse;
            }
#pragma warning disable CA1031 // any exception is the property failing; it becomes the inner exception of the report
            catch (Exception e)
            {
                return Threw(e);
            }
#pragma warning restore CA1031
        }

        /// <summary>How <paramref name="property"/> does: it fails when it throws, so assertions of any test framework can be the property.</summary>
        public static Outcome Of(Action property) =>
            Of(() =>
            {
                property();
                return true;
            });

        /// <summary>
        /// Whether this is the same kind of failure as <paramref name="original"/>: both returned false, or both threw the same exception type. Shrinking keeps
        /// to it, so it doesn't trade the bug it found for another one that a simpler object happens to hit.
        /// </summary>
        public bool SameFailureAs(Outcome original) => Failed && Exception?.GetType() == original.Exception?.GetType();
    }
}

/// <summary>The seed of a check and where it came from, for the replay hint.</summary>
/// <param name="Value">The seed.</param>
/// <param name="From">Where it came from.</param>
/// <param name="Pin">How code pins this seed (<c>Seed(42) on the generator</c>, <c>[FakeCheck(Seed = 42)]</c>), or empty when code can't.</param>
internal readonly record struct CheckSeed(long Value, CheckSeed.Origin From, string Pin)
{
    /// <summary>How the report of a generator says to pin a seed; <c>{0}</c> is the seed.</summary>
    public const string GeneratorPin = "Seed({0}) on the generator";

    public enum Origin { Generator, Environment, Random }

    /// <summary>The generator's own seed, else <c>PHONEY_SEED</c>, else a new random one; <paramref name="pin"/> formats how to pin it (<c>{0}</c> is the seed), empty for no way.</summary>
    public static CheckSeed Choose(long? own, string pin = GeneratorPin)
    {
        if (own is { } seed)
            return Create(seed, Origin.Generator, pin);
        return FakeSeed.FromEnvironment() is { } environment ? Create(environment, Origin.Environment, pin) : Create(FakeSeed.New(), Origin.Random, pin);
    }

    private static CheckSeed Create(long value, Origin from, string pin) => new(value, from, string.Format(CultureInfo.InvariantCulture, pin, value));

    /// <summary>How to run the same check again.</summary>
    public string Replay => From switch
    {
        Origin.Generator when Pin.Length == 0 => "Replay: the same seed finds and shrinks it again.",
        Origin.Generator => $"Replay: {Pin} finds and shrinks it again.",
        Origin.Environment when Pin.Length == 0 => $"Replay: the seed came from {FakeSeed.EnvironmentVariable}; run again with {FakeSeed.EnvironmentVariable}={Value}.",
        Origin.Environment => $"Replay: the seed came from {FakeSeed.EnvironmentVariable}; run again with {FakeSeed.EnvironmentVariable}={Value}, or pin it with {Pin}.",
        _ when Pin.Length == 0 => $"Replay: {FakeSeed.EnvironmentVariable}={Value} dotnet test.",
        _ => $"Replay: {FakeSeed.EnvironmentVariable}={Value} dotnet test, or {Pin}.",
    };
}
