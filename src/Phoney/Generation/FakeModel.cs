using System.ComponentModel;

namespace Phoney.Generation;

/// <summary>A member (property, field or constructor parameter) that a <see cref="FakeModel{T}"/> populates.</summary>
public sealed class FakeMember
{
    private readonly bool? _isSettable;

    /// <summary>
    /// Creates a member description. The other values are set with the initializer:
    /// <c>new FakeMember("Email", typeof(string), ConventionKind.Email) { IsInitOnly = true, Constraints = constraints }</c>.
    /// </summary>
    /// <param name="name">Member name; constructor parameters use the name of the property they initialize.</param>
    /// <param name="type">Declared type.</param>
    /// <param name="convention">Name convention chosen for the member.</param>
    public FakeMember(string name, Type type, ConventionKind convention)
    {
        Name = name;
        Type = type;
        Convention = convention;
    }

    /// <summary>Creates a member description with every value; kept for models compiled against earlier versions.</summary>
    /// <param name="name">Member name; constructor parameters use the name of the property they initialize.</param>
    /// <param name="type">Declared type.</param>
    /// <param name="convention">Name convention chosen for the member.</param>
    /// <param name="isConstructorParameter">See <see cref="IsConstructorParameter"/>.</param>
    /// <param name="isInitOnly">See <see cref="IsInitOnly"/>.</param>
    /// <param name="constraints">See <see cref="Constraints"/>.</param>
    /// <param name="isSettable">See <see cref="IsSettable"/>; <see langword="null"/> for the default.</param>
    /// <param name="isNested">See <see cref="IsNested"/>.</param>
    /// <param name="fillsExisting">See <see cref="FillsExisting"/>.</param>
#pragma warning disable S1133 // kept so models compiled against earlier versions keep loading; removing it is a breaking change for a major version
    [Obsolete("Pass the name, type and convention, and set the other values with the initializer: new FakeMember(name, type, convention) { IsConstructorParameter = true }.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
#pragma warning disable S107 // the signature models compiled against earlier versions call; new code uses the initializer
    public FakeMember(string name, Type type, ConventionKind convention, bool isConstructorParameter, bool isInitOnly, MemberConstraints? constraints, bool? isSettable, bool isNested, bool fillsExisting)
#pragma warning restore S107, S1133
        : this(name, type, convention)
    {
        IsConstructorParameter = isConstructorParameter;
        IsInitOnly = isInitOnly;
        Constraints = constraints;
        _isSettable = isSettable;
        IsNested = isNested;
        FillsExisting = fillsExisting;
    }

    /// <summary>Member name.</summary>
    public string Name { get; }

    /// <summary>Declared type.</summary>
    public Type Type { get; }

    /// <summary>Name convention chosen for the member, or <see cref="ConventionKind.None"/>.</summary>
    public ConventionKind Convention { get; }

    /// <summary>Whether the value is passed to the constructor.</summary>
    public bool IsConstructorParameter { get; init; }

    /// <summary>Whether the member can only be set during construction (<c>init</c>).</summary>
    public bool IsInitOnly { get; init; }

    /// <summary>Validation rules from data annotations (<c>[StringLength]</c>, <c>[Range]</c>…), or <see langword="null"/>.</summary>
    public MemberConstraints? Constraints { get; init; }

    /// <summary>
    /// Whether the member can be set on an existing instance (for <see cref="PopulateMode"/>): a public setter or <c>init</c> accessor, also behind a constructor parameter.
    /// Default: every member that is not a constructor parameter.
    /// </summary>
    public bool IsSettable
    {
        get => _isSettable ?? !IsConstructorParameter;
        init => _isSettable = value;
    }

    /// <summary>Whether the member holds an object that is created and populated recursively.</summary>
    public bool IsNested { get; init; }

    /// <summary>
    /// Whether the member is a collection that can only be read (<c>{ get; }</c>, a private setter, a readonly field): it is generated like any collection and its
    /// elements are added to the collection the object already holds (<see cref="FakeModel{T}.Fill"/>). Such a member is not <see cref="IsSettable"/>.
    /// </summary>
    public bool FillsExisting { get; init; }

    /// <summary>Whether the member is an optional constructor parameter with a declared default (<see cref="DefaultValue"/>), which <c>WithParameterDefaults()</c> uses.</summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>The declared default of an optional constructor parameter (see <see cref="HasDefaultValue"/>), as a value of <see cref="Type"/>.</summary>
    public object? DefaultValue { get; init; }

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
public abstract class FakeModel<T> : IFakeModel, IPopulateTarget<T>
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
    public virtual T Populate(T instance, FakeScope scope, PopulateMode mode) => Populator.Populate(this, instance, scope, mode);

    /// <inheritdoc />
    object? IFakeModel.CreateObject(FakeScope scope) => Create(scope);

    /// <inheritdoc />
    object IFakeModel.PopulateObject(object instance, FakeScope scope, PopulateMode mode) => Populate((T)instance, scope, mode)!;

    /// <inheritdoc />
    object? IFakeModel.GetMember(object instance, int member) => Get((T)instance, member);

    /// <inheritdoc />
    object IFakeModel.SetMember(object instance, int member, object? value) => Set((T)instance, member, value)!;

    bool IPopulateTarget<T>.IsUnsetAt(T instance, int member) => IsUnset(instance, member);

    object? IPopulateTarget<T>.ValueAt(T instance, int member) => Get(instance, member);

    T IPopulateTarget<T>.WithValueAt(T instance, int member, object? value) => Set(instance, member, value);

    Func<FakeScope, object?> IPopulateTarget<T>.ProducerOf(int member) => Fallback(member);

    void IPopulateTarget<T>.FillAt(T instance, int member, object? values, bool replace) => Fill(instance, member, values, replace);
}

/// <summary>What <see cref="Populator"/> needs from a model, so typed models and the untyped reflection model fill objects with one algorithm.</summary>
internal interface IPopulateTarget<T>
{
    /// <summary>The members, by index.</summary>
    IReadOnlyList<FakeMember> Members { get; }

    /// <summary>Whether a member of <paramref name="instance"/> still holds its default value.</summary>
    bool IsUnsetAt(T instance, int member);

    /// <summary>A member's value (boxed).</summary>
    object? ValueAt(T instance, int member);

    /// <summary>Sets a member; returns the instance (a copy for value types).</summary>
    T WithValueAt(T instance, int member, object? value);

    /// <summary>The producer of a member's value from its convention, type and annotations.</summary>
    Func<FakeScope, object?> ProducerOf(int member);

    /// <summary>Adds generated elements to the collection a read-only member holds; <paramref name="replace"/> empties it first.</summary>
    void FillAt(T instance, int member, object? values, bool replace);
}

/// <summary>The populate algorithm, shared by typed models and the untyped reflection model.</summary>
internal static class Populator
{
    public static T Populate<T>(IPopulateTarget<T> target, T instance, FakeScope scope, PopulateMode mode)
    {
        var members = target.Members;
        var (generate, current) = Decide(target, instance, scope, mode);

        // Members are produced in declaration order, as when creating an object, so a seed gives the same values.
        for (var i = 0; i < members.Count; i++)
        {
            if (generate[i])
                instance = target.WithValueAt(instance, i, scope.GetObject(i, target.ProducerOf(i)));
            else if (members[i].IsNested && current[i] is { } existing && scope.Include(i))
                instance = target.WithValueAt(instance, i, scope.PopulateNested(members[i].Type, existing, mode));
        }

        // Read-only collections are filled last, in place: an empty one gets elements, and PopulateMode.All replaces what it holds.
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].FillsExisting && scope.Include(i) && (mode == PopulateMode.All || target.IsUnsetAt(instance, i)))
                target.FillAt(instance, i, scope.GetObject(i, target.ProducerOf(i)), mode == PopulateMode.All);
        }

        return instance;
    }

    /// <summary>
    /// Decides first which members are generated, so that kept members shape the identity before any value is generated; returns that decision and the
    /// values that are kept.
    /// </summary>
    private static (bool[] Generate, object?[] Current) Decide<T>(IPopulateTarget<T> target, T instance, FakeScope scope, PopulateMode mode)
    {
        var members = target.Members;
        var generate = new bool[members.Count];
        var current = new object?[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            if (!members[i].IsSettable)
                continue;
            var unset = target.IsUnsetAt(instance, i);
            generate[i] = scope.Include(i) && (mode == PopulateMode.All || unset);
            if (!generate[i] && !unset)
            {
                current[i] = target.ValueAt(instance, i);
                scope.Adopt(members[i], current[i]);
            }
        }

        return (generate, current);
    }
}
