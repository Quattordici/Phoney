using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Phoney.Generation;

namespace Phoney.DependencyInjection;

/// <summary>Adds Phoney generators to a service collection; returned by <c>services.AddPhoney()</c>.</summary>
public sealed class PhoneyBuilder
{
    private const string ReflectionMessage =
        "Uses reflection for types without a source-generated model. For trimmed or Native AOT apps pass a [FakeFor<T>] generator to AddGenerator.";

    internal PhoneyBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection, to continue registering services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a singleton <see cref="Generator{T}"/> (generators are immutable and thread-safe) with the locale, seed
    /// and clock of <see cref="PhoneyOptions"/>, then <paramref name="configure"/> applied:
    /// <c>.AddGenerator&lt;Customer&gt;(g =&gt; g.With(c =&gt; c.Country, "SE"))</c>.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public PhoneyBuilder AddGenerator<T>(Func<Generator<T>, Generator<T>>? configure = null) =>
        Register(() => new Generator<T>(ModelRegistry.For<T>()), configure);

    /// <summary>
    /// Registers a singleton generator built from <paramref name="generator"/>, typically a source-generated one
    /// (<c>TestData.Customer</c>), so no reflection is needed: the options are applied to it, then <paramref name="configure"/>.
    /// </summary>
    public PhoneyBuilder AddGenerator<T>(Generator<T> generator, Func<Generator<T>, Generator<T>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return Register(() => generator, configure);
    }

    private PhoneyBuilder Register<T>(Func<Generator<T>> create, Func<Generator<T>, Generator<T>>? configure)
    {
        Services.RemoveAll<Generator<T>>();
        Services.AddSingleton(sp =>
        {
            var generator = ((FakerFactory)sp.GetRequiredService<IFakerFactory>()).Apply(create());
            return configure is null ? generator : configure(generator) ?? throw new InvalidOperationException($"The configure delegate for Generator<{typeof(T).Name}> returned null; return the configured generator.");
        });
        return this;
    }
}
