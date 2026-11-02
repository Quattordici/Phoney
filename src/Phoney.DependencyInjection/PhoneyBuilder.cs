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
    /// Registers a singleton <see cref="ObjectGenerator{T}"/> (generators are immutable and thread-safe) with the locale, seed
    /// and clock of <see cref="PhoneyOptions"/>, then the type-wide configuration of <c>Fake.Generators.Configure&lt;T&gt;</c> (whose locale and date win
    /// over the options), then <paramref name="configure"/> applied:
    /// <c>.AddGenerator&lt;Customer&gt;(g =&gt; g.With(c =&gt; c.Country, "SE"))</c>.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public PhoneyBuilder AddGenerator<T>(Func<ObjectGenerator<T>, ObjectGenerator<T>>? configure = null) =>
        Register(() => new ObjectGenerator<T>(ModelRegistry.For<T>()), configure, typeWide: true);

    /// <summary>
    /// Registers a singleton generator built from <paramref name="generator"/>, typically a source-generated one
    /// (<c>TestData.Customer</c>), so no reflection is needed: the options are applied to it, then <paramref name="configure"/>.
    /// </summary>
    public PhoneyBuilder AddGenerator<T>(ObjectGenerator<T> generator, Func<ObjectGenerator<T>, ObjectGenerator<T>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return Register(() => generator, configure, typeWide: false);
    }

    private PhoneyBuilder Register<T>(Func<ObjectGenerator<T>> create, Func<ObjectGenerator<T>, ObjectGenerator<T>>? configure, bool typeWide)
    {
        Services.RemoveAll<ObjectGenerator<T>>();
        Services.AddSingleton(sp =>
        {
            var generator = ((FakerFactory)sp.GetRequiredService<IFakerFactory>()).Apply(create());
            if (typeWide)
                generator = Fake.Generators.Apply(generator); // Fake.Generators.Configure<T> comes after the options and before this registration's own configure
            return configure is null ? generator : configure(generator) ?? throw new InvalidOperationException($"The configure delegate for ObjectGenerator<{typeof(T).Name}> returned null; return the configured generator.");
        });
        return this;
    }
}
