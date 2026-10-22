using System.Diagnostics.CodeAnalysis;

namespace Phoney.Generation;

/// <summary>
/// The settings of the <c>[FakeCheck]</c> attributes of the test framework packages (<c>Phoney.TUnit</c>), which run a test as a property check: once per
/// generated row of its parameters, shrinking a failing row to the simplest one that still fails (see <see cref="ArgumentGenerator.Check(Func{object?[], bool}, int, int)"/>).
/// </summary>
/// <remarks>
/// Unlike <c>[FakeData]</c>, the rows are new on every run, so a suite explores more data over time: the seed is <see cref="Seed"/> when set, else
/// <c>PHONEY_SEED</c>, else a new random one, and a failure reports it with <c>[FakeCheck(Seed = …)]</c> to replay it.
/// </remarks>
public sealed class FakeCheckOptions : FakeArgumentOptions
{
    /// <summary>How the report of a failed check says to pin its seed.</summary>
    public const string ReplayHint = "[FakeCheck(Seed = {0})]";

    private int _count = 100;
    private int _maxShrinks = 1000;

    /// <summary>How many rows to test. Default 100.</summary>
    public int Count
    {
        get => _count;
        set => _count = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Count), value, "A check needs at least one row.");
    }

    /// <summary>How many simpler rows shrinking may try; 0 reports the original row. Default 1000.</summary>
    public int MaxShrinks
    {
        get => _maxShrinks;
        set => _maxShrinks = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxShrinks), value, "Use 0 to report the original row without shrinking.");
    }

    /// <summary>Pins the seed, to replay a failure; <see langword="null"/> (the default) uses <c>PHONEY_SEED</c> or a new random seed on every run.</summary>
    public long? Seed { get; set; }

    /// <summary>Creates the generator of the rows to check.</summary>
    /// <param name="arguments">The parameters to fill.</param>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("Populates arguments with reflection.")]
    [RequiresDynamicCode("Populates arguments with reflection.")]
    public ArgumentGenerator CreateGenerator(IReadOnlyList<FakeArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var generator = new ArgumentGenerator(arguments).ReplayHint(ReplayHint);
        return Configure(Seed is { } seed ? generator.Seed(seed) : generator);
    }
}
