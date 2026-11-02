using TUnit.Core;
using TUnit.Core.Exceptions;
using TUnit.Core.Interfaces;

namespace Phoney.TUnit;

/// <summary>
/// Runs a test body with a seeded <see cref="Fake"/> facade and reports the seed when the body fails. A test has only one executor,
/// so the one it had before (<paramref name="inner"/>, e.g. from <c>[TestExecutor&lt;T&gt;]</c>) runs inside the seeded scope instead of being replaced.
/// </summary>
internal sealed class SeededTestExecutor(long? pinned, ITestExecutor? inner) : ITestExecutor
{
    /// <inheritdoc />
    public ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SeededRun.Execute(pinned, context.Output.WriteLine, inner is null ? action : () => inner.ExecuteTest(context, action));
    }
}

/// <summary>The seeding and reporting around a test body, apart from TUnit so it can be tested directly.</summary>
internal static class SeededRun
{
    /// <summary>How a TUnit test pins a seed, offered by the report of a random seed.</summary>
    public const string PinHint = "[FakeSeed({0})]";

    /// <summary>
    /// Runs <paramref name="action"/> in a <see cref="FakeSeed.Begin"/> run with the pinned seed, else <c>PHONEY_SEED</c>, else a new random one. When the
    /// body throws (a skip excepted), <paramref name="report"/> gets a line that says how to replay it and the exception is rethrown unchanged.
    /// </summary>
    public static async ValueTask Execute(long? pinned, Action<string> report, Func<ValueTask> action)
    {
        using var run = FakeSeed.Begin(pinned, PinHint);
        try
        {
            await action();
        }
        catch (Exception e) when (e is not SkipTestException)
        {
            run.Failed(report);
            throw;
        }
    }
}
