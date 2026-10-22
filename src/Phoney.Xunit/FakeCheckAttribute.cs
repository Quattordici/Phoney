using System.Runtime.CompilerServices;
using Phoney.Generation;
using Xunit;
using Xunit.v3;

namespace Phoney.Xunit;

/// <summary>
/// Runs an xUnit.net v3 test method as a property check: put it on a method <i>instead of</i> <c>[Fact]</c> or <c>[Theory]</c>. The test runs once for
/// each of <see cref="Count"/> generated rows of its parameters and, when it fails for one, the row is <em>shrunk</em> to the simplest row that still
/// fails, and the test fails with a <see cref="FakeCheckException{T}"/> that shows it.
/// </summary>
/// <remarks>
/// <para>
/// Parameters are filled like <c>[FakeData]</c> fills them (by name, type and data annotations; the values of a row agree), but the rows are new on every
/// run, so a suite explores more data over time. The seed is <see cref="Seed"/> when set, else <c>PHONEY_SEED</c>, else a new random one, and a failure
/// reports it as <c>[FakeCheck(Seed = …)]</c>, which replays the failure and the shrinking exactly.
/// </para>
/// <para>
/// The rows run one after the other on one instance of the test class, between the <c>[BeforeAfterTestAttribute]</c>s of the test, which run once.
/// xUnit.net shows one test named after the method; the failure message names the failing row. The options of <see cref="FactAttribute"/>
/// (<c>DisplayName</c>, <c>Skip</c>, <c>Timeout</c>, <c>Explicit</c>…) apply to the whole check.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [FakeCheck]
/// public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =&gt;
///     Assert.True(Pricing.Total(order, discount) &gt;= 0);
/// // FakeCheckException: The property failed for row number 12 of 100 (seed 812734, shrunk in 31 steps).
/// // Counterexample:
/// //   order = Order { Lines = [OrderLine { Sku = "AAA-00000", Quantity = 1, Price = 0.01 }] }, discount = 2
/// </code>
/// </example>
/// <param name="sourceFilePath">The file of the test method; filled in by the compiler.</param>
/// <param name="sourceLineNumber">The line of the test method; filled in by the compiler.</param>
[XunitTestCaseDiscoverer(typeof(FakeCheckDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FakeCheckAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
    : FactAttribute(sourceFilePath, sourceLineNumber)
{
    /// <summary>The settings, read again when the test runs.</summary>
    internal FakeCheckOptions Options { get; } = new();

    /// <summary>How many rows to test. Default 100.</summary>
    public int Count
    {
        get => Options.Count;
        set => Options.Count = value;
    }

    /// <summary>How many simpler rows shrinking may try; 0 reports the original row. Default 1000.</summary>
    public int MaxShrinks
    {
        get => Options.MaxShrinks;
        set => Options.MaxShrinks = value;
    }

    /// <summary>Pins the seed, to replay a failure. Default: <c>PHONEY_SEED</c>, else a new random seed on every run.</summary>
    public long Seed
    {
        get => Options.Seed ?? 0;
        set => Options.Seed = value;
    }

    /// <summary>Locale of the data, e.g. <c>sv</c> or <c>de-AT</c>. Default: <see cref="Fake.Locale"/>.</summary>
    public string? Locale
    {
        get => Options.Locale;
        set => Options.Locale = value;
    }

    /// <summary>Probability (0–1) that nullable parameters (<c>string?</c>, <c>int?</c>…) are <see langword="null"/>. Default 0.</summary>
    public double NullProbability
    {
        get => Options.NullProbability;
        set => Options.NullProbability = value;
    }

    /// <summary>
    /// The point in time relative dates (past, recent, birthdates, ages) are computed from, in ISO 8601
    /// (<c>2030-01-01</c>). Default: <see cref="Faker.DefaultSeededReferenceDate"/>.
    /// </summary>
    public string? ReferenceDate
    {
        get => Options.ReferenceDate;
        set => Options.ReferenceDate = value;
    }
}
