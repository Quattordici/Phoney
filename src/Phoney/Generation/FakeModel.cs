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
    /// <param name="constraints">Validation rules from the member's data annotations, if any.</param>
    /// <param name="isSettable">
    /// Whether the member can be set on an existing instance (for <see cref="PopulateMode"/>): a public setter or
    /// <c>init</c> accessor, also behind a constructor parameter. Default: every member that is not a constructor parameter.
    /// </param>
    /// <param name="isNested">Whether the member holds an object the generator creates and populates recursively.</param>
    /// <param name="fillsExisting">
    /// Whether the member is a collection that can only be read (<c>{ get; }</c>, a private setter, a readonly field): it is generated like any collection and its
    /// elements are added to the collection the object already holds (<see cref="FakeModel{T}.Fill"/>). Such a member is not <paramref name="isSettable"/>.
    /// </param>
    public FakeMember(string name, Type type, ConventionKind convention, bool isConstructorParameter = false, bool isInitOnly = false, MemberConstraints? constraints = null, bool? isSettable = null, bool isNested = false, bool fillsExisting = false)
    {
        FillsExisting = fillsExisting;
        Constraints = constraints;
        Name = name;
        Type = type;
        Convention = convention;
        IsConstructorParameter = isConstructorParameter;
        IsInitOnly = isInitOnly;
        IsSettable = isSettable ?? !isConstructorParameter;
        IsNested = isNested;
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

    /// <summary>Validation rules from data annotations (<c>[StringLength]</c>, <c>[Range]</c>…), or <see langword="null"/>.</summary>
    public MemberConstraints? Constraints { get; }

    /// <summary>Whether the member can be set on an existing instance.</summary>
    public bool IsSettable { get; }

    /// <summary>Whether the member holds an object that is created and populated recursively.</summary>
    public bool IsNested { get; }

    /// <summary>Whether the member is a read-only collection whose elements are added to the collection the object holds.</summary>
    public bool FillsExisting { get; }

    /// <summary>Whether the member holds a scalar value (string, number, date…) rather than an object or collection.</summary>
    public bool IsScalar => ScalarCategory(Type) != TypeCategory.Other || ScalarAdapters.IsAdapted(Type);

    /// <summary>Scalar category of a type (looking through <c>Nullable&lt;T&gt;</c>); <see cref="TypeCategory.Other"/> for objects and collections.</summary>
    internal static TypeCategory ScalarCategory(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return ConventionRules.CategoryOf(t.FullName ?? "", t.IsEnum);
    }

    /// <summary>The category name conventions are matched against: <see cref="ScalarCategory"/>, or the category of an adapted type (NodaTime's <c>Instant</c> → date).</summary>
    internal static TypeCategory ConventionCategory(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return ConventionRules.ConventionCategoryOf(t.FullName ?? "", t.IsEnum);
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

    /// <summary>The members, indexed as used by <see cref="FakeScope.Get{TValue}"/>.</summary>
    IReadOnlyList<FakeMember> Members { get; }

    /// <summary>Creates and populates an instance.</summary>
    object? CreateObject(FakeScope scope);

    /// <summary>Populates an existing instance; returns it (a populated copy for structs).</summary>
    object PopulateObject(object instance, FakeScope scope, PopulateMode mode);

    /// <summary>Reads a <see cref="FakeMember.IsSettable"/> member (boxed) from an instance.</summary>
    object? GetMember(object instance, int member);

    /// <summary>Sets a <see cref="FakeMember.IsSettable"/> member on an instance; returns it (a copy for structs).</summary>
    object SetMember(object instance, int member, object? value);
}

/// <summary>
/// Knows how to create and populate a <typeparamref name="T"/>. Implemented reflectively at runtime, or
/// generated at compile time by <c>[FakeFor&lt;T&gt;]</c> for reflection-free, AOT-safe generation.
/// </summary>
/// <remarks>
/// <see cref="Create"/> must read every member value through <see cref="FakeScope.Get{TValue}"/> (or skip it when
/// <see cref="FakeScope.Include"/> is false) so that generator rules, ignores and uniqueness apply.
/// <see cref="Populate"/> is shared by all models: it is written in terms of <see cref="Get"/>, <see cref="IsUnset"/>,
/// <see cref="Set"/> and <see cref="Fallback"/>, so reflection and generated models fill an object identically.
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

    /// <summary>Sets member <paramref name="member"/> on an existing instance (used for rules that depend on other members and by <see cref="Populate"/>).</summary>
    /// <returns>The instance; a copy for value types.</returns>
    /// <exception cref="InvalidOperationException">The member is not <see cref="FakeMember.IsSettable"/>.</exception>
    public abstract T Set(T instance, int member, object? value);

    /// <summary>Reads member <paramref name="member"/> (boxed) from an instance; only for <see cref="FakeMember.IsSettable"/> and <see cref="FakeMember.FillsExisting"/> members.</summary>
    public abstract object? Get(T instance, int member);

    /// <summary>
    /// Adds the elements of <paramref name="values"/> (a collection that was generated for the member) to the collection that a <see cref="FakeMember.FillsExisting"/>
    /// member holds on <paramref name="instance"/>; with <paramref name="replace"/> it is emptied first. Does nothing when the object holds no collection or a read-only one.
    /// </summary>
    public virtual void Fill(T instance, int member, object? values, bool replace) =>
        throw new InvalidOperationException($"{Members[member].Name} is not a collection that is filled in place.");

    /// <summary>Whether member <paramref name="member"/> of an instance still holds its default value, see <see cref="FakeScope.IsUnset{TValue}"/>.</summary>
    public abstract bool IsUnset(T instance, int member);

    /// <summary>The producer of member <paramref name="member"/>'s value from the convention, type and annotations (rules are applied by <see cref="FakeScope.Get{TValue}"/> around it).</summary>
    public abstract Func<FakeScope, object?> Fallback(int member);

    /// <summary>
    /// Fills members of an existing instance. A member is generated when it is not ignored and either
    /// <paramref name="mode"/> is <see cref="PopulateMode.All"/> or it is unset; an object member that is already set is populated recursively.
    /// Members that keep their value tell the object's identity what generated values must agree with (a kept
    /// <c>FirstName</c> shapes the generated email).
    /// </summary>
    /// <returns>The instance; a populated copy for value types.</returns>
    public virtual T Populate(T instance, FakeScope scope, PopulateMode mode) =>
        Populator.Populate(Members, instance, scope, mode, IsUnset, Get, Set, Fallback, Fill);

    /// <inheritdoc />
    object? IFakeModel.CreateObject(FakeScope scope) => Create(scope);

    /// <inheritdoc />
    object IFakeModel.PopulateObject(object instance, FakeScope scope, PopulateMode mode) => Populate((T)instance, scope, mode)!;

    /// <inheritdoc />
    object? IFakeModel.GetMember(object instance, int member) => Get((T)instance, member);

    /// <inheritdoc />
    object IFakeModel.SetMember(object instance, int member, object? value) => Set((T)instance, member, value)!;
}

