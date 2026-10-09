using System.Globalization;
using System.Text;
using Phoney.Generation;
using TUnit.Core;

namespace Phoney.TUnit;

/// <summary>
/// Fills test method parameters, test class constructor parameters or a <c>required</c> property with Phoney fake data.
/// </summary>
/// <remarks>
/// <para>
/// Values are chosen like the members of one generated object: by name and type (<c>email</c> gets an email address,
/// <c>createdAt</c> a past date, <c>Customer customer</c> a populated customer), honouring data annotations on the
/// parameters (<c>[Range(1, 10)] int quantity</c>, <c>[EmailAddress] string contact</c>). All values of one test case
/// describe the same person, place and timeline, so <c>firstName</c>, <c>email</c>, <c>birthDate</c> and <c>age</c> agree.
/// </para>
/// <para>
/// Values are reproducible: without a <see cref="Seed"/>, the seed is derived from the test's class, method and
/// parameter types, so every run and machine gets the same values and a failing test fails again. Set
/// <see cref="Seed"/> to get other values.
/// </para>
/// <para>
/// Custom conventions registered with <see cref="Fake.Conventions"/> and source-generated models
/// (<see cref="FakeForAttribute{T}"/>) are used for matching parameters and types. Values are generated while TUnit
/// discovers tests, so register conventions in a <c>[Before(TestDiscovery)]</c> hook; later hooks run too late.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Test, FakeData]
/// public async Task Places_order(Customer customer, string email, [Range(1, 10)] int quantity) { … }
///
/// [Test, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
/// public async Task Formats_address(string street, string city, string zipCode) { … }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true)]
public sealed class FakeDataAttribute : UntypedDataSourceGeneratorAttribute
{
    private long? _seed;
    private int _count = 1;
    private double _nullProbability;

    /// <summary>Number of test cases to generate. Default 1.</summary>
    public int Count
    {
        get => _count;
        set => _count = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Count), value, "At least one test case is needed.");
    }

    /// <summary>Seed for the values. Default: derived from the test's name, so values are stable between runs.</summary>
    public long Seed
    {
        get => _seed ?? 0;
        set => _seed = value;
    }

    /// <summary>Locale of the data, e.g. <c>sv</c> or <c>de-AT</c>. Default: <see cref="Fake.Locale"/>.</summary>
    public string? Locale { get; set; }

    /// <summary>Probability (0–1) that nullable parameters (<c>string?</c>, <c>int?</c>…) are <see langword="null"/>. Default 0.</summary>
    public double NullProbability
    {
        get => _nullProbability;
        set => _nullProbability = value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(NullProbability), value, "Must be between 0 and 1.");
    }

    /// <summary>
    /// The point in time relative dates (past, recent, birthdates, ages) are computed from, in ISO 8601
    /// (<c>2030-01-01</c>). Default: <see cref="Faker.DefaultSeededReferenceDate"/>.
    /// </summary>
    public string? ReferenceDate { get; set; }

    /// <inheritdoc />
    protected override IEnumerable<Func<object?[]?>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata)
    {
        ArgumentNullException.ThrowIfNull(dataGeneratorMetadata);
        var arguments = dataGeneratorMetadata.MembersToGenerate.Select(ToArgument).ToList();
        var generator = CreateGenerator(arguments, dataGeneratorMetadata.TestInformation);

        // Each test case recreates its row on demand, so retried or re-instantiated tests get fresh, identical values.
        for (var i = 0; i < Count; i++)
        {
            var index = i;
            yield return () => generator.Row(index);
        }
    }

    /// <summary>Configures the generator from the attribute's properties.</summary>
    internal ArgumentGenerator CreateGenerator(IReadOnlyList<FakeArgument> arguments, MethodMetadata? test)
    {
        var generator = new ArgumentGenerator(arguments)
            .Seed(_seed ?? DefaultSeed(test, arguments))
            .NullProbability(NullProbability);
        if (Locale is { } locale)
            generator = generator.Locale(locale);
        if (ReferenceDate is { } date)
            generator = generator.ReferenceDate(DateTimeOffset.Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        return generator;
    }

    /// <summary>
    /// A seed that depends only on the test's identity and the generated members, hashed with FNV-1a rather than
    /// <see cref="string.GetHashCode()"/> (which differs per process), so it is the same on every run and machine.
    /// </summary>
    internal static long DefaultSeed(MethodMetadata? test, IEnumerable<FakeArgument> arguments)
    {
        var identity = new StringBuilder();
        identity.Append(test?.Class?.Type.FullName).Append('.').Append(test?.Name);
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

    /// <summary>
    /// Describes a parameter or property TUnit asks for, preferring its reflection info (which carries data
    /// annotations and nullability) and falling back to the type and name from TUnit's metadata.
    /// </summary>
    private static FakeArgument ToArgument(IMemberMetadata member) => member switch
    {
        ParameterMetadata parameter => TryReflectionInfo(() => parameter.ReflectionInfo) is { } info
            ? FakeArgument.From(info)
            : new FakeArgument(parameter.Name, parameter.Type, parameter.IsNullable),
        PropertyMetadata property => TryReflectionInfo(() => property.ReflectionInfo) is { } info
            ? FakeArgument.From(info)
            : new FakeArgument(property.Name, property.Type, property.IsNullable),
        _ => throw new NotSupportedException($"[FakeData] can't generate '{member.Name}' ({member.GetType().Name})."),
    };

    /// <summary>Reflection info can be unavailable (or fail to resolve) in TUnit's source-generated mode.</summary>
    private static T? TryReflectionInfo<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or MissingMemberException)
        {
            return null;
        }
    }
}
