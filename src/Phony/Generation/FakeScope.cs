using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Phony.Modules;

namespace Phony.Generation;

/// <summary>Options shared by every object created in one generation run.</summary>
internal sealed record GenerationSettings(
    bool UseConventions,
    double NullProbability,
    int MinCollectionSize,
    int MaxCollectionSize,
    int MaxDepth)
{
    public static readonly GenerationSettings Default = new(true, 0, 1, 3, 4);
}

/// <summary>How a generator rule produces a member's value.</summary>
internal enum RuleKind
{
    /// <summary>A fixed value.</summary>
    Constant,

    /// <summary><c>Func&lt;Faker, TValue&gt;</c>.</summary>
    Factory,

    /// <summary><c>Func&lt;Faker, T, TValue&gt;</c>, applied after the object exists.</summary>
    Dependent,

    /// <summary>Leave the member at its default value.</summary>
    Ignore,

    /// <summary>A nested <see cref="Generator{T}"/>.</summary>
    Generator,

    /// <summary><c>Func&lt;FakeScope, object?&gt;</c> from a custom convention.</summary>
    Convention,

    /// <summary>The model's own convention or type default (used to attach uniqueness without a rule).</summary>
    Default,
}

/// <summary>A rule bound to a member index for one compiled generator.</summary>
internal sealed class RuleSlot(RuleKind kind, object? value, bool unique, int maxAttempts, Func<Faker, object?>? boxedFactory = null)
{
    private readonly HashSet<object?>? _seen = unique ? [] : null;

    /// <summary>How the rule produces values.</summary>
    public RuleKind Kind { get; } = kind;

    /// <summary>The constant, delegate or generator, depending on <see cref="Kind"/>.</summary>
    public object? Value { get; } = value;

    /// <summary>For <see cref="RuleKind.Factory"/>: the factory returning <see cref="object"/>, for untyped (reflection) callers.</summary>
    public Func<Faker, object?>? BoxedFactory { get; } = boxedFactory;

    /// <summary>Whether produced values must be unique.</summary>
    public bool Unique => _seen is not null;

    /// <summary>Attempts per value before uniqueness gives up.</summary>
    public int MaxAttempts { get; } = maxAttempts;

    /// <summary>Records <paramref name="value"/>; false when it was produced before.</summary>
    public bool TryClaim(object? value)
    {
        lock (_seen!)
            return _seen.Add(value);
    }
}

