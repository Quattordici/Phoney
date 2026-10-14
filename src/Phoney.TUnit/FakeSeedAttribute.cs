using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Phoney.TUnit;

/// <summary>
/// Runs a test, a test class or a whole assembly on a seeded <see cref="Fake"/> facade: <c>Fake.One&lt;T&gt;()</c>, <c>Fake.Many&lt;T&gt;()</c>,
/// <c>Fake.For&lt;T&gt;()</c> and <c>Fake.Person…</c> are random from run to run, and when the test fails its seed is printed in the test output,
/// so the failure can be replayed.
/// </summary>
/// <remarks>
/// <para>
/// Without a number the seed is new on every run, or the value of the <c>PHONEY_SEED</c> environment variable when it is set
/// (<c>PHONEY_SEED=123456789 dotnet test</c> replays a whole run). <c>[FakeSeed(123456789)]</c> pins the seed of a test: use it to replay a failure
/// while you fix it. A pinned seed wins over <c>PHONEY_SEED</c>.
/// </para>
/// <para>
/// The seed applies to the facade of the test's own async flow, so parallel tests don't affect each other. Generators created before the test
/// runs (a <c>static</c> field) are not seeded by it, and neither are generators that call <c>.Seed(...)</c> themselves. On a method it
/// wins over the class, and on a class over the assembly. The attribute installs the test's executor, so it can't be combined with another executor
/// such as <c>[STAThreadExecutor]</c>.
/// </para>
/// <para><c>[FakeData]</c> values don't need this attribute: they are already reproducible, and follow <c>PHONEY_SEED</c> too.</para>
/// </remarks>
/// <example>
/// <code>
/// [Test, FakeSeed]
/// public async Task Totals_add_up()
/// {
///     var orders = Fake.Many&lt;Order&gt;(100);   // different on every run
///     …                                       // on failure the output says: Replay it with [FakeSeed(481516234)] or PHONEY_SEED=481516234
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class FakeSeedAttribute : Attribute, ITestRegisteredEventReceiver, IScopedAttribute
{
    /// <summary>Uses a new seed on every run (or <c>PHONEY_SEED</c>), and prints it when the test fails.</summary>
    public FakeSeedAttribute()
    {
    }

    /// <summary>Pins the seed, to replay a failure.</summary>
    /// <param name="seed">The seed a failing run printed.</param>
    public FakeSeedAttribute(long seed) => Seed = seed;

    /// <summary>The pinned seed, or <see langword="null"/> when each run picks its own.</summary>
    public long? Seed { get; }

    /// <inheritdoc />
    public Type ScopeType => typeof(FakeSeedAttribute);

    /// <inheritdoc />
    public ValueTask OnTestRegistered(TestRegisteredContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.SetTestExecutor(new SeededTestExecutor(Seed));
        return default;
    }
}
