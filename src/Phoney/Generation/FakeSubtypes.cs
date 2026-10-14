using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Phoney.Generation;

/// <summary>
/// App-wide subtypes for interfaces, abstract classes and base classes: members declared as the base type get an
/// instance of one of its subtypes, in every generator compiled afterwards. A generator's own
/// <see cref="ObjectGenerator{T}.Subtype{TBase, T1}"/> overrides these for the same base type.
/// </summary>
/// <remarks>
/// Without a registration, an interface or abstract class uses the types listed with <c>[JsonDerivedType]</c> on it,
/// and is left <see langword="null"/> when there are none. Subtypes registered here are kept sorted by name, so the
/// order of registration does not change seeded output.
/// </remarks>
/// <example>
/// <code>
/// Fake.Subtypes.Add&lt;IShape, Circle, Square&gt;(); // every IShape member is a Circle or a Square
/// </code>
/// </example>
public sealed class FakeSubtypes
{
    private static ImmutableDictionary<Type, ImmutableArray<Type>> s_all = ImmutableDictionary<Type, ImmutableArray<Type>>.Empty;

    internal FakeSubtypes()
    {
    }

    /// <summary>Snapshot of the registered subtypes by base type.</summary>
    internal static ImmutableDictionary<Type, ImmutableArray<Type>> All => Volatile.Read(ref s_all);

    /// <summary>Generates <typeparamref name="T1"/> for members of type <typeparamref name="TBase"/>, in addition to subtypes added before.</summary>
    public void Add<TBase, T1>()
        where T1 : TBase => Add<TBase>(typeof(T1));

    /// <summary>Generates one of the subtypes for members of type <typeparamref name="TBase"/>, in addition to subtypes added before.</summary>
    public void Add<TBase, T1, T2>()
        where T1 : TBase
        where T2 : TBase => Add<TBase>(typeof(T1), typeof(T2));

    /// <summary>Generates one of the subtypes for members of type <typeparamref name="TBase"/>, in addition to subtypes added before.</summary>
    public void Add<TBase, T1, T2, T3>()
        where T1 : TBase
        where T2 : TBase
        where T3 : TBase => Add<TBase>(typeof(T1), typeof(T2), typeof(T3));

    /// <summary>Generates one of the subtypes for members of type <typeparamref name="TBase"/>, in addition to subtypes added before.</summary>
    public void Add<TBase, T1, T2, T3, T4>()
        where T1 : TBase
        where T2 : TBase
        where T3 : TBase
        where T4 : TBase => Add<TBase>(typeof(T1), typeof(T2), typeof(T3), typeof(T4));

    /// <summary>Generates one of <paramref name="subtypes"/> for members of type <typeparamref name="TBase"/>, in addition to subtypes added before.</summary>
    /// <exception cref="ArgumentException">A subtype is abstract, an interface, open generic or not assignable to <typeparamref name="TBase"/>.</exception>
    public void Add<TBase>(params Type[] subtypes) => Add(typeof(TBase), subtypes);

    /// <summary>Removes the subtypes registered for <typeparamref name="TBase"/>.</summary>
    public void Remove<TBase>() => ImmutableInterlocked.Update(ref s_all, all => all.Remove(typeof(TBase)));

    /// <summary>Removes all registered subtypes, including those registered by <see cref="FakeSubtypeAttribute{TBase, TSubtype}"/>.</summary>
    public void Clear() => Volatile.Write(ref s_all, ImmutableDictionary<Type, ImmutableArray<Type>>.Empty);

    private static void Add(Type baseType, Type[] subtypes)
    {
        var valid = Subtypes.Validate(baseType, subtypes);
        ImmutableInterlocked.Update(ref s_all, all =>
            all.SetItem(baseType, Subtypes.Sorted(all.TryGetValue(baseType, out var existing) ? existing.Union(valid) : valid)));
    }
}

/// <summary>
/// Registers <typeparamref name="TSubtype"/> as a subtype of <typeparamref name="TBase"/> app-wide, like
/// <c>Fake.Subtypes.Add&lt;TBase, TSubtype&gt;()</c>, and makes the source generator write a model for it. Put it on
/// the partial class that has the <see cref="FakeForAttribute{T}"/> attributes.
/// </summary>
/// <example>
/// <code>
/// [FakeFor&lt;Drawing&gt;, FakeSubtype&lt;IShape, Circle&gt;, FakeSubtype&lt;IShape, Square&gt;]
/// internal static partial class TestData;
/// </code>
/// </example>
/// <typeparam name="TBase">The interface, abstract class or base class members are declared as.</typeparam>
/// <typeparam name="TSubtype">The concrete type to generate for them.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FakeSubtypeAttribute<TBase, TSubtype> : Attribute
    where TSubtype : TBase;

/// <summary>Subtype validation, ordering and the <c>[JsonDerivedType]</c> defaults.</summary>
internal static class Subtypes
{
    private static readonly ConcurrentDictionary<Type, ImmutableArray<Type>> Defaults = new();

    /// <summary>Checks that every subtype is a concrete type assignable to <paramref name="baseType"/>.</summary>
    public static ImmutableArray<Type> Validate(Type baseType, IReadOnlyList<Type> subtypes)
    {
        ArgumentNullException.ThrowIfNull(subtypes);
        if (subtypes.Count == 0)
            throw new ArgumentException($"Provide at least one subtype of {baseType.Name}.", nameof(subtypes));
        foreach (var subtype in subtypes)
        {
            if (subtype is null)
                throw new ArgumentException("A subtype is null.", nameof(subtypes));
            if (subtype.IsAbstract || subtype.IsInterface || subtype.ContainsGenericParameters)
                throw new ArgumentException($"{subtype.Name} can't be created; register concrete subtypes of {baseType.Name}, e.g. classes that implement it.", nameof(subtypes));
            if (!subtype.IsAssignableTo(baseType))
                throw new ArgumentException($"{subtype.Name} is not a {baseType.Name}; register types that derive from or implement it.", nameof(subtypes));
        }

        return [.. subtypes.Distinct()];
    }

    /// <summary>Distinct subtypes ordered by full name, so the order of registration never changes which one is drawn.</summary>
    public static ImmutableArray<Type> Sorted(IEnumerable<Type> subtypes) =>
        [.. subtypes.Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal)];

    /// <summary>Records the <c>[JsonDerivedType]</c> types of an interface or abstract class (from generated code).</summary>
    public static void RegisterDefaults(Type baseType, Type[] derivedTypes) => Defaults[baseType] = Sorted(derivedTypes);

    /// <summary>
    /// The default subtypes of an interface or abstract class: registered by generated code, else read from its
    /// <c>[JsonDerivedType]</c> attributes where reflection is available. Concrete types have none, so they are generated as themselves.
    /// </summary>
    public static ImmutableArray<Type> DefaultsOf(Type type)
    {
        if (!(type.IsInterface || type.IsAbstract))
            return [];
        if (Defaults.TryGetValue(type, out var registered))
            return registered;
        return RuntimeFeature.IsDynamicCodeSupported ? Defaults.GetOrAdd(type, ReadJsonDerivedTypes) : [];
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Only the attributes of the type are read; the derived types are created through their models.")]
    private static ImmutableArray<Type> ReadJsonDerivedTypes(Type type) =>
        Sorted(type.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .Select(a => a.DerivedType)
            .Where(t => !t.IsAbstract && !t.IsInterface && !t.ContainsGenericParameters && t.IsAssignableTo(type)));
}
