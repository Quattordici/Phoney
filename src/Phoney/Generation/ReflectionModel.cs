using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Phoney.Generation;

/// <summary>Typed wrapper over <see cref="ReflectionTypeModel"/>.</summary>
[RequiresUnreferencedCode("Populates types with reflection.")]
[RequiresDynamicCode("Populates types with reflection.")]
internal sealed class ReflectionModel<T> : FakeModel<T>
{
    // A value type of its own (a number, a string, an address, a tuple…) has no members to populate: it is produced the way a member of that type is.
    private readonly Func<FakeScope, object?>? _value = ReflectionTypeModel.RootValue(typeof(T));
    private readonly ReflectionTypeModel _inner;

    public ReflectionModel()
    {
        _inner = _value is null ? ReflectionTypeModel.Get(typeof(T)) : null!;
    }

    /// <inheritdoc />
    public override IReadOnlyList<FakeMember> Members => _value is null ? _inner.Members : [];

    /// <inheritdoc />
    public override T Create(FakeScope scope) => (T)(_value is null ? _inner.CreateObject(scope) : _value(scope))!;

    /// <inheritdoc />
    public override T Set(T instance, int member, object? value) => (T)_inner.SetObject(instance!, member, value);

    /// <inheritdoc />
    public override object? Get(T instance, int member) => _inner.GetObject(instance!, member);

    /// <inheritdoc />
    public override bool IsUnset(T instance, int member) => _inner.IsUnsetObject(instance!, member);

    /// <inheritdoc />
    public override void Fill(T instance, int member, object? values, bool replace) => _inner.FillObject(instance!, member, values, replace);

    /// <inheritdoc />
    public override Func<FakeScope, object?> Fallback(int member) => _inner.FallbackOf(member);
}

/// <summary>
/// Populates any class, struct or record with reflection: picks a constructor (parameterless, or the one with
/// the most parameters such as a record's primary constructor), then sets public writable properties and fields
/// in declaration order. Delegates are compiled once per type and cached.
/// </summary>
[RequiresUnreferencedCode("Populates types with reflection.")]
[RequiresDynamicCode("Populates types with reflection.")]
internal sealed class ReflectionTypeModel : IFakeModel
{
    private static readonly ConcurrentDictionary<Type, ReflectionTypeModel> Cache = new();

    private readonly Func<object?[], object> _construct;
    private readonly int _constructorParameterCount;
    private readonly Action<object, object?>?[] _setters;
    private readonly Action<object?, object?, bool>?[] _fillers;
    private readonly Func<object, object?>?[] _getters;
    private readonly object?[] _defaults;
    private readonly Func<FakeScope, object?>[] _fallbacks;

