using System.Collections.Immutable;

namespace Phony.Generation;

/// <summary>
/// App-wide custom conventions, checked before the built-in ones for members without an explicit rule.
/// </summary>
/// <example>
/// <code>
/// // Every string member ending in "Sku" in every generator:
/// Fake.Conventions.Add&lt;string&gt;(name =&gt; name.EndsWith("Sku"), f =&gt; f.Random.Replace("SKU-#####"));
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

    /// <summary>Generates members named <paramref name="memberName"/> (ignoring case) of type <typeparamref name="TValue"/> with <paramref name="factory"/>.</summary>
    public void Add<TValue>(string memberName, Func<Faker, TValue> factory) =>
        Add(name => string.Equals(name, memberName, StringComparison.OrdinalIgnoreCase), factory);

    /// <summary>Removes all custom conventions.</summary>
    public void Clear() => Volatile.Write(ref s_all, []);
}

/// <summary>A user-defined convention: member type plus a name predicate.</summary>
internal sealed record CustomConvention(Type Type, Func<string, bool> NameMatches, Func<FakeScope, object?> Factory)
{
    /// <summary>Whether the convention applies to <paramref name="member"/> (same type, or its nullable form, and a matching name).</summary>
    public bool Matches(FakeMember member) =>
        (member.Type == Type || Nullable.GetUnderlyingType(member.Type) == Type) && NameMatches(member.Name);
}

/// <summary>
/// Generates a reflection-free, trim- and Native AOT-safe model for <typeparamref name="T"/> (and the types it
/// contains) at compile time. Put it on a <c>partial</c> class; the class gets a <c>Generator&lt;T&gt;</c> property per type.
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
