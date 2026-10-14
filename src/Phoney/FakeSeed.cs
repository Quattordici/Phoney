using System.Globalization;

namespace Phoney;

/// <summary>
/// Seeds for tests that run on random data but must be replayable: a failing test reports its seed, and the seed brings the
/// same data back. A test framework integration (such as <c>Phoney.TUnit</c>'s <c>[FakeSeed]</c>) picks a seed with
/// <see cref="Pick"/>, runs the test in <see cref="Fake.Seeded"/> and prints the seed when the test fails.
/// </summary>
/// <example>
/// <code>
/// var seed = FakeSeed.Pick();             // PHONEY_SEED if set, else a new random seed
/// using var _ = Fake.Seeded(seed);
/// try { /* the test */ }
/// catch { Console.WriteLine($"Failed with seed {seed}; replay with PHONEY_SEED={seed}"); throw; }
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
}
