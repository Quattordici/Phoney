using System.Diagnostics.CodeAnalysis;
using Phoney.Generation;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Phoney.TUnit;

/// <summary>
/// Runs a test as a property check: the test runs once for each of <see cref="Count"/> generated rows of its parameters and, when it fails for one, the
/// row is <em>shrunk</em> to the simplest row that still fails, and the test fails with a <see cref="FakeCheckException{T}"/> that shows it.
/// </summary>
/// <remarks>
/// <para>
/// Parameters are filled like <c>[FakeData]</c> fills them (by name, type and data annotations; the values of a row agree), but the rows are new on every
/// run, so a suite explores more data over time. The seed is <see cref="Seed"/> when set, else <c>PHONEY_SEED</c>, else a new random one, and a failure
/// reports it as <c>[FakeCheck(Seed = …)]</c>, which replays the failure and the shrinking exactly.
/// </para>
/// <para>
/// The rows run one after the other on one instance of the test class, inside the executor the test already has (<c>[TestExecutor&lt;T&gt;]</c>), and
/// inside <c>[FakeSeed]</c> when the test has it. TUnit shows one test with the values of a sample row; the failure message names the failing row.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Test, FakeCheck]
/// public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =&gt;
///     Pricing.Total(order, discount).ShouldBeGreaterThanOrEqualTo(0);
/// // FakeCheckException: The property failed for row number 12 of 100 (seed 812734, shrunk in 31 steps).
/// // Counterexample:
/// //   order = Order { Lines = [OrderLine { Sku = "AAA-00000", Quantity = 1, Price = 0.01 }] }, discount = 2
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FakeCheckAttribute : UntypedDataSourceGeneratorAttribute, ITestRegisteredEventReceiver
{
    private readonly FakeCheckOptions _options = new();

    /// <summary>How many rows to test. Default 100.</summary>
    public int Count
    {
        get => _options.Count;
        set => _options.Count = value;
    }

    /// <summary>How many simpler rows shrinking may try; 0 reports the original row. Default 1000.</summary>
    public int MaxShrinks
    {
        get => _options.MaxShrinks;
        set => _options.MaxShrinks = value;
    }

    /// <summary>Pins the seed, to replay a failure. Default: <c>PHONEY_SEED</c>, else a new random seed on every run.</summary>
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

    /// <summary>Registers before <c>[FakeSeed]</c> (which registers last), so a seeded scope wraps the whole check.</summary>
    public int Order => int.MaxValue - 1;

    /// <inheritdoc />
    public ValueTask OnTestRegistered(TestRegisteredContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.SetTestExecutor(new CheckingTestExecutor(_options, context.DiscoveredTest?.TestExecutor));
        return default;
    }

    /// <summary>One test case with a sample row, stable for the test so its name doesn't change between runs; the executor gives the test its real rows.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; TUnit test projects are not trimmed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; TUnit test projects are not published with Native AOT.")]
    protected override IEnumerable<Func<object?[]?>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata)
    {
        ArgumentNullException.ThrowIfNull(dataGeneratorMetadata);
        var arguments = dataGeneratorMetadata.MembersToGenerate.Select(TestArguments.From).ToList();
        var test = dataGeneratorMetadata.TestInformation;
        var seed = FakeDataOptions.DefaultSeed(FakeDataOptions.TestName(test?.Class?.Type, test?.Name ?? ""), arguments);
        var sample = _options.CreateGenerator(arguments).Seed(seed);
        return [() => sample.Row(0)];
    }
}
