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
    private readonly ReflectionTypeModel _inner = ReflectionTypeModel.Get(typeof(T));

    /// <inheritdoc />
    public override IReadOnlyList<FakeMember> Members => _inner.Members;

    /// <inheritdoc />
    public override T Create(FakeScope scope) => (T)_inner.CreateObject(scope)!;

    /// <inheritdoc />
    public override T Set(T instance, int member, object? value) => (T)_inner.SetObject(instance!, member, value);
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
    private readonly Func<FakeScope, object?>[] _fallbacks;

    private ReflectionTypeModel(Type type)
    {
        Type = type;
        var nullability = new NullabilityInfoContext();
        var members = new List<FakeMember>();
        var setters = new List<Action<object, object?>?>();
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
            AddMember(name, parameter.ParameterType, nullable, isConstructorParameter: true, isInitOnly: false, setter: null);
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
            AddMember(member.Name, memberType, nullable, isConstructorParameter: false, initOnly, CreateSetter(type, member));
        }

        Members = [.. members];
        _setters = [.. setters];
        _fallbacks = [.. fallbacks];
        _construct = CreateConstructor(type, constructor);

        void AddMember(string name, Type memberType, bool nullable, bool isConstructorParameter, bool isInitOnly, Action<object, object?>? setter)
        {
            var category = FakeMember.ScalarCategory(memberType);
            var convention = ConventionRules.Match(name, category, type.Name);
            members.Add(new FakeMember(name, memberType, convention, isConstructorParameter, isInitOnly));
            setters.Add(setter);
            fallbacks.Add(Producer(memberType, name, convention, nullable));
        }
    }

    /// <summary>The populated type.</summary>
    public Type Type { get; }

    /// <summary>Constructor parameters first, then writable members in declaration order.</summary>
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
            if (scope.Include(i))
                _setters[i]!(instance, scope.GetObject(i, _fallbacks[i]));
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

    /// <summary>Builds the value producer for a member type: scalar conventions, collections, nested objects.</summary>
    private static Func<FakeScope, object?> Producer(Type type, string memberName, ConventionKind convention, bool nullableReference)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            var inner = Producer(underlying, memberName, convention, false);
            return s => s.MaybeNull() ? null : inner(s);
        }

        var produce = NonNullProducer(type, memberName, convention);
        return nullableReference ? s => s.MaybeNull() ? null : produce(s) : produce;
    }

    /// <summary>Producer for a non-nullable type: scalars, byte arrays, collections, then nested objects.</summary>
    private static Func<FakeScope, object?> NonNullProducer(Type type, string memberName, ConventionKind convention)
    {
        var category = FakeMember.ScalarCategory(type);
        if (category != TypeCategory.Other)
            return s => ConventionValues.Boxed(s, type, category, convention);

        if (type == typeof(byte[]))
            return s => s.Bytes();

        // Collections: elements use the singular member name for conventions ("Emails" → email addresses).
        var element = ElementType(type, out var key);
        if (element is not null)
        {
            var singular = memberName.EndsWith('s') ? memberName[..^1] : memberName;
            var elementProducer = Producer(element, singular, ConventionRules.Match(singular, FakeMember.ScalarCategory(element), ""), false);
            if (key is not null)
            {
                var keyProducer = Producer(key, singular + "Key", ConventionKind.None, false);
                var dictionaryType = typeof(Dictionary<,>).MakeGenericType(key, element);
                return s => FillDictionary(s, (IDictionary)Activator.CreateInstance(dictionaryType)!, keyProducer, elementProducer);
            }

            if (type.IsArray)
            {
                return s =>
                {
                    var count = s.CollectionCount();
                    var array = System.Array.CreateInstance(element, count);
                    for (var i = 0; i < count; i++)
                        array.SetValue(elementProducer(s), i);
                    return array;
                };
            }

            if (IsSet(type))
            {
                var setType = typeof(HashSet<>).MakeGenericType(element);
                var add = setType.GetMethod("Add")!;
                return s =>
                {
                    var set = Activator.CreateInstance(setType)!;
                    var count = s.CollectionCount();
                    for (var i = 0; i < count; i++)
                        add.Invoke(set, [elementProducer(s)]);
                    return set;
                };
            }

            var listType = type.IsClass && !type.IsAbstract && typeof(IList).IsAssignableFrom(type) ? type : typeof(List<>).MakeGenericType(element);
            return s =>
            {
                var list = (IList)Activator.CreateInstance(listType)!;
                var count = s.CollectionCount();
                for (var i = 0; i < count; i++)
                    list.Add(elementProducer(s));
                return list;
            };
        }

        // Framework types (System.*, Microsoft.*) are not populated recursively; their constructors expect real data.
        var ns = type.Namespace ?? "";
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal) || type.IsGenericType)
            return static _ => null;

        return s => s.NestedObject(type);
    }

    /// <summary>Adds up to the collection size of distinct keys to <paramref name="dictionary"/>.</summary>
    private static object FillDictionary(FakeScope s, IDictionary dictionary, Func<FakeScope, object?> key, Func<FakeScope, object?> value)
    {
        var count = s.CollectionCount();
        for (var i = 0; i < count * 10 && dictionary.Count < count; i++)
        {
            var k = key(s);
            if (k is not null && !dictionary.Contains(k))
                dictionary.Add(k, value(s));
        }

        return dictionary;
    }

    /// <summary>Element type of arrays and generic collections; <paramref name="key"/> is set for dictionaries.</summary>
    private static Type? ElementType(Type type, out Type? key)
    {
        key = null;
        if (type == typeof(string))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        if (!type.IsGenericType)
            return null;

        var definition = type.GetGenericTypeDefinition();
        var args = type.GetGenericArguments();
        if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
        {
            key = args[0];
            return args[1];
        }

        return definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
               definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) ||
               definition == typeof(HashSet<>) || definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>)
            ? args[0]
            : null;
    }

    /// <summary>Whether the type is <c>HashSet&lt;T&gt;</c>, <c>ISet&lt;T&gt;</c> or <c>IReadOnlySet&lt;T&gt;</c>.</summary>
    private static bool IsSet(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() is var d && (d == typeof(HashSet<>) || d == typeof(ISet<>) || d == typeof(IReadOnlySet<>));

    /// <summary>Public settable/init properties and non-readonly fields, base class members first, in declaration order.</summary>
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

        return [.. members.OrderBy(m => Depth(m.DeclaringType!)).ThenBy(m => m.MetadataToken)];

        static int Depth(Type t)
        {
            var depth = 0;
            for (var b = t.BaseType; b is not null; b = b.BaseType)
                depth++;
            return depth;
        }
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

    /// <summary>Whether the property's setter is <c>init</c>.</summary>
    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) == true;

    /// <summary>Upper-cases the first character (constructor parameter → property name).</summary>
    private static string Capitalize(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
