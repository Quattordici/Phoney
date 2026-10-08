using System.Diagnostics.CodeAnalysis;
using Phoney.Generation;

namespace Phoney;

// Object generation entry points of the static facade.
public static partial class Fake
{
    private const string ReflectionMessage =
        "Uses reflection for types without a source-generated model. For trimmed or Native AOT apps declare [FakeFor<T>] on a partial class and use its generated generators.";

    /// <summary>App-wide custom conventions, e.g. <c>Fake.Conventions.Add&lt;string&gt;("Sku", f =&gt; …)</c>.</summary>
    public static FakeConventions Conventions { get; } = new();

    /// <summary>Generates one populated <typeparamref name="T"/>.</summary>
    /// <example><code>var customer = Fake.One&lt;Customer&gt;();</code></example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static T One<T>() => For<T>().Generate();

    /// <summary>Generates <paramref name="count"/> populated <typeparamref name="T"/> objects.</summary>
    /// <example><code>List&lt;Customer&gt; customers = Fake.Many&lt;Customer&gt;(100);</code></example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Many<T>(int count) => For<T>().Generate(count);

    /// <summary>
    /// Starts configuring a generator for <typeparamref name="T"/>. Uses a source-generated model when one is
    /// registered (see <see cref="FakeForAttribute{T}"/>), otherwise reflection.
    /// </summary>
    /// <example>
    /// <code>
    /// var customers = Fake.For&lt;Customer&gt;()
    ///     .Locale("sv").Seed(42)
    ///     .With(x =&gt; x.Age, f =&gt; f.Number.Int(18, 65))
    ///     .Generate(100);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Generator<T> For<T>() => new(ModelRegistry.For<T>());
}
