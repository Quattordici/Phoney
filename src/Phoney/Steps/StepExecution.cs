using System.Collections.Immutable;
using System.Globalization;

namespace Phoney.Generation;

/// <summary>
/// Runs one <see cref="StepRun{TState}"/>: creates the state, checks the invariants, then, while a step may run and the run goes on, picks one of the steps
/// that may run, makes its argument, applies it and checks the invariants again. Every choice is a draw from the run's faker, so the run is on its tape.
/// </summary>
/// <remarks>
/// Each step's draws lie together on the tape: one "go on?" draw (0 stops, so the simplest tape is the empty run), one for the step, then its argument's. The
/// shrinker deleting a run of draws therefore deletes whole steps, and the steps after them keep their own draws. Drawing the length up front would shift
/// every later step whenever one was deleted. One implementation serves both checks: with synchronous steps and invariants every <see cref="ValueTask"/> is
/// already complete, so <see cref="Run"/> finishes before it returns.
/// </remarks>
internal sealed class StepExecution<TState>(Func<TState> create, ImmutableArray<StepDefinition<TState>> steps, ImmutableArray<StepInvariant<TState>> invariants, int maxSteps)
{
    /// <summary>
    /// The run goes on while <c>Int(0, odds)</c> isn't 0: on average about half of <c>maxSteps</c>, so runs of every length up to the cap occur and the cap
    /// is reached now and then.
    /// </summary>
    private readonly int _odds = Math.Max(1, maxSteps / 2);

    /// <summary>Runs <paramref name="run"/> and tells how it did; the state is disposed when it is disposable, and the faker leaves the tape.</summary>
    public async ValueTask<PropertyCheck<StepRun<TState>>.Outcome> Run(StepRun<TState> run)
    {
        var faker = run.Faker;
        try
        {
            return await Steps(run, faker).ConfigureAwait(false);
        }
        finally
        {
            faker.Random.Tape = null;
            await DisposeAsync(run.State).ConfigureAwait(false);
        }
    }

    private async ValueTask<PropertyCheck<StepRun<TState>>.Outcome> Steps(StepRun<TState> run, Faker faker)
    {
        try
        {
            run.State = create();
        }
#pragma warning disable CA1031 // any exception fails the run; it becomes the inner exception of the report
        catch (Exception e)
        {
            return Failed(run, e, $"creating the state threw {Describe(e)}");
        }

        if (await Broken(run).ConfigureAwait(false) is { } broken)
            return broken;

        var arguments = steps.Select(s => s.Arguments?.Invoke()).ToArray();
        var enabled = new List<int>(steps.Length);
        for (var number = 1; number <= maxSteps; number++)
        {
            StepDefinition<TState> step;
            object? argument;
            try
            {
                Enabled(run.State, enabled);
                var start = faker.Random.Tape?.Position ?? 0;
                if (enabled.Count == 0 || faker.Random.Int(0, _odds) == 0)
                    break;
                var index = enabled[faker.Random.Index(enabled.Count)];
                step = steps[index];
                argument = arguments[index]?.Invoke(faker, run.State);
                run.Add(new TakenStep(step.Name, step.Arguments is not null, argument));
                run.Drew(start, faker.Random.Tape?.Position ?? 0);
            }
            catch (Exception e)
            {
                return Failed(run, e, $"choosing step {number} threw {Describe(e)}");
            }

            try
            {
                await step.Apply(run.State, argument).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return Failed(run, e, $"step {number} ({step.Name}) threw {Describe(e)}");
            }
#pragma warning restore CA1031

            if (await Broken(run).ConfigureAwait(false) is { } failed)
                return failed;
        }

        return PropertyCheck<StepRun<TState>>.Outcome.Passed;
    }

    private void Enabled(TState state, List<int> enabled)
    {
        enabled.Clear();
        for (var i = 0; i < steps.Length; i++)
        {
            if (steps[i].Enabled(state))
                enabled.Add(i);
        }
    }

    /// <summary>The failure of the first invariant that doesn't hold for the state, or <see langword="null"/> when they all hold.</summary>
    private async ValueTask<PropertyCheck<StepRun<TState>>.Outcome?> Broken(StepRun<TState> run)
    {
        foreach (var invariant in invariants)
        {
            try
            {
                if (await invariant.Holds(run.State).ConfigureAwait(false))
                    continue;
                run.Fail($"the invariant {invariant.Text} failed {After(run)}");
                return PropertyCheck<StepRun<TState>>.Outcome.ReturnedFalse;
            }
#pragma warning disable CA1031 // an assertion that throws is the invariant failing
            catch (Exception e)
            {
                return Failed(run, e, $"the invariant {invariant.Text} failed {After(run)}: {Describe(e)}");
            }
#pragma warning restore CA1031
        }

        return null;
    }

    private static PropertyCheck<StepRun<TState>>.Outcome Failed(StepRun<TState> run, Exception exception, string reason)
    {
        run.Fail(reason);
        return PropertyCheck<StepRun<TState>>.Outcome.Threw(exception);
    }

    private static string After(StepRun<TState> run) =>
        run.Steps.Count == 0 ? "right after the state was created" : string.Create(CultureInfo.InvariantCulture, $"after step {run.Steps.Count}");

    private static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message}";

    private static async ValueTask DisposeAsync(TState state)
    {
        switch (state)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
