using System.Collections.Immutable;

namespace Phoney.Generation;

/// <summary>
/// App-wide generators for types: every nested object of a registered type, in every generator compiled afterwards, is created by
/// the registered generator. A generator's own <see cref="ObjectGenerator{T}.WithGeneratorFor{TNested}"/> overrides these for the same type.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="Fake.Subtypes"/> and <see cref="Fake.Conventions"/>, registrations are read when a generator is first used, so register
/// them at startup (a test setup hook, a module initializer). See <see cref="ObjectGenerator{T}.WithGeneratorFor{TNested}"/> for what a type generator does:
/// it is self-contained, a rule for the member wins over it, and it wins over subtypes. A registered generator is shared, so its
/// <c>Unique</c> members are unique across everything it creates.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Fake.Generators.Add(Fake.For&lt;Address&gt;().Locale("sv"));   // every Address in every generated graph is Swedish
/// </code>
/// </example>
public sealed class FakeGenerators
{
    private static ImmutableDictionary<Type, INestedGenerator> s_all = ImmutableDictionary<Type, INestedGenerator>.Empty;

    internal FakeGenerators()
    {
    }

    /// <summary>Snapshot of the registered generators by type.</summary>
    internal static ImmutableDictionary<Type, INestedGenerator> All => Volatile.Read(ref s_all);

#pragma warning disable S2325 // instance members on purpose: Fake.Generators is one registry discovered from Fake., and making them static would break callers
    /// <summary>Creates every nested <typeparamref name="TNested"/> with <paramref name="generator"/>, replacing an earlier registration for the type.</summary>
    /// <exception cref="ArgumentException"><typeparamref name="TNested"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public void Add<TNested>(ObjectGenerator<TNested> generator) => Add<TNested, TNested>(generator);

    /// <summary>
    /// Creates every nested <typeparamref name="TBase"/> (an interface, abstract class or base class) with the generator of a subtype,
    /// replacing an earlier registration for the type.
    /// </summary>
    /// <exception cref="ArgumentException"><typeparamref name="TBase"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public void Add<TBase, TGenerated>(ObjectGenerator<TGenerated> generator)
        where TGenerated : TBase
    {
        ArgumentNullException.ThrowIfNull(generator);
        NestedGenerators.ValidateType(typeof(TBase), nameof(generator));
        NestedGenerators.RejectIfNotNestable(generator, nameof(generator));
        ImmutableInterlocked.Update(ref s_all, all => all.SetItem(typeof(TBase), generator));
    }

    /// <summary>
    /// Configures every <typeparamref name="T"/>: the objects of <c>Fake.One</c>, <c>Fake.Many</c>, <c>Fake.For</c>, <c>Fake.ForGenerated</c> and
    /// <c>Fake.Populate</c>, nested objects in every generator compiled afterwards, test parameters (<c>[FakeData]</c>, <c>[FakeCheck]</c>) and
    /// <c>AddGenerator&lt;T&gt;()</c> in <c>Phoney.DependencyInjection</c>. Replaces an earlier registration for the type, <see cref="Add{TNested}"/> included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="configure"/> always receives the plain generator of the type (on the model the caller uses, source-generated or reflection), so
    /// configuring twice replaces rather than stacks, and it may run again when registrations change: keep it free of side effects. Rules added where the
    /// generator is used come after the configured ones. The configured <see cref="ObjectGenerator{T}.Locale"/> and <see cref="ObjectGenerator{T}.ReferenceDate"/>
    /// win over the facade's; the seed always comes from where the object is made (<see cref="Fake.Seeded"/>, the generator it is nested in), so the
    /// configuration may not set one. Each facade call starts with fresh <c>Unique</c> values; nested objects share one generator, like <see cref="Add{TNested}"/>.
    /// </para>
    /// <para>
    /// Rules that count the objects a generator makes itself (<c>Sequence</c>, <c>Cycle</c>, <c>WithFeed</c>) and <c>VerifyRules</c> are refused when
    /// the configuration is first applied, because nested objects are always number 0 and never verified. Register at startup, like the other registrations.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Fake.Generators.Configure&lt;Customer&gt;(g =&gt; g.With(c =&gt; c.Age, f =&gt; f.Number.Int(18, 65)));
    /// var adult = Fake.One&lt;Customer&gt;();            // also every nested Customer and every Customer test parameter
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is a scalar, an array, an interface or an abstract class.</exception>
    public void Configure<T>(Func<ObjectGenerator<T>, ObjectGenerator<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        NestedGenerators.ValidateType(typeof(T), nameof(configure));
        if (typeof(T).IsInterface || typeof(T).IsAbstract)
        {
            throw new ArgumentException(
                $"{typeof(T).Name} has no objects of its own to configure. Configure a subtype and pick it with Fake.Subtypes, or register the generator of one with Fake.Generators.Add<{typeof(T).Name}, TSubtype>(…).",
                nameof(configure));
        }

        ImmutableInterlocked.Update(ref s_all, all => all.SetItem(typeof(T), new TypeConfiguration<T>(configure)));
    }

    /// <summary>
    /// <paramref name="generator"/> configured by <see cref="Configure{T}"/>'s registration for <typeparamref name="T"/>, or <paramref name="generator"/> itself when
    /// there is none. For integrations that build generators themselves (<c>Phoney.DependencyInjection</c>); the facade applies it already.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The configuration returned <see langword="null"/>, set a seed, or added rules a nested generator can't have.</exception>
    public ObjectGenerator<T> Apply<T>(ObjectGenerator<T> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return TypeConfiguration<T>.Apply(Registrations.Current, generator);
    }

    /// <summary>Removes the generator registered for <typeparamref name="TBase"/>.</summary>
    public void Remove<TBase>() => ImmutableInterlocked.Update(ref s_all, all => all.Remove(typeof(TBase)));

    /// <summary>Removes all registered generators.</summary>
    public void Clear() => Volatile.Write(ref s_all, ImmutableDictionary<Type, INestedGenerator>.Empty);
#pragma warning restore S2325
}

/// <summary>What generators registered for types (<c>WithGeneratorFor</c>, <c>Fake.Generators</c>) have in common.</summary>
internal static class NestedGenerators
{
    /// <summary>Refuses a generator that would silently misbehave as a nested one (it verifies rules nobody would check, or numbers objects that are all number 0).</summary>
    public static void RejectIfNotNestable(INestedGenerator generator, string parameterName)
    {
        if (generator.CannotBeNested is { } reason)
            throw new ArgumentException($"The {generator.GeneratedType.Name} generator {reason}", parameterName);
    }

    /// <summary>Scalars and arrays never reach the nested-object path, so a generator for them would silently do nothing.</summary>
    public static void ValidateType(Type type, string parameterName)
    {
        if (type.IsArray || FakeMember.ScalarCategory(type) != TypeCategory.Other || ScalarAdapters.IsAdapted(type))
        {
            throw new ArgumentException(
                $"{type.Name} is not a nested object: values like it come from rules and conventions. Use .With(x => x.Member, …) for a member, or .WithConvention<{type.Name}>(…) for all members with a name.",
                parameterName);
        }
    }
}
