using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Phoney.Generation;

/// <summary>
/// App-wide custom conventions, checked before the built-in ones for members without an explicit rule, in the
/// generated objects and in every object nested in them.
/// </summary>
/// <example>
/// <code>
/// // Every string member ending in "Sku" in every generator:
/// Fake.Conventions.Add&lt;string&gt;(name =&gt; name.EndsWith("Sku"), f =&gt; f.Random.ReplaceSymbols("SKU-#####"));
/// </code>
/// </example>
public sealed class FakeConventions
{
    private static ImmutableList<CustomConvention> s_all = [];

    internal FakeConventions()
    {
    }

    /// <summary>Snapshot of the registered conventions.</summary>
    internal static ImmutableList<CustomConvention> All => Volatile.Read(ref s_all);

#pragma warning disable S2325 // instance members on purpose: Fake.Conventions is one registry discovered from Fake., and making them static would break callers
    /// <summary>
    /// Generates members of type <typeparamref name="TValue"/> whose name matches <paramref name="nameMatches"/>
    /// with <paramref name="factory"/>. Applies to generators compiled afterwards.
    /// </summary>
    public void Add<TValue>(Func<string, bool> nameMatches, Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(nameMatches);
        ArgumentNullException.ThrowIfNull(factory);
        var convention = new CustomConvention(typeof(TValue), nameMatches, s => factory(s.Faker));
        ImmutableInterlocked.Update(ref s_all, list => list.Add(convention));
    }

    /// <summary>
    /// Generates every member of type <typeparamref name="TValue"/> (also its nullable form) with <paramref name="factory"/>, whatever its name,
    /// in every generator compiled afterwards. Members only: the elements of collections are not affected. Data annotations are not applied to the value.
    /// </summary>
    public void Add<TValue>(Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var convention = new CustomConvention(typeof(TValue), static _ => true, s => factory(s.Faker), "Fake.Conventions.Add");
        ImmutableInterlocked.Update(ref s_all, list => list.Add(convention));
    }

    /// <summary>Generates members named <paramref name="memberName"/> (ignoring case) of type <typeparamref name="TValue"/> with <paramref name="factory"/>.</summary>
    public void Add<TValue>(string memberName, Func<Faker, TValue> factory) =>
        Add(name => string.Equals(name, memberName, StringComparison.OrdinalIgnoreCase), factory);

    /// <summary>Removes all custom conventions.</summary>
    public void Clear() => Volatile.Write(ref s_all, []);
#pragma warning restore S2325
}

/// <summary>A user-defined convention: member type plus a name predicate; <paramref name="Source"/> is the call that registered it, for messages.</summary>
internal sealed record CustomConvention(Type Type, Func<string, bool> NameMatches, Func<FakeScope, object?> Factory, string Source = "WithConvention")
{
    /// <summary>Whether the convention applies to <paramref name="member"/> (same type, or its nullable form, and a matching name).</summary>
    public bool Matches(FakeMember member) =>
        (member.Type == Type || Nullable.GetUnderlyingType(member.Type) == Type) && NameMatches(member.Name);
}

/// <summary>
/// The custom conventions of one compiled generator (its own, then the app-wide ones) for the nested objects it creates,
/// bound to each nested type's members once and cached. Bound slots carry no state, so they are shared across threads.
/// </summary>
internal sealed class NestedConventions(ImmutableList<CustomConvention> conventions, RuleUsage? usage, bool parameterDefaults)
{
    private readonly ConcurrentDictionary<Type, RuleSlot?[]?> _slots = new();

    /// <summary>
    /// A convention holder, or <see langword="null"/> when there are no conventions and optional constructor parameters are generated
    /// (nested objects then need no slots).
    /// </summary>
    public static NestedConventions? For(ImmutableList<CustomConvention> conventions, RuleUsage? usage = null, bool parameterDefaults = false) =>
        conventions.IsEmpty && !parameterDefaults ? null : new(conventions, usage, parameterDefaults);

    /// <summary>Slots for the members of <paramref name="model"/> that a convention or declared default matches, or <see langword="null"/> when none does.</summary>
    public RuleSlot?[]? SlotsFor(Type type, IFakeModel model) =>
        _slots.TryGetValue(type, out var slots) ? slots : _slots.GetOrAdd(type, Bind(model.Members));

    /// <summary>
    /// The sources a nested object's members are bound from: declared defaults, then the first matching convention, in the order <see cref="MemberPlan"/> applies them
    /// to the root object. <paramref name="rules"/> are rules for specific members that come before both (path rules).
    /// </summary>
    public MemberPlanInputs Sources(IReadOnlyDictionary<int, SlotDecision>? rules = null) =>
        new() { Rules = rules, ParameterDefaults = parameterDefaults, Conventions = conventions, Usage = usage };

    private RuleSlot?[]? Bind(IReadOnlyList<FakeMember> members)
    {
        var slots = MemberPlan.Build(members, Sources()).Slots;
        return Array.Exists(slots, slot => slot is not null) ? slots : null;
    }
}

/// <summary>The declared defaults of optional constructor parameters, for <c>ObjectGenerator&lt;T&gt;.WithParameterDefaults</c>.</summary>
internal static class ParameterDefaults
{
    /// <summary>A constant with the member's declared default, or <see langword="null"/> when it is not an optional constructor parameter.</summary>
    public static SlotDecision? Decision(FakeMember member) =>
        member is { IsConstructorParameter: true, HasDefaultValue: true } ? new SlotDecision(RuleKind.Constant, member.DefaultValue) : null;
}

#pragma warning disable S2326 // the source generator reads T: it is the type to generate a model for
/// <summary>
/// Generates a reflection-free, trim- and Native AOT-safe model for <typeparamref name="T"/> (and the types it
/// contains) at compile time. Put it on a <c>partial</c> class; the class gets a <c>ObjectGenerator&lt;T&gt;</c> property per type.
/// </summary>
/// <example>
/// <code>
/// [FakeFor&lt;Customer&gt;, FakeFor&lt;Order&gt;]
/// internal static partial class TestData;
///
/// var customers = TestData.Customer.Generate(100);
/// </code>
/// </example>
/// <typeparam name="T">The type to generate a model for.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FakeForAttribute<T> : Attribute;
#pragma warning restore S2326

/// <summary>
/// Marks a type parameter whose type arguments get a source-generated model, as if each were listed with
/// <see cref="FakeForAttribute{T}"/>. Put it on the type parameter of a generic helper that generates <c>T</c> with
/// <see cref="Fake.ForGenerated{T}"/>; every compilation that passes a concrete type for it (in a base class, a
/// variable type or an explicit generic method call) generates and registers a model for that type.
/// </summary>
/// <example>
/// <code>
/// public abstract class TestDataBuilder&lt;[FakeForTypeArgument] T&gt; where T : class
/// {
///     public T Build() =&gt; Fake.ForGenerated&lt;T&gt;().Generate();
/// }
///
/// // In a project referencing Phoney: Customer gets a model, no [FakeFor&lt;Customer&gt;] needed.
/// internal sealed class CustomerBuilder : TestDataBuilder&lt;Customer&gt;;
/// </code>
/// </example>
/// <remarks>Type arguments inferred from method arguments are not seen; pass them explicitly.</remarks>
[AttributeUsage(AttributeTargets.GenericParameter, AllowMultiple = false, Inherited = false)]
public sealed class FakeForTypeArgumentAttribute : Attribute;
