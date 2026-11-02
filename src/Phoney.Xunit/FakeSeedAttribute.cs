using System.Reflection;
using Xunit;
using Xunit.v3;

namespace Phoney.Xunit;

/// <summary>
/// Runs a test, the tests of a class or a whole assembly on a seeded <see cref="Fake"/> facade: <c>Fake.One&lt;T&gt;()</c>, <c>Fake.Many&lt;T&gt;()</c>,
/// <c>Fake.For&lt;T&gt;()</c> and <c>Fake.Person…</c> are random from run to run, and when the test fails its seed is printed in the test output,
/// so the failure can be replayed. Put it next to <c>[Fact]</c>, <c>[Theory]</c> or <c>[FakeCheck]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Without a number the seed is new on every run, or the value of the <c>PHONEY_SEED</c> environment variable when it is set
/// (<c>PHONEY_SEED=123456789 dotnet test</c> replays a whole run). <c>[FakeSeed(123456789)]</c> pins the seed of a test: use it to replay a failure
/// while you fix it. A pinned seed wins over <c>PHONEY_SEED</c>.
/// </para>
/// <para>
/// The seed applies to the facade of the test's own async flow, so parallel tests don't affect each other; every row of a theory starts from it. Generators
/// created before the test runs (a <c>static</c> field) are not seeded by it, and neither are generators that call <c>.Seed(...)</c> themselves. On a method
/// it wins over the class, and on a class over the assembly (<c>[assembly: FakeSeed]</c>); only the one that wins reports. Skipped tests print nothing.
/// </para>
/// <para><c>[FakeData]</c> values don't need this attribute: they are already reproducible, and follow <c>PHONEY_SEED</c> too.</para>
/// </remarks>
/// <example>
/// <code>
/// [Fact, FakeSeed]
/// public void Totals_add_up()
/// {
///     var orders = Fake.Many&lt;Order&gt;(100);   // different on every run
///     …                                       // on failure the output says: Replay it with [FakeSeed(481516234)] or PHONEY_SEED=481516234
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class FakeSeedAttribute : BeforeAfterTestAttribute
{
    /// <summary>How a test pins a seed, offered by the report of a random seed.</summary>
    internal const string PinHint = "[FakeSeed({0})]";

    /// <summary>Uses a new seed on every run (or <c>PHONEY_SEED</c>), and prints it when the test fails.</summary>
    public FakeSeedAttribute()
    {
    }

    /// <summary>Pins the seed, to replay a failure.</summary>
    /// <param name="seed">The seed a failing run printed.</param>
    public FakeSeedAttribute(long seed) => Seed = seed;

    /// <summary>The pinned seed, or <see langword="null"/> when each run picks its own.</summary>
    public long? Seed { get; }

    /// <summary>Opens the seeded run of the test; xUnit.net calls the assembly's, the class's and then the method's, so the method's is innermost.</summary>
    /// <param name="methodUnderTest">The test method.</param>
    /// <param name="test">The test.</param>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) =>
#pragma warning disable CA2000 // the run is ended by After, through FakeSeedRun.Current
        FakeSeed.Begin(Seed, PinHint);
#pragma warning restore CA2000

    /// <summary>Ends the run and, when the test failed, writes its seed to the test output.</summary>
    /// <param name="methodUnderTest">The test method.</param>
    /// <param name="test">The test.</param>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var context = TestContext.Current;
        FakeSeedRun.Current?.End(context.TestState?.Result == TestResult.Failed, line => context.TestOutputHelper?.WriteLine(line));
    }
}
