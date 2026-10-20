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
    private string? _referenceDateText;
    private DateTimeOffset? _referenceDate;

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

    /// <summary>The point in time relative dates are computed from, in ISO 8601 (<c>2030-01-01</c>, UTC unless it has an offset). Default: <see cref="Faker.DefaultSeededReferenceDate"/>.</summary>
    /// <exception cref="ArgumentException">The text is not a date, so the test would otherwise fail later with a bare <see cref="FormatException"/>.</exception>
    public string? ReferenceDate
    {
        get => _referenceDateText;
        set
        {
            if (value is null)
                _referenceDate = null;
            // A time alone would take today's date, and the seeded data would change every day.
            else if (HasDay(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                _referenceDate = date;
            else
                throw new ArgumentException($"ReferenceDate '{value}' is not a date with a day; use ISO 8601, such as 2030-01-01 or 2030-01-01T12:00:00Z.", nameof(ReferenceDate));
            _referenceDateText = value;
        }
    }

    /// <summary>Whether the text names a day: with <see cref="DateTimeStyles.NoCurrentDateDefault"/> (which <see cref="DateTimeOffset"/> doesn't accept) a time alone is 0001-01-01.</summary>
    private static bool HasDay(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal, out var date) && date.Year > 1;

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
        if (_referenceDate is { } date)
            generator = generator.ReferenceDate(date);
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
            identity.Append('|').Append(StableName(argument.Type)).Append(' ').Append(argument.Name);

        const ulong offset = 14695981039346656037, prime = 1099511628211;
        var hash = offset;
        foreach (var c in identity.ToString())
        {
            hash = (hash ^ (byte)c) * prime;
            hash = (hash ^ (byte)(c >> 8)) * prime;
        }

        return unchecked((long)hash);
    }

    /// <summary>
    /// Identifies a test for <see cref="DefaultSeed"/>: the full name of its class and its method name (<c>Shop.OrderTests.Places_order</c>), with the
    /// type arguments of a generic class named without their assembly versions, like <see cref="DefaultSeed"/> does for the arguments.
    /// </summary>
    public static string TestName(Type? testClass, string method) => $"{(testClass is null ? null : StableName(testClass))}.{method}";

    /// <summary>
    /// <see cref="Type.FullName"/> without the assembly names it puts in type arguments (<c>System.Nullable`1[[System.Int32, System.Private.CoreLib, Version=10.0.0.0, …]]</c>),
    /// whose versions differ between .NET 8 and .NET 10 and change with every release of the assembly, which would move the seed. Other names are unchanged.
    /// </summary>
    internal static string StableName(Type type)
    {
        if (type.HasElementType)
        {
            var suffix = type.IsArray ? (type.IsSZArray ? "[]" : "[" + new string(',', type.GetArrayRank() - 1) + "]") : type.IsPointer ? "*" : "&";
            return StableName(type.GetElementType()!) + suffix;
        }

        if (!type.IsGenericType || type.IsGenericTypeDefinition)
            return type.FullName ?? type.Name;
        var name = new StringBuilder(type.GetGenericTypeDefinition().FullName).Append('[');
        var arguments = type.GetGenericArguments();
        for (var i = 0; i < arguments.Length; i++)
            name.Append(i == 0 ? "[" : ",[").Append(StableName(arguments[i])).Append(']');
        return name.Append(']').ToString();
    }
}
