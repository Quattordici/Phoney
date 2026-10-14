using System.Collections.Immutable;

namespace Phoney.Generation;

/// <summary>
/// App-wide generators for types: every nested object of a registered type, in every generator compiled afterwards, is created by
/// the registered generator. A generator's own <see cref="Generator{T}.Use{TNested}"/> overrides these for the same type.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="Fake.Subtypes"/> and <see cref="Fake.Conventions"/>, registrations are read when a generator is first used, so register
/// them at startup (a test setup hook, a module initializer). See <see cref="Generator{T}.Use{TNested}"/> for what a type generator does:
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

    /// <summary>Creates every nested <typeparamref name="TNested"/> with <paramref name="generator"/>, replacing an earlier registration for the type.</summary>
    /// <exception cref="ArgumentException"><typeparamref name="TNested"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public void Add<TNested>(Generator<TNested> generator) => Add<TNested, TNested>(generator);

    /// <summary>
    /// Creates every nested <typeparamref name="TBase"/> (an interface, abstract class or base class) with the generator of a subtype,
    /// replacing an earlier registration for the type.
    /// </summary>
    /// <exception cref="ArgumentException"><typeparamref name="TBase"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public void Add<TBase, TGenerated>(Generator<TGenerated> generator)
        where TGenerated : TBase
    {
        ArgumentNullException.ThrowIfNull(generator);
        NestedGenerators.ValidateType(typeof(TBase), nameof(generator));
        NestedGenerators.RejectVerification(generator, nameof(generator));
        ImmutableInterlocked.Update(ref s_all, all => all.SetItem(typeof(TBase), generator));
    }

    /// <summary>Removes the generator registered for <typeparamref name="TBase"/>.</summary>
    public void Remove<TBase>() => ImmutableInterlocked.Update(ref s_all, all => all.Remove(typeof(TBase)));

    /// <summary>Removes all registered generators.</summary>
    public void Clear() => Volatile.Write(ref s_all, ImmutableDictionary<Type, INestedGenerator>.Empty);
}

/// <summary>What generators registered for types (<c>Use</c>, <c>Fake.Generators</c>) have in common.</summary>
internal static class NestedGenerators
{
    /// <summary>A nested generator has no <c>Generate</c> call of its own to verify at, so asking it to would silently check nothing.</summary>
    public static void RejectVerification(INestedGenerator generator, string parameterName)
    {
        if (generator.VerifiesRules)
        {
            throw new ArgumentException(
                $"The {generator.GeneratedType.Name} generator verifies its rules (VerifyRules), but a nested generator is never checked: it has no Generate call of its own. Call VerifyRules(false) on it and verify the generator you call Generate on.",
                parameterName);
        }
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
