using TUnit.Core;
using TUnit.Core.Exceptions;
using TUnit.Core.Interfaces;

namespace Phoney.TUnit;

/// <summary>Runs a test body with a seeded <see cref="Fake"/> facade and reports the seed when the body fails.</summary>
internal sealed class SeededTestExecutor(long? pinned) : ITestExecutor
{
    /// <inheritdoc />
    public ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SeededRun.Execute(pinned, FakeSeed.FromEnvironment(), context.Output.WriteLine, action);
    }
}

/// <summary>The seeding and reporting around a test body, apart from TUnit so it can be tested directly.</summary>
internal static class SeededRun
{
    /// <summary>
    /// Runs <paramref name="action"/> in <see cref="Fake.Seeded(long, string?)"/> with the pinned seed, else the one from the environment,
    /// else a new random one. When the body throws (a skip excepted), <paramref name="report"/> gets a line that says how to replay it and
    /// the exception is rethrown unchanged.
    /// </summary>
    public static async ValueTask Execute(long? pinned, long? environment, Action<string> report, Func<ValueTask> action)
    {
        var seed = pinned ?? environment ?? FakeSeed.New();
        using var scope = Fake.Seeded(seed);
        try
        {
            await action();
        }
        catch (Exception e) when (e is not SkipTestException)
        {
            report(Describe(seed, pinned is not null ? Origin.Pinned : environment is not null ? Origin.Environment : Origin.Random));
            throw;
        }
    }

    private enum Origin { Random, Environment, Pinned }

    private static string Describe(long seed, Origin origin) => origin switch
    {
        Origin.Pinned => $"Phoney: this test ran with the pinned seed {seed}.",
        Origin.Environment => $"Phoney: this test ran with seed {seed} from {FakeSeed.EnvironmentVariable}. Replay it with {FakeSeed.EnvironmentVariable}={seed}.",
        _ => $"Phoney: this test ran with random seed {seed}. Replay it with [FakeSeed({seed})] or {FakeSeed.EnvironmentVariable}={seed}.",
    };
}
