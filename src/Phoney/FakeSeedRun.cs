using System.Globalization;

namespace Phoney;

/// <summary>
/// One seeded test run, from <see cref="FakeSeed.Begin"/>: the <see cref="Fake"/> facade of this async flow is seeded with <see cref="Seed"/> until the run is
/// disposed, and <see cref="Failed"/> says how to replay the test. Test-framework attributes (<c>[FakeSeed]</c>) open one around each test; use it
/// directly to give another framework the same attribute.
/// </summary>
/// <remarks>
/// Runs nest like <see cref="Fake.Seeded"/> scopes (an assembly, a class and a method can each open one): the innermost open run seeds the facade and is
/// <see cref="Current"/>, and a failure is reported once, by the innermost run that hears of it. The runs around a run that reported stay quiet, while a
/// run that ended without a failure leaves the report to the run around it.
/// </remarks>
/// <example>
/// <code>
/// // A before/after hook of a test framework
/// void Before() => FakeSeed.Begin(pinned, "[FakeSeed({0})]");
/// void After(bool failed) => FakeSeedRun.Current?.End(failed, output.WriteLine);
/// // Phoney: this test ran with random seed 481516234. Replay it with [FakeSeed(481516234)] or PHONEY_SEED=481516234.
/// </code>
/// </example>
public sealed class FakeSeedRun : IDisposable
{
    private static readonly AsyncLocal<FakeSeedRun?> s_current = new();

    private readonly FakeSeedRun? _outer;
    private readonly IDisposable _scope;
    private readonly Origin _origin;
    private readonly string? _pinHint;
    private volatile bool _disposed;
    private volatile bool _reported;

    private FakeSeedRun(long seed, Origin origin, string? pinHint, FakeSeedRun? outer)
    {
        (Seed, _origin, _pinHint, _outer) = (seed, origin, pinHint, outer);
        _scope = Fake.Seeded(seed);
    }

    /// <summary>The seed the facade runs with.</summary>
    public long Seed { get; }

    /// <summary>
    /// The innermost run of this async flow that is still open, or <see langword="null"/>. A task started inside a run keeps a copy of it, so the copy can be
    /// a run its creator has disposed since; that one is skipped like any other ended run.
    /// </summary>
    public static FakeSeedRun? Current => OpenFrom(s_current.Value);

    /// <summary>Opens a run in this async flow; see <see cref="FakeSeed.Begin"/>.</summary>
    internal static FakeSeedRun Begin(long? pinned, string? pinHint)
    {
        if (pinHint is not null && !pinHint.Contains("{0}", StringComparison.Ordinal))
            throw new ArgumentException($"The pin hint says how to pin a seed and must hold {{0}} where the seed goes, like \"[FakeSeed({{0}})]\", but was \"{pinHint}\".", nameof(pinHint));

        var (seed, origin) = Choose(pinned);
        var run = new FakeSeedRun(seed, origin, pinHint, Current);
        s_current.Value = run;
        return run;
    }

    /// <summary>The pinned seed, else <c>PHONEY_SEED</c>, else a new random one, with where it came from.</summary>
    private static (long Seed, Origin Origin) Choose(long? pinned)
    {
        if (pinned is { } value)
            return (value, Origin.Pinned);
        return FakeSeed.FromEnvironment() is { } environment ? (environment, Origin.Environment) : (FakeSeed.New(), Origin.Random);
    }

    /// <summary>
    /// Tells the run that its test failed: <paramref name="report"/> gets one line with the seed and how to replay it, unless this run or a run opened inside
    /// it has reported already. The runs around this one won't report the same failure again.
    /// </summary>
    /// <param name="report">Where the line goes: the test output of the framework.</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    public void Failed(Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (_reported)
            return;
        for (var run = this; run is not null; run = run._outer)
            run._reported = true;
        report(Describe());
    }

    /// <summary>Ends the run as a before/after hook does when its test is over: <see cref="Failed"/> when it <paramref name="failed"/>, then <see cref="Dispose"/>.</summary>
    /// <param name="failed">Whether the test failed (not skipped, not inconclusive).</param>
    /// <param name="report">Where the line goes when it failed: the test output of the framework.</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    public void End(bool failed, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            if (failed)
                Failed(report);
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>
    /// Ends the run: the facade goes back to the nearest enclosing run or scope that is still open. Disposing out of order never brings back a run that has
    /// ended.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _scope.Dispose();
        if (ReferenceEquals(s_current.Value, this))
            s_current.Value = OpenFrom(_outer);
    }

    private static FakeSeedRun? OpenFrom(FakeSeedRun? run)
    {
        while (run is { _disposed: true })
            run = run._outer;
        return run;
    }

    private enum Origin { Random, Environment, Pinned }

    private string Describe() => _origin switch
    {
        Origin.Pinned => $"Phoney: this test ran with the pinned seed {Seed}.",
        Origin.Environment => $"Phoney: this test ran with seed {Seed} from {FakeSeed.EnvironmentVariable}. Replay it with {FakeSeed.EnvironmentVariable}={Seed}.",
        _ when _pinHint is null => $"Phoney: this test ran with random seed {Seed}. Replay it with {FakeSeed.EnvironmentVariable}={Seed}.",
        _ => $"Phoney: this test ran with random seed {Seed}. Replay it with {string.Format(CultureInfo.InvariantCulture, _pinHint, Seed)} or {FakeSeed.EnvironmentVariable}={Seed}.",
    };
}
