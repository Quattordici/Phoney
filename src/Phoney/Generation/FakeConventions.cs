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
internal sealed class NestedConventions(ImmutableList<CustomConvention> conventions, RuleUsage? usage)
{
    private readonly ConcurrentDictionary<Type, RuleSlot?[]?> _slots = new();

    /// <summary>A convention holder, or <see langword="null"/> when there are no conventions (nested objects then need no slots).</summary>
    public static NestedConventions? For(ImmutableList<CustomConvention> conventions, RuleUsage? usage = null) => conventions.IsEmpty ? null : new(conventions, usage);

    /// <summary>Slots for the members of <paramref name="model"/> that a convention matches, or <see langword="null"/> when none does.</summary>
    public RuleSlot?[]? SlotsFor(Type type, IFakeModel model) =>
        _slots.TryGetValue(type, out var slots) ? slots : _slots.GetOrAdd(type, Bind(conventions, model.Members, usage));

    /// <summary>Binds the first matching convention to each member, like the root object's rules in <c>ObjectGenerator&lt;T&gt;.Compile</c>.</summary>
    private static RuleSlot?[]? Bind(ImmutableList<CustomConvention> conventions, IReadOnlyList<FakeMember> members, RuleUsage? usage)
    {
        RuleSlot?[]? slots = null;
        for (var i = 0; i < members.Count; i++)
        {
            if (conventions.FirstOrDefault(c => c.Matches(members[i])) is not { } match)
                continue;
            usage?.Convention(match);
            slots ??= new RuleSlot?[members.Count];
            slots[i] = new RuleSlot(RuleKind.Convention, match.Factory, unique: false, maxAttempts: 0);
        }

        return slots;
    }
}

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
