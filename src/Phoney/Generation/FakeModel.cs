using System.ComponentModel;

namespace Phoney.Generation;

/// <summary>A member (property, field or constructor parameter) that a <see cref="FakeModel{T}"/> populates.</summary>
public sealed class FakeMember
{
    /// <summary>Creates a member description.</summary>
    /// <param name="name">Member name; constructor parameters use the name of the property they initialize.</param>
    /// <param name="type">Declared type.</param>
    /// <param name="convention">Name convention chosen for the member.</param>
    /// <param name="isConstructorParameter">Whether the value is passed to the constructor.</param>
    /// <param name="isInitOnly">Whether the member can only be set during construction (<c>init</c>).</param>
    public FakeMember(string name, Type type, ConventionKind convention, bool isConstructorParameter = false, bool isInitOnly = false)
    {
        Name = name;
        Type = type;
        Convention = convention;
        IsConstructorParameter = isConstructorParameter;
        IsInitOnly = isInitOnly;
    }

    /// <summary>Member name.</summary>
    public string Name { get; }

    /// <summary>Declared type.</summary>
    public Type Type { get; }

    /// <summary>Name convention chosen for the member, or <see cref="ConventionKind.None"/>.</summary>
    public ConventionKind Convention { get; }

    /// <summary>Whether the value is passed to the constructor.</summary>
    public bool IsConstructorParameter { get; }

    /// <summary>Whether the member can only be set during construction.</summary>
    public bool IsInitOnly { get; }

    /// <summary>Whether the member holds a scalar value (string, number, date…) rather than an object or collection.</summary>
    public bool IsScalar => ScalarCategory(Type) != TypeCategory.Other;

    /// <summary>Scalar category of a type (looking through <c>Nullable&lt;T&gt;</c>); <see cref="TypeCategory.Other"/> for objects and collections.</summary>
    internal static TypeCategory ScalarCategory(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return ConventionRules.CategoryOf(t.FullName ?? "", t.IsEnum);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Type.Name}, {Convention})";
}

/// <summary>Untyped access to a model, used for nested objects.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IFakeModel
{
    /// <summary>The type the model creates.</summary>
    Type Type { get; }

    /// <summary>Creates and populates an instance.</summary>
    object? CreateObject(FakeScope scope);
}

/// <summary>
/// Knows how to create and populate a <typeparamref name="T"/>. Implemented reflectively at runtime, or
/// generated at compile time by <c>[FakeFor&lt;T&gt;]</c> for reflection-free, AOT-safe generation.
/// </summary>
/// <remarks>
/// <see cref="Create"/> must read every member value through <see cref="FakeScope.Get{TValue}"/> (or skip it when
/// <see cref="FakeScope.Include"/> is false) so that generator rules, ignores and uniqueness apply.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class FakeModel<T> : IFakeModel
{
    /// <summary>The members, indexed as used by <see cref="FakeScope.Get{TValue}"/>.</summary>
    public abstract IReadOnlyList<FakeMember> Members { get; }

    /// <inheritdoc />
    public Type Type => typeof(T);

    /// <summary>Creates and populates an instance.</summary>
    public abstract T Create(FakeScope scope);

    /// <summary>Sets member <paramref name="member"/> on an existing instance (used for rules that depend on other members).</summary>
    /// <returns>The instance; a copy for value types.</returns>
    public abstract T Set(T instance, int member, object? value);

    /// <inheritdoc />
    object? IFakeModel.CreateObject(FakeScope scope) => Create(scope);
}
