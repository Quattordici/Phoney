using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Phoney.Generation;

/// <summary>
/// The settings and seed rules that the <c>[FakeData]</c> attributes of the test framework packages (<c>Phoney.TUnit</c>, <c>Phoney.Xunit</c>,
/// <c>Phoney.NUnit</c>) share, so that a test gets the same data whichever framework runs it.
/// </summary>
/// <remarks>
/// Without a <see cref="Seed"/> the seed is derived from the test's name and the generated arguments, so every run and machine gets the same values;
/// the <c>PHONEY_SEED</c> environment variable moves the derived seeds of a whole suite at once (see <see cref="FakeSeed"/>).
/// </remarks>
public sealed class FakeDataOptions
{
    private int _count = 1;
    private double _nullProbability;

    /// <summary>Number of test cases to generate. Default 1.</summary>
    public int Count
    {
        get => _count;
        set => _count = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Count), value, "At least one test case is needed.");
    }

    /// <summary>Seed for the values, or <see langword="null"/> (the default) to derive a stable one from the test's name.</summary>
    public long? Seed { get; set; }

    /// <summary>Locale of the data, e.g. <c>sv</c> or <c>de-AT</c>. Default: <see cref="Fake.Locale"/>.</summary>
    public string? Locale { get; set; }

    /// <summary>Probability (0–1) that nullable parameters (<c>string?</c>, <c>int?</c>…) are <see langword="null"/>. Default 0.</summary>
    public double NullProbability
    {
        get => _nullProbability;
        set => _nullProbability = value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(NullProbability), value, "Must be between 0 and 1.");
    }

    /// <summary>The point in time relative dates are computed from, in ISO 8601 (<c>2030-01-01</c>). Default: <see cref="Faker.DefaultSeededReferenceDate"/>.</summary>
    public string? ReferenceDate { get; set; }

    /// <summary>Creates the generator of the test's rows, with the <c>PHONEY_SEED</c> environment variable (if set) moving the derived seed.</summary>
    /// <param name="arguments">The parameters or properties to fill.</param>
    /// <param name="testName">Identifies the test: its class's full name and method, e.g. <c>Shop.OrderTests.Places_order</c>.</param>
    [RequiresUnreferencedCode("Populates arguments with reflection.")]
    [RequiresDynamicCode("Populates arguments with reflection.")]
    public ArgumentGenerator CreateGenerator(IReadOnlyList<FakeArgument> arguments, string testName) =>
        CreateGenerator(arguments, testName, FakeSeed.FromEnvironment());

    /// <summary>Creates the generator of the test's rows; <paramref name="environmentSeed"/> is the <c>PHONEY_SEED</c> value, if any.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="testName"/> is empty.</exception>
    [RequiresUnreferencedCode("Populates arguments with reflection.")]
    [RequiresDynamicCode("Populates arguments with reflection.")]
    public ArgumentGenerator CreateGenerator(IReadOnlyList<FakeArgument> arguments, string testName, long? environmentSeed)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        var generator = new ArgumentGenerator(arguments)
            .Seed(ResolveSeed(Seed, testName, arguments, environmentSeed))
            .NullProbability(NullProbability);
        if (Locale is { } locale)
            generator = generator.Locale(locale);
        if (ReferenceDate is { } date)
            generator = generator.ReferenceDate(DateTimeOffset.Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        return generator;
    }

    /// <summary>
    /// The seed of the rows: the explicit one; else the stable one derived from the test, moved by <c>PHONEY_SEED</c> when that is set, so
    /// a whole suite can run on other data (and be replayed with the same value) while each test keeps its own data.
    /// </summary>
    public static long ResolveSeed(long? explicitSeed, string testName, IEnumerable<FakeArgument> arguments, long? environmentSeed)
    {
        if (explicitSeed is { } seed)
            return seed;

        var derived = DefaultSeed(testName, arguments);
        return environmentSeed is { } moved ? unchecked(derived ^ ((moved + 1) * unchecked((long)0x9E3779B97F4A7C15UL))) : derived;
    }

    /// <summary>
    /// A seed that depends only on the test's name and the generated arguments, hashed with FNV-1a rather than <see cref="string.GetHashCode()"/>
    /// (which differs per process), so it is the same on every run and machine. Changing it would move the data of every existing test.
    /// </summary>
    public static long DefaultSeed(string testName, IEnumerable<FakeArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var identity = new StringBuilder(testName);
        foreach (var argument in arguments)
            identity.Append('|').Append(argument.Type.FullName).Append(' ').Append(argument.Name);

        const ulong offset = 14695981039346656037, prime = 1099511628211;
        var hash = offset;
        foreach (var c in identity.ToString())
        {
            hash = (hash ^ (byte)c) * prime;
            hash = (hash ^ (byte)(c >> 8)) * prime;
        }

        return unchecked((long)hash);
    }
}
