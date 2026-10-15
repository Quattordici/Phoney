using System.Globalization;

namespace Phoney;

/// <summary>
/// Seeds for tests that run on random data but must be replayable: a failing test reports its seed, and the seed brings the
/// same data back. <see cref="Run"/> and <see cref="RunAsync"/> do it for any test framework: they pick a seed with
/// <see cref="Pick"/>, run the test in <see cref="Fake.Seeded"/> and report the seed when the test fails (<c>Phoney.TUnit</c>'s <c>[FakeSeed]</c>
/// does the same as an attribute).
/// </summary>
/// <example>
/// <code>
/// // xUnit.net v3: the seed of a failing run ends up in the test output
/// [Fact]
/// public void Totals_add_up() => FakeSeed.Run(() =>
/// {
///     var orders = Fake.Many&lt;Order&gt;(100);   // different on every run
///     Totals.Of(orders).ShouldBe(orders.Sum(o => o.Total));
/// }, TestContext.Current.SendDiagnosticMessage);
/// </code>
/// </example>
public static class FakeSeed
{
    /// <summary>Name of the environment variable that replays a run: <c>PHONEY_SEED=123456789 dotnet test</c>.</summary>
    public const string EnvironmentVariable = "PHONEY_SEED";

    /// <summary>
    /// The seed in the <c>PHONEY_SEED</c> environment variable, or <see langword="null"/> when it is not set (or blank). Read on every call.
    /// </summary>
    /// <exception cref="FormatException">The variable is set but is not a whole number.</exception>
    public static long? FromEnvironment()
    {
        var text = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            return seed;

        throw new FormatException($"{EnvironmentVariable} must be a whole number (the seed to replay), but was '{text}'.");
    }

    /// <summary>The seed to run with: <see cref="FromEnvironment"/> when it is set, otherwise a <see cref="New"/> one.</summary>
    /// <exception cref="FormatException"><c>PHONEY_SEED</c> is set but is not a whole number.</exception>
    public static long Pick() => FromEnvironment() ?? New();

    /// <summary>A new random seed, up to nine digits so it is easy to read out, copy and type. The environment is not consulted.</summary>
    public static long New() =>
#pragma warning disable RS0030 // A fresh random seed is the point; what it produces is then reproducible.
        System.Random.Shared.NextInt64(1, 1_000_000_000);
#pragma warning restore RS0030

    /// <summary>
    /// Runs <paramref name="body"/> on a seeded <see cref="Fake"/> facade and, when it throws, gives <paramref name="report"/> a line with the seed and how
    /// to replay it before the exception is rethrown unchanged. The seed is <paramref name="seed"/>, else <c>PHONEY_SEED</c>, else a new random one.
    /// </summary>
    /// <param name="body">The test.</param>
    /// <param name="report">Where the line goes: the test output of the framework (<c>TestContext.WriteLine</c>, <c>ITestOutputHelper.WriteLine</c>…).</param>
    /// <param name="seed">Pins the seed to replay a failure; it wins over <c>PHONEY_SEED</c>.</param>
    /// <remarks>
    /// Every exception is reported, so a test that is skipped by throwing (as some frameworks do) is reported too. Only the facade of the
    /// body's own flow is seeded: generators created earlier or with their own <c>.Seed(...)</c> keep theirs.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> or <paramref name="report"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><c>PHONEY_SEED</c> is set but is not a whole number (the body does not run).</exception>
    public static void Run(Action body, Action<string> report, long? seed = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(report);
        var (value, origin) = Choose(seed);
        using var scope = Fake.Seeded(value);
        try
        {
            body();
        }
        catch
        {
            report(Describe(value, origin));
            throw;
        }
    }

    /// <summary>The asynchronous form of <see cref="Run"/>: the seed stays in effect across <see langword="await"/>s of <paramref name="body"/>.</summary>
    /// <param name="body">The test.</param>
    /// <param name="report">Where the line goes: the test output of the framework.</param>
    /// <param name="seed">Pins the seed to replay a failure; it wins over <c>PHONEY_SEED</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> or <paramref name="report"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><c>PHONEY_SEED</c> is set but is not a whole number (the body does not run).</exception>
    public static async Task RunAsync(Func<Task> body, Action<string> report, long? seed = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(report);
        var (value, origin) = Choose(seed);
        using var scope = Fake.Seeded(value);
        try
        {
            await body().ConfigureAwait(false);
        }
        catch
        {
            report(Describe(value, origin));
            throw;
        }
    }

    private enum Origin { Random, Environment, Pinned }

    private static (long Seed, Origin Origin) Choose(long? pinned)
    {
        if (pinned is { } seed)
            return (seed, Origin.Pinned);
        return FromEnvironment() is { } fromEnvironment ? (fromEnvironment, Origin.Environment) : (New(), Origin.Random);
    }

    private static string Describe(long seed, Origin origin) => origin switch
    {
        Origin.Pinned => $"Phoney: the failing run used the pinned seed {seed}.",
        Origin.Environment => $"Phoney: the failing run used seed {seed} from {EnvironmentVariable}. Replay it with {EnvironmentVariable}={seed}.",
        _ => $"Phoney: the failing run used random seed {seed}. Replay it with {EnvironmentVariable}={seed} or FakeSeed.Run(..., seed: {seed}).",
    };
}