/// <summary>The populate algorithm, shared by typed models and the untyped reflection model.</summary>
internal static class Populator
{
    public static T Populate<T>(
        IReadOnlyList<FakeMember> members,
        T instance,
        FakeScope scope,
        PopulateMode mode,
        Func<T, int, bool> isUnset,
        Func<T, int, object?> get,
        Func<T, int, object?, T> set,
        Func<int, Func<FakeScope, object?>> fallback,
        Action<T, int, object?, bool> fill)
    {
        var generate = new bool[members.Count];
        var current = new object?[members.Count];

        // Decide first, so that kept members shape the identity before any value is generated.
        for (var i = 0; i < members.Count; i++)
        {
            if (!members[i].IsSettable)
                continue;
            var unset = isUnset(instance, i);
            generate[i] = scope.Include(i) && (mode == PopulateMode.All || unset);
            if (!generate[i] && !unset)
            {
                current[i] = get(instance, i);
                scope.Adopt(members[i], current[i]);
            }
        }

        // Members are produced in declaration order, as when creating an object, so a seed gives the same values.
        for (var i = 0; i < members.Count; i++)
        {
            if (generate[i])
                instance = set(instance, i, scope.GetObject(i, fallback(i)));
            else if (members[i].IsNested && current[i] is { } existing && scope.Include(i))
                instance = set(instance, i, scope.PopulateNested(members[i].Type, existing, mode));
        }

        // Read-only collections are filled last, in place: an empty one gets elements, and PopulateMode.All replaces what it holds.
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].FillsExisting && scope.Include(i) && (mode == PopulateMode.All || isUnset(instance, i)))
                fill(instance, i, scope.GetObject(i, fallback(i)), mode == PopulateMode.All);
        }

        return instance;
    }
}