/// <summary>
/// The context of one object being generated: the faker, the generator's rules, the object's coherent identity
/// (so first name, last name, email and avatar match) and the nesting depth. Generated models call into it.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class FakeScope
{
    private readonly RuleSlot?[]? _slots;
    private readonly FakeScope? _parent;
    private readonly Type _type;
    private Identity? _identity;

    internal FakeScope(Faker faker, GenerationSettings settings, RuleSlot?[]? slots, long index, Type type, FakeScope? parent = null)
    {
        Faker = faker;
        Settings = settings;
        _slots = slots;
        Index = index;
        _type = type;
        _parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    /// <summary>The faker producing values; use it for custom values so seeding stays reproducible.</summary>
    public Faker Faker { get; }

    /// <summary>Zero-based position of the root object in the generator's sequence.</summary>
    public long Index { get; }

    /// <summary>Nesting depth: 0 for the root object.</summary>
    public int Depth { get; }

    /// <summary>Whether this scope is a nested object rather than the generated item itself.</summary>
    public bool IsNested => _parent is not null;

    /// <summary>Collection sizes, null probability, depth limit and convention switch.</summary>
    internal GenerationSettings Settings { get; }

    /// <summary>The person this object describes; shared by name, email, username and avatar conventions.</summary>
    internal Identity Identity => _identity ??= new Identity(Faker);

    /// <summary>Whether the model should set <paramref name="member"/> while creating the object.</summary>
    /// <remarks>False for ignored members and for members set afterwards by dependent rules.</remarks>
    public bool Include(int member) =>
        _slots is null || _slots[member] is not { Kind: RuleKind.Ignore or RuleKind.Dependent };

    /// <summary>Returns the value for <paramref name="member"/>: from a generator rule if there is one, otherwise from <paramref name="fallback"/>.</summary>
    public TValue Get<TValue>(int member, Func<FakeScope, TValue> fallback)
    {
        var slot = _slots?[member];
        if (slot is null)
            return fallback(this);
        if (!slot.Unique)
            return Produce(slot, fallback);

        for (var attempt = 0; attempt < slot.MaxAttempts; attempt++)
        {
            var value = Produce(slot, fallback);
            if (slot.TryClaim(value))
                return value;
        }

        throw new InvalidOperationException(
            $"Could not generate a unique value for member {member} of {_type.Name} after {slot.MaxAttempts} attempts. Use a source with more distinct values.");
    }

    /// <summary>Untyped <see cref="Get{TValue}"/> used by the reflection model.</summary>
    internal object? GetObject(int member, Func<FakeScope, object?> fallback) => Get(member, fallback);

    /// <summary>Creates a nested object of type <typeparamref name="TValue"/>, or <see langword="default"/> past the depth limit or on a cycle.</summary>
    public TValue? Nested<TValue>() => (TValue?)NestedObject(typeof(TValue));

    /// <summary>Creates a list of <see cref="CollectionCount"/> elements.</summary>
    public List<TElement> List<TElement>(Func<FakeScope, TElement> element)
    {
        var count = CollectionCount();
        var list = new List<TElement>(count);
        for (var i = 0; i < count; i++)
            list.Add(element(this));
        return list;
    }

    /// <summary>Creates an array of <see cref="CollectionCount"/> elements.</summary>
    public TElement[] Array<TElement>(Func<FakeScope, TElement> element)
    {
        var array = new TElement[CollectionCount()];
        for (var i = 0; i < array.Length; i++)
            array[i] = element(this);
        return array;
    }

    /// <summary>Creates a set of up to <see cref="CollectionCount"/> distinct elements.</summary>
    public HashSet<TElement> Set<TElement>(Func<FakeScope, TElement> element)
    {
        var count = CollectionCount();
        var set = new HashSet<TElement>();
        for (var i = 0; i < count * 10 && set.Count < count; i++)
            set.Add(element(this));
        return set;
    }

    /// <summary>Creates a dictionary of up to <see cref="CollectionCount"/> entries (duplicate keys are skipped).</summary>
    public Dictionary<TKey, TValue> Dictionary<TKey, TValue>(Func<FakeScope, TKey> key, Func<FakeScope, TValue> value)
        where TKey : notnull
    {
        var count = CollectionCount();
        var dictionary = new Dictionary<TKey, TValue>(count);
        for (var i = 0; i < count * 10 && dictionary.Count < count; i++)
            dictionary.TryAdd(key(this), value(this));
        return dictionary;
    }

    /// <summary>Returns <paramref name="length"/> random bytes (used for <c>byte[]</c> members).</summary>
    public byte[] Bytes(int length = 16)
    {
        var bytes = new byte[length];
        Faker.Random.Bytes(bytes);
        return bytes;
    }

    /// <summary>A random collection size within the generator's configured range.</summary>
    public int CollectionCount() => Faker.Random.Int(Settings.MinCollectionSize, Settings.MaxCollectionSize);

    /// <summary>Whether a nullable member should be left <see langword="null"/>, per the generator's null probability.</summary>
    public bool MaybeNull() => Settings.NullProbability > 0 && Faker.Random.Bool(Settings.NullProbability);

    /// <summary>Creates a nested object through its model, stopping at the depth limit and on cycles.</summary>
    internal object? NestedObject(Type type)
    {
        if (Depth + 1 > Settings.MaxDepth)
            return null;
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._type == type)
                return null; // cycle: Customer → Order → Customer
        }

        var model = ModelRegistry.Get(type);
        return model?.CreateObject(new FakeScope(Faker, Settings, null, Faker.Random.Long(0, int.MaxValue), type, this));
    }

    private TValue Produce<TValue>(RuleSlot slot, Func<FakeScope, TValue> fallback) => slot.Kind switch
    {
        RuleKind.Constant => (TValue)slot.Value!,
        // Typed callers (generated models) hit the typed delegate; the reflection model asks for object.
        RuleKind.Factory => slot.Value is Func<Faker, TValue> typed ? typed(Faker) : (TValue)slot.BoxedFactory!(Faker)!,
        RuleKind.Generator => (TValue)((INestedGenerator)slot.Value!).CreateNested(this)!,
        RuleKind.Convention => Convert<TValue>(((Func<FakeScope, object?>)slot.Value!)(this)),
        RuleKind.Ignore => default!,
        _ => fallback(this),
    };

    [return: NotNullIfNotNull(nameof(value))]
    private static TValue Convert<TValue>(object? value) => value is null ? default! : (TValue)value;
}

/// <summary>A generator that can create objects nested inside another generator's objects.</summary>
internal interface INestedGenerator
{
    object? CreateNested(FakeScope parent);
}

/// <summary>The person an object describes, created on first use so unrelated objects pay nothing.</summary>
internal sealed class Identity(Faker faker)
{
    private string? _firstName;
    private string? _lastName;
    private string? _username;
    private string? _email;

    /// <summary>The person's sex; drives gendered names, avatars and sex/gender enums.</summary>
    public Sex Sex { get; } = faker.Person.SexType();

    /// <summary>First name, matching <see cref="Sex"/>.</summary>
    public string FirstName => _firstName ??= faker.Person.FirstName(Sex);

    /// <summary>Last name, matching <see cref="Sex"/> where the locale has gendered last names.</summary>
    public string LastName => _lastName ??= faker.Person.LastName(Sex);

    /// <summary>Username derived from the names.</summary>
    public string Username => _username ??= faker.Internet.Username(FirstName, LastName);

    /// <summary>Email address derived from the names.</summary>
    public string Email => _email ??= faker.Internet.Email(FirstName, LastName);
}
