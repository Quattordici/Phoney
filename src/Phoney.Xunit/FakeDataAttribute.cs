using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Phoney.Generation;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Phoney.Xunit;

/// <summary>
/// Fills the parameters of an xUnit.net v3 <c>[Theory]</c> with Phoney fake data.
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
/// <see cref="Seed"/> to get other values, or set the <c>PHONEY_SEED</c> environment variable to move the seed of every
/// <c>[FakeData]</c> test at once (<c>PHONEY_SEED=123 dotnet test</c> runs the suite on other data; use the same value to replay it).
/// </para>
/// <para>
/// Custom conventions registered with <see cref="Fake.Conventions"/> and source-generated models
/// (<see cref="FakeForAttribute{T}"/>) are used for matching parameters and types. Test data is created while xUnit discovers tests,
/// so register conventions before that, for example in a module initializer.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Theory, FakeData]
/// public void Places_order(Customer customer, string email, [Range(1, 10)] int quantity) { … }
///
/// [Theory, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
/// public void Formats_address(string street, string city, string zipCode) { … }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class FakeDataAttribute : DataAttribute
{
    private readonly FakeDataOptions _options = new();

    /// <summary>Number of test cases to generate. Default 1.</summary>
    public int Count
    {
        get => _options.Count;
        set => _options.Count = value;
    }

    /// <summary>Seed for the values. Default: derived from the test's name, so values are stable between runs.</summary>
    public long Seed
    {
        get => _options.Seed ?? 0;
        set => _options.Seed = value;
    }

    /// <summary>Locale of the data, e.g. <c>sv</c> or <c>de-AT</c>. Default: <see cref="Fake.Locale"/>.</summary>
    public string? Locale
    {
        get => _options.Locale;
        set => _options.Locale = value;
    }

    /// <summary>Probability (0–1) that nullable parameters (<c>string?</c>, <c>int?</c>…) are <see langword="null"/>. Default 0.</summary>
    public double NullProbability
    {
        get => _options.NullProbability;
        set => _options.NullProbability = value;
    }

    /// <summary>
    /// The point in time relative dates (past, recent, birthdates, ages) are computed from, in ISO 8601
    /// (<c>2030-01-01</c>). Default: <see cref="Faker.DefaultSeededReferenceDate"/>.
    /// </summary>
    public string? ReferenceDate
    {
        get => _options.ReferenceDate;
        set => _options.ReferenceDate = value;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "[FakeData] reads the parameters of test methods with reflection by design; xUnit test projects are not trimmed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "[FakeData] reads the parameters of test methods with reflection by design; xUnit test projects are not published with Native AOT.")]
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        var arguments = testMethod.GetParameters().Select(FakeArgument.From).ToList();
        var generator = _options.CreateGenerator(arguments, FakeDataOptions.TestName(testMethod.DeclaringType, testMethod.Name));

        // Rows depend on the seed and their position only, so discovery and execution see the same values.
        var rows = new List<ITheoryDataRow>(Count);
        for (var i = 0; i < Count; i++)
            rows.Add(new TheoryDataRow(generator.Row(i)));
        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}
