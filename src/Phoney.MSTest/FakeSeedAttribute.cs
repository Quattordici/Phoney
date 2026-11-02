using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Phoney.MSTest;

/// <summary>
/// Runs an MSTest test on a seeded <see cref="Fake"/> facade: put it on a method <i>instead of</i> <c>[TestMethod]</c>. <c>Fake.One&lt;T&gt;()</c>,
/// <c>Fake.Many&lt;T&gt;()</c>, <c>Fake.For&lt;T&gt;()</c> and <c>Fake.Person…</c> are random from run to run, and when the test fails its seed is
/// printed in the test output, so the failure can be replayed. For every test of a class, use <see cref="FakeSeedClassAttribute"/> instead of
/// <c>[TestClass]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Without a number the seed is new on every run, or the value of the <c>PHONEY_SEED</c> environment variable when it is set
/// (<c>PHONEY_SEED=123456789 dotnet test</c> replays a whole run). <c>[FakeSeed(123456789)]</c> pins the seed of a test: use it to replay a failure
/// while you fix it. A pinned seed wins over <c>PHONEY_SEED</c>.
/// </para>
/// <para>
/// The seed applies to the facade of the test's own async flow, so parallel tests don't affect each other. It is open around the whole invocation
/// (<c>[TestInitialize]</c> and <c>[TestCleanup]</c> included), and every <c>[DataRow]</c> starts from it. Generators created before the test runs (a
/// <c>static</c> field) are not seeded by it, and neither are generators that call <c>.Seed(...)</c> themselves. Failed, errored, timed-out and aborted
/// tests report; other outcomes print nothing. On a method it wins over <see cref="FakeSeedClassAttribute"/>. To seed a <c>[FakeCheck]</c> test, put
/// <see cref="FakeSeedClassAttribute"/> on its class: a method takes only one test attribute.
/// </para>
/// <para><c>[FakeData]</c> values don't need this attribute: they are already reproducible, and follow <c>PHONEY_SEED</c> too.</para>
/// </remarks>
/// <example>
/// <code>
/// [FakeSeed]
/// public void Totals_add_up()
/// {
///     var orders = Fake.Many&lt;Order&gt;(100);   // different on every run
///     …                                       // on failure the output says: Replay it with [FakeSeed(481516234)] or PHONEY_SEED=481516234
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class FakeSeedAttribute : TestMethodAttribute
{
    /// <summary>How a test pins a seed, offered by the report of a random seed.</summary>
    internal const string PinHint = "[FakeSeed({0})]";

    /// <summary>Uses a new seed on every run (or <c>PHONEY_SEED</c>), and prints it when the test fails.</summary>
    /// <param name="callerFilePath">The file of the test method; filled in by the compiler.</param>
    /// <param name="callerLineNumber">The line of the test method; filled in by the compiler.</param>
    public FakeSeedAttribute([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
    }

    /// <summary>Pins the seed, to replay a failure.</summary>
    /// <param name="seed">The seed a failing run printed.</param>
    /// <param name="callerFilePath">The file of the test method; filled in by the compiler.</param>
    /// <param name="callerLineNumber">The line of the test method; filled in by the compiler.</param>
    public FakeSeedAttribute(long seed, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber) => Seed = seed;

    /// <summary>The pinned seed, or <see langword="null"/> when each run picks its own.</summary>
    public long? Seed { get; private init; }

    /// <summary>The test attribute of the method when <see cref="FakeSeedClassAttribute"/> wraps it (<c>[TestMethod]</c>, <c>[FakeCheck]</c>…).</summary>
    private TestMethodAttribute? Inner { get; init; }

    /// <summary>The attribute <see cref="FakeSeedClassAttribute"/> runs <paramref name="inner"/> through, with the same name and source position.</summary>
    internal static FakeSeedAttribute Around(TestMethodAttribute inner, long? seed) =>
#pragma warning disable MSTEST0056 // the arguments are the source position of the wrapped attribute, not a display name
        new(callerFilePath: inner.DeclaringFilePath ?? "", callerLineNumber: inner.DeclaringLineNumber ?? -1)
#pragma warning restore MSTEST0056
        {
            Seed = seed,
            Inner = inner,
        };

    /// <summary>Runs the test in a seeded run and writes the seed to the output of a result that failed.</summary>
    /// <param name="testMethod">The test method.</param>
    /// <returns>The results of the test, unchanged but for the line in the output of a failed one.</returns>
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        using var run = FakeSeed.Begin(Seed, PinHint);
        var results = Inner is null ? await base.ExecuteAsync(testMethod).ConfigureAwait(false) : await Inner.ExecuteAsync(testMethod).ConfigureAwait(false);
        foreach (var result in results.Where(r => r.Outcome is UnitTestOutcome.Failed or UnitTestOutcome.Error or UnitTestOutcome.Timeout or UnitTestOutcome.Aborted))
            run.Failed(line => result.LogOutput += line + Environment.NewLine);
        return results;
    }
}

/// <summary>
/// Runs every test of an MSTest class on a seeded <see cref="Fake"/> facade, like <see cref="FakeSeedAttribute"/> on each: put it on the class
/// <i>instead of</i> <c>[TestClass]</c>. It wraps whatever test attribute a method has (<c>[TestMethod]</c>, <c>[DataRow]</c> tests, <c>[FakeCheck]</c>),
/// and <c>[FakeSeed]</c> on a method wins over it.
/// </summary>
/// <example>
/// <code>
/// [FakeSeedClass]
/// public sealed class PricingTests
/// {
///     [TestMethod]
///     public void Totals_add_up() { … }   // seeded; on failure the output says how to replay it
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FakeSeedClassAttribute : TestClassAttribute
{
    /// <summary>Uses a new seed for every test on every run (or <c>PHONEY_SEED</c>), and prints it when the test fails.</summary>
    public FakeSeedClassAttribute()
    {
    }

    /// <summary>Pins the seed of every test of the class, to replay a failure.</summary>
    /// <param name="seed">The seed a failing run printed.</param>
    public FakeSeedClassAttribute(long seed) => Seed = seed;

    /// <summary>The pinned seed, or <see langword="null"/> when each test picks its own.</summary>
    public long? Seed { get; }

    /// <summary>Runs the test attribute of a method inside a seeded run, unless the method has its own <see cref="FakeSeedAttribute"/>.</summary>
    /// <param name="testMethodAttribute">The test attribute of the method.</param>
    /// <returns>The attribute MSTest runs the test with.</returns>
    public override TestMethodAttribute? GetTestMethodAttribute(TestMethodAttribute testMethodAttribute) =>
        testMethodAttribute is null or FakeSeedAttribute ? testMethodAttribute : FakeSeedAttribute.Around(testMethodAttribute, Seed);
}