    private ReflectionTypeModel(Type type)
    {
        Type = type;
        var nullability = new NullabilityInfoContext();
        var members = new List<FakeMember>();
        var setters = new List<Action<object, object?>?>();
        var fillers = new List<Action<object?, object?, bool>?>();
        var getters = new List<Func<object, object?>?>();
        var defaults = new List<object?>();
        var fallbacks = new List<Func<FakeScope, object?>>();
        var writable = WritableMembers(type);

        // Constructor parameters come first; properties they initialize are not set again.
        var constructor = ChooseConstructor(type);
        var parameters = constructor?.GetParameters() ?? [];
        foreach (var parameter in parameters)
        {
            var match = writable.FirstOrDefault(m => string.Equals(m.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                writable.Remove(match);
            var name = match?.Name ?? Capitalize(parameter.Name ?? "Arg");
            var nullable = nullability.Create(parameter).ReadState == NullabilityState.Nullable;
            var constraints = MemberConstraints.Read(parameter, match);
            AddMember(name, parameter.ParameterType, nullable, isConstructorParameter: true, isInitOnly: false, accessor: match, constraints);
        }

        _constructorParameterCount = parameters.Length;
        foreach (var member in writable)
        {
            var (memberType, nullable, initOnly) = member switch
            {
                PropertyInfo p => (p.PropertyType, nullability.Create(p).WriteState == NullabilityState.Nullable, IsInitOnly(p)),
                FieldInfo f => (f.FieldType, nullability.Create(f).WriteState == NullabilityState.Nullable, false),
                _ => throw new InvalidOperationException(),
            };
            AddMember(member.Name, memberType, nullable, isConstructorParameter: false, initOnly, member, MemberConstraints.Read(member));
        }

        // Read-only collections come last, so their elements are drawn after everything else (existing seeded data does not move).
        var taken = parameters.Select(p => p.Name ?? "").Concat(members.Select(m => m.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var member in FilledMembers(type))
        {
            if (!taken.Add(member.Name))
                continue;
            var (memberType, nullable) = member switch
            {
                PropertyInfo p => (p.PropertyType, nullability.Create(p).ReadState == NullabilityState.Nullable),
                FieldInfo f => (f.FieldType, nullability.Create(f).ReadState == NullabilityState.Nullable),
                _ => throw new InvalidOperationException(),
            };
            AddMember(member.Name, memberType, nullable, isConstructorParameter: false, isInitOnly: false, member, MemberConstraints.Read(member), fillsExisting: true);
        }

        Members = [.. members];
        _fillers = [.. fillers];
        _setters = [.. setters];
        _getters = [.. getters];
        _defaults = [.. defaults];
        _fallbacks = [.. fallbacks];
        _construct = CreateConstructor(type, constructor);

        void AddMember(string name, Type memberType, bool nullable, bool isConstructorParameter, bool isInitOnly, MemberInfo? accessor, MemberConstraints? constraints, bool fillsExisting = false)
        {
            // A format annotation ([EmailAddress]…) says more than the member name; [Required] members are never null.
            var category = FakeMember.ConventionCategory(memberType);
            var convention = constraints?.Format ?? ConventionRules.Match(name, category, type.Name);
            // Constructor parameters behind a settable property (a record's positional properties) can be set on an existing object too.
            members.Add(new FakeMember(name, memberType, convention, isConstructorParameter, isInitOnly, constraints, isSettable: accessor is not null && !fillsExisting, isNested: IsNestedType(memberType), fillsExisting));
            setters.Add(accessor is null || fillsExisting ? null : CreateSetter(type, accessor));
            fillers.Add(fillsExisting ? CollectionFillers.Create(memberType) : null);
            getters.Add(accessor is null ? null : CreateGetter(type, accessor));
            defaults.Add(memberType.IsValueType ? Activator.CreateInstance(memberType) : null);
            fallbacks.Add(Producer(memberType, name, convention, nullable && constraints?.Required != true, constraints));
        }
    }

    /// <summary>The populated type.</summary>
    public Type Type { get; }

    /// <summary>Constructor parameters first, then writable members (base class first; fields, then properties, each in declaration order), then read-only collections.</summary>
    public IReadOnlyList<FakeMember> Members { get; }

    /// <summary>The cached model for <paramref name="type"/>.</summary>
    public static ReflectionTypeModel Get(Type type) => Cache.GetOrAdd(type, static t => new ReflectionTypeModel(t));

    /// <summary>Creates and populates an instance.</summary>
    public object? CreateObject(FakeScope scope)
    {
        var arguments = _constructorParameterCount == 0 ? [] : new object?[_constructorParameterCount];
        for (var i = 0; i < arguments.Length; i++)
            arguments[i] = scope.GetObject(i, _fallbacks[i]);

        var instance = _construct(arguments);
        for (var i = _constructorParameterCount; i < _setters.Length; i++)
        {
            if (!scope.Include(i))
                continue;
            var value = scope.GetObject(i, _fallbacks[i]);
            if (_fillers[i] is { } fill)
                fill(_getters[i]!(instance), value, false);
            else
                _setters[i]!(instance, value);
        }

        return instance;
    }

    /// <summary>Sets a member after creation; constructor parameters cannot be set.</summary>
    public object SetObject(object instance, int member, object? value)
    {
        var setter = _setters[member]
            ?? throw new InvalidOperationException($"'{Members[member].Name}' of {Type.Name} is a constructor parameter and cannot be set after creation; use a rule that does not depend on the object.");
        setter(instance, value);
        return instance;
    }

    /// <summary>Adds the elements of a generated collection to the collection a read-only member holds.</summary>
    public void FillObject(object instance, int member, object? values, bool replace) =>
        (_fillers[member] ?? throw new InvalidOperationException($"'{Members[member].Name}' of {Type.Name} is not a collection that is filled in place."))(GetObject(instance, member), values, replace);

    /// <summary>Reads a settable member, or the collection of a read-only collection member.</summary>
    public object? GetObject(object instance, int member) =>
        (_getters[member] ?? throw new InvalidOperationException($"'{Members[member].Name}' of {Type.Name} cannot be read from an existing object."))(instance);

    /// <inheritdoc />
    object? IFakeModel.GetMember(object instance, int member) => GetObject(instance, member);

    /// <inheritdoc />
    object IFakeModel.SetMember(object instance, int member, object? value) => SetObject(instance, member, value);

    /// <summary>Whether a settable member still holds its default value.</summary>
    public bool IsUnsetObject(object instance, int member) => FakeScope.IsUnsetBoxed(GetObject(instance, member), _defaults[member]);

    /// <summary>The producer of a member's convention or type value.</summary>
    public Func<FakeScope, object?> FallbackOf(int member) => _fallbacks[member];

    /// <summary>Populates an existing instance; the untyped twin of <see cref="FakeModel{T}.Populate"/>.</summary>
    public object PopulateObject(object instance, FakeScope scope, PopulateMode mode) =>
        Populator.Populate(Members, instance, scope, mode, IsUnsetObject, GetObject, SetObject, FallbackOf, FillObject);

    /// <summary>Whether a member of this type is created and populated recursively; mirrors the last branch of <see cref="NonNullProducer"/>.</summary>
    private static bool IsNestedType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (FakeMember.ConventionCategory(type) != TypeCategory.Other || type == typeof(byte[]) || CollectionTypes.ElementType(type, out _) is not null)
            return false;
        return !IsFrameworkType(type);
    }

    /// <summary>Framework types (System.*, Microsoft.*) are not populated recursively; their constructors expect real data.</summary>
    private static bool IsFrameworkType(Type type)
    {
        var ns = type.Namespace ?? "";
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    /// <summary>
    /// The parts of a <see cref="ValueTuple"/>, <see cref="Tuple"/> (up to seven) or <see cref="KeyValuePair{TKey, TValue}"/>, the names their conventions are matched with
    /// (<c>Item1</c>… or <c>Key</c>, <c>Value</c>) and the constructor taking them; <see langword="null"/> for other types.
    /// </summary>
    private static (Type[] Types, string[] Names, ConstructorInfo Constructor)? CompositeParts(Type type)
    {
        if (!type.IsGenericType)
            return null;
        var definition = type.GetGenericTypeDefinition();
        var types = type.GetGenericArguments();
        string[] names;
        if (definition == typeof(KeyValuePair<,>))
        {
            names = ["Key", "Value"];
        }
        else if (types.Length <= 7 && definition.Namespace == "System" && definition.Name.StartsWith(definition.IsValueType ? "ValueTuple`" : "Tuple`", StringComparison.Ordinal))
        {
            names = new string[types.Length];
            for (var i = 0; i < names.Length; i++)
                names[i] = "Item" + (i + 1);
        }
        else
        {
            return null;
        }

        return type.GetConstructor(types) is { } constructor ? (types, names, constructor) : null;
    }

    /// <summary>A delegate calling a constructor with its arguments in an array.</summary>
    private static Func<object?[], object> CompileConstructor(ConstructorInfo constructor)
    {
        var arguments = Expression.Parameter(typeof(object?[]), "arguments");
        var parameters = constructor.GetParameters();
        var call = Expression.New(constructor, parameters.Select((p, i) => (Expression)Expression.Convert(Expression.ArrayIndex(arguments, Expression.Constant(i)), p.ParameterType)));
        return Expression.Lambda<Func<object?[], object>>(Expression.Convert(call, typeof(object)), arguments).Compile();
    }

    /// <summary>Interfaces and abstract classes, which are created as one of their subtypes (also when generic).</summary>
    private static bool IsPolymorphic(Type type) => type.IsInterface || type.IsAbstract;

    /// <summary>
    /// The producer for a type that is generated as a value of its own when it is asked for directly (<c>Fake.One&lt;int&gt;()</c>, <c>Fake.Many&lt;Guid&gt;(5)</c>,
    /// <c>Fake.One&lt;List&lt;Customer&gt;&gt;()</c>): scalars, byte buffers, collections, tuples and key/value pairs, produced like members of that type, and
    /// interfaces and abstract classes, created as one of their subtypes; <see langword="null"/> for objects, which have members to populate.
    /// </summary>
    internal static Func<FakeScope, object?>? RootValue(Type type)
    {
        if (FakeMember.ScalarCategory(type) != TypeCategory.Other || type == typeof(byte[]) || type == typeof(Memory<byte>) || type == typeof(ReadOnlyMemory<byte>) ||
            CollectionTypes.IsGeneratedCollection(type) || CompositeParts(type) is not null)
        {
            return Producer(type, "", ConventionKind.None, false);
        }

        if (IsPolymorphic(type) && !type.ContainsGenericParameters)
            return s => s.NestedObject(type) ?? throw NoSubtype(type);
        return null;
    }

    /// <summary>The exception for an interface or abstract class asked for directly that has no subtype to create.</summary>
    private static InvalidOperationException NoSubtype(Type type)
    {
        var kind = type.IsInterface ? "an interface" : "an abstract class";
        return new InvalidOperationException(
            $"{type.Name} is {kind}, so Phoney needs a subtype to create. Register one on the generator with .Subtype<{type.Name}, YourType>(), " +
            $"app-wide with Fake.Subtypes.Add<{type.Name}, YourType>(), or with [JsonDerivedType(typeof(YourType))] on {type.Name}; or create it yourself with .CreateWith(...).");
    }

    /// <summary>Builds the value producer for a member type: scalar conventions, collections, nested objects.</summary>
    internal static Func<FakeScope, object?> Producer(Type type, string memberName, ConventionKind convention, bool nullableReference, MemberConstraints? constraints = null)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            var inner = Producer(underlying, memberName, convention, false, constraints);
            if (constraints?.Required == true)
                return inner;
            return s => s.MaybeNull() ? null : inner(s);
        }

        var produce = NonNullProducer(type, memberName, convention, constraints);
        return nullableReference ? s => s.MaybeNull() ? null : produce(s) : produce;
    }

    /// <summary>Producer for a non-nullable type: scalars, byte arrays, collections, then nested objects.</summary>
    private static Func<FakeScope, object?> NonNullProducer(Type type, string memberName, ConventionKind convention, MemberConstraints? constraints)
    {
        var category = FakeMember.ScalarCategory(type);
        if (category != TypeCategory.Other)
            return s => ConventionValues.Boxed(s, type, category, convention, constraints);

        if (type == typeof(byte[]))
            return constraints is null ? static s => s.Bytes() : s => s.Bytes(constraints);
        if (type == typeof(Memory<byte>))
            return static s => new Memory<byte>(s.Bytes());
        if (type == typeof(ReadOnlyMemory<byte>))
            return static s => new ReadOnlyMemory<byte>(s.Bytes());

        // Tuples and key/value pairs: every part is produced like a member of its own type, in order, by the name Item1… (Key, Value).
        if (CompositeParts(type) is { } composite)
        {
            var producers = new Func<FakeScope, object?>[composite.Types.Length];
            var unset = new object?[producers.Length];
            for (var i = 0; i < producers.Length; i++)
            {
                producers[i] = Producer(composite.Types[i], composite.Names[i], ConventionRules.Match(composite.Names[i], FakeMember.ConventionCategory(composite.Types[i]), ""), false);
                unset[i] = composite.Types[i].IsValueType ? Activator.CreateInstance(composite.Types[i]) : null;
            }

            var construct = CompileConstructor(composite.Constructor);
            return s =>
            {
                var values = new object?[producers.Length];
                for (var i = 0; i < values.Length; i++)
                    values[i] = producers[i](s) ?? unset[i]; // missing: depth limit or cycle
                return construct(values);
            };
        }

        // Two-dimensional arrays are filled row by row; more dimensions are not generated (the member stays null, as for other types without a generator).
        if (type.IsArray && type.GetArrayRank() > 1)
        {
            if (type.GetArrayRank() != 2)
                return static _ => null;
            var cell = type.GetElementType()!;
            var singularName = memberName.EndsWith('s') ? memberName[..^1] : memberName;
            var cellProducer = Producer(cell, singularName, ConventionRules.Match(singularName, FakeMember.ConventionCategory(cell), ""), false);
            return ReflectionCollections.Of(cell).Array2(cellProducer);
        }

        // Date and time types of add-on packages (NodaTime) are produced from the built-in type their category names.
        if (ScalarAdapters.TryGet(type, out var adapter))
            return s => adapter(s, convention, constraints);

        // Collections: elements use the singular member name for conventions ("Emails" → email addresses). They are built by
        // the typed FakeScope helpers the generated models call, so both models share one algorithm and nothing is reflected per element.
        var element = CollectionTypes.ElementType(type, out var key);
        if (element is not null)
        {
            var singular = memberName.EndsWith('s') ? memberName[..^1] : memberName;
            var elementProducer = Producer(element, singular, ConventionRules.Match(singular, FakeMember.ConventionCategory(element), ""), false);
            // Built like List<T>, HashSet<T> or Dictionary<TKey, TValue> whatever the member's type is, then converted to it, as generated models do.
            Func<FakeScope, object?> producer;
            Type built;
            if (key is not null)
            {
                var keyProducer = Producer(key, singular + "Key", ConventionKind.None, false);
                producer = ReflectionCollections.DictionaryOf(key, element, keyProducer, elementProducer, constraints);
                built = typeof(Dictionary<,>).MakeGenericType(key, element);
            }
            else if (type.IsArray)
            {
                return ReflectionCollections.Of(element).Array(elementProducer, constraints);
            }
            else if (CollectionTypes.KindOf(type) == CollectionKind.Set)
            {
                producer = ReflectionCollections.Of(element).Set(elementProducer, constraints);
                built = typeof(HashSet<>).MakeGenericType(element);
            }
            else
            {
                producer = ReflectionCollections.Of(element).List(elementProducer, constraints);
                built = typeof(List<>).MakeGenericType(element);
            }

            var convert = CollectionConverters.Create(type, built);
            return convert is null ? producer : s => producer(s) is { } value ? convert(value) : null;
        }

        // Framework types are not populated recursively (a struct among them keeps its default value, which is what a generated model leaves too);
        // other classes, closed generic ones (Envelope<Order>) included, are created as nested objects, and interfaces and abstract classes go through their subtypes.
        if (IsFrameworkType(type))
        {
            if (!type.IsValueType)
                return static _ => null;
            var unset = Activator.CreateInstance(type);
            return _ => unset;
        }

        return s => s.NestedObject(type);
    }

    /// <summary>
    /// Public settable/init properties and non-readonly fields, base class members first. Within a class the order is that of the metadata tokens, which puts all fields before all
    /// properties (each in declaration order); the source generator sorts the same way (<c>ModelBuilder.InReflectionOrder</c>) so both models draw members in the same order.
    /// </summary>
    private static List<MemberInfo> WritableMembers(Type type)
    {
        var members = new List<MemberInfo>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length == 0 && p.SetMethod is { IsPublic: true } && !p.IsDefined(typeof(CompilerGeneratedAttribute)))
                members.Add(p);
        }

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.IsInitOnly && !f.IsLiteral)
                members.Add(f);
        }

        return [.. members.OrderBy(m => InheritanceDepth(m.DeclaringType!)).ThenBy(m => m.MetadataToken)];
    }

    /// <summary>Public properties without a public setter and readonly fields that hold a collection that can be added to, base class members first, in declaration order.</summary>
    private static List<MemberInfo> FilledMembers(Type type)
    {
        var members = new List<MemberInfo>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true } && p.SetMethod is not { IsPublic: true } && CollectionTypes.IsFillable(p.PropertyType))
                members.Add(p);
        }

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.IsInitOnly && !f.IsLiteral && CollectionTypes.IsFillable(f.FieldType))
                members.Add(f);
        }

        return [.. members.OrderBy(m => InheritanceDepth(m.DeclaringType!)).ThenBy(m => m.MetadataToken)];
    }

    private static int InheritanceDepth(Type t)
    {
        var depth = 0;
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            depth++;
        return depth;
    }

    /// <summary>
    /// A public parameterless constructor if there is one, otherwise the public constructor with the most parameters.
    /// Structs are always default-constructed and then populated (the source generator does the same).
    /// </summary>
    private static ConstructorInfo? ChooseConstructor(Type type)
    {
        if (type.IsValueType)
            return null;
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        return constructors.FirstOrDefault(c => c.GetParameters().Length == 0)
            ?? constructors.Where(c => !IsCopyConstructor(c)).MaxBy(c => c.GetParameters().Length)
            ?? throw new InvalidOperationException($"{type.FullName} has no public constructor Phoney can use. Add one, or use .CreateWith(...) on the generator.");

        // Records have a protected copy constructor; also skip any public T(T other).
        bool IsCopyConstructor(ConstructorInfo c) => c.GetParameters() is [var only] && only.ParameterType == type;
    }

    /// <summary>Compiled constructor call (or reflection invoke without a JIT); default construction for structs.</summary>
    private static Func<object?[], object> CreateConstructor(Type type, ConstructorInfo? constructor)
    {
        if (constructor is null)
            return _ => Activator.CreateInstance(type)!;
        if (!RuntimeFeature.IsDynamicCodeCompiled)
            return constructor.Invoke;

        var args = Expression.Parameter(typeof(object?[]), "args");
        var parameters = constructor.GetParameters()
            .Select((p, i) => (Expression)Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), p.ParameterType));
        var body = Expression.Convert(Expression.New(constructor, parameters), typeof(object));
        return Expression.Lambda<Func<object?[], object>>(body, args).Compile();
    }

    /// <summary>Compiled member setter; reflection for structs (in-place on the box) and without a JIT.</summary>
    private static Action<object, object?> CreateSetter(Type type, MemberInfo member)
    {
        // Boxed structs must be mutated in place, which only reflection does; compiled setters would copy.
        if (type.IsValueType || !RuntimeFeature.IsDynamicCodeCompiled)
        {
            return member switch
            {
                PropertyInfo p => p.SetValue,
                FieldInfo f => f.SetValue,
                _ => throw new InvalidOperationException(),
            };
        }

        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var memberType = member is PropertyInfo prop ? prop.PropertyType : ((FieldInfo)member).FieldType;
        var access = Expression.MakeMemberAccess(Expression.Convert(target, type), member);
        var assign = Expression.Assign(access, Expression.Convert(value, memberType));
        return Expression.Lambda<Action<object, object?>>(assign, target, value).Compile();
    }

    /// <summary>Compiled member getter; reflection for structs and without a JIT.</summary>
    private static Func<object, object?> CreateGetter(Type type, MemberInfo member)
    {
        if (type.IsValueType || !RuntimeFeature.IsDynamicCodeCompiled)
        {
            return member switch
            {
                PropertyInfo p => p.GetValue,
                FieldInfo f => f.GetValue,
                _ => throw new InvalidOperationException(),
            };
        }

        var target = Expression.Parameter(typeof(object), "target");
        var access = Expression.MakeMemberAccess(Expression.Convert(target, type), member);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), target).Compile();
    }

    /// <summary>Whether the property's setter is <c>init</c>.</summary>
    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) == true;

    /// <summary>Upper-cases the first character (constructor parameter → property name).</summary>
    internal static string Capitalize(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
