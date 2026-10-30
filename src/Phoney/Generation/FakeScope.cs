using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Phoney.Generation;

/// <summary>
/// The context of one object being generated: the faker, the generator's rules, the object's coherent identity
/// (so first name, last name, email and avatar match) and the nesting depth. Generated models call into it.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class FakeScope
{
    private readonly RuleSlot?[]? _slots;
    private readonly PathNode?[]? _children;
    private int _member = -1;
    private readonly FakeScope? _parent;
    private readonly Type _type;

    internal FakeScope(Faker faker, GenerationSettings settings, RuleSlot?[]? slots, long index, Type type, FakeScope? parent = null, PathNode?[]? children = null)
    {
        _children = children;
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

    /// <summary>The person or place this object describes; shared by name, email, avatar, birthdate, age and address conventions.</summary>
    internal Identity Identity => _facts.Identity(Faker, _slots);

    /// <summary>The object's creation time, drawn once (see <see cref="Timeline"/>); with causal timelines a nested object is created after its parent.</summary>
    internal DateTimeOffset Created => _facts.Created(Faker, Settings.CausalTimelines ? _parent : null, _slots);

    /// <summary>The creation time the object was given, or <see langword="null"/> when it is drawn.</summary>
    internal DateTimeOffset? GivenCreated => _facts.GivenCreated(_slots);

    /// <summary>The later of two moments.</summary>
    internal static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    /// <summary>Whether a "created"-style member already received <see cref="Created"/>.</summary>
    internal bool CreatedClaimed
    {
        get => _facts.CreatedClaimed;
        set => _facts.CreatedClaimed = value;
    }

    // A scope is allocated for every object, so its state is kept compact: what it is told about itself is a struct, -1 for "no forced count".
    private ObjectFacts _facts;
    private int _forcedCount = -1;

    /// <summary>
    /// Makes the next collection created in this scope get exactly <paramref name="count"/> elements (to break or hit a length limit), without a draw, until the
    /// returned value is disposed: a member that stays <see langword="null"/> never asks for the count, and the next member must not get it.
    /// </summary>
    internal ForcedCount ForceCount(int count)
    {
        _forcedCount = count;
        return new ForcedCount(this);
    }

    /// <summary>Ends a <see cref="ForceCount"/>.</summary>
    internal readonly struct ForcedCount(FakeScope scope) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => scope._forcedCount = -1;
    }

    // Set when NestedObject returns null because the object can't exist here (depth limit, cycle, no subtype or model), so collections leave that element out.
#pragma warning disable S1450 // set by NestedObject and read by TryElement, two separate calls
    private bool _cutOff;
#pragma warning restore S1450

    /// <summary>
    /// Produces one collection element; <see langword="false"/> when it was cut off (see <see cref="NestedObject"/>) and must be left out. Only a
    /// <see langword="null"/> element counts: one whose own parts were cut off (a dictionary without keys, a tuple without an object) is still a value.
    /// </summary>
    private bool TryElement<TElement>(Func<FakeScope, TElement> element, out TElement value)
    {
        _cutOff = false;
        value = element(this);
        var cutOff = _cutOff && value is null;
        _cutOff = false;
        return !cutOff;
    }

    /// <summary>
    /// Whether <paramref name="value"/> still holds its default: <see langword="null"/>, <c>default(T)</c> (0, <see langword="false"/>,
    /// <see cref="Guid.Empty"/>…), an empty string or an empty collection. <see cref="PopulateMode.MissingOnly"/> fills such members.
    /// </summary>
    public static bool IsUnset<TValue>(TValue value)
    {
        if (value is null)
            return true;
        if (value is string text)
            return text.Length == 0;
        if (value is IEnumerable sequence)
            return IsEmpty(sequence);
        return typeof(TValue).IsValueType && EqualityComparer<TValue>.Default.Equals(value, default!);
    }

    /// <summary>Like <see cref="IsUnset{TValue}"/> for a boxed value; <paramref name="defaultValue"/> is <c>default</c> of the member's type (<see langword="null"/> for reference and nullable types).</summary>
    internal static bool IsUnsetBoxed(object? value, object? defaultValue) => value switch
    {
        null => true,
        string text => text.Length == 0,
        IEnumerable sequence => IsEmpty(sequence),
        _ => defaultValue is not null && value.Equals(defaultValue),
    };

    /// <summary>Whether <paramref name="sequence"/> has no elements; a default <c>ImmutableArray&lt;T&gt;</c> counts as empty.</summary>
    internal static bool IsEmpty(IEnumerable sequence)
    {
        try
        {
            var enumerator = sequence.GetEnumerator();
            try
            {
                return !enumerator.MoveNext();
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }
        catch (InvalidOperationException)
        {
            return true; // a default ImmutableArray<T> is not initialized and refuses to be enumerated: it holds nothing
        }
    }

    /// <summary>
    /// Tells the object about a value that is kept as it is while populating (a set <c>FirstName</c>, birthdate, sex/gender enum or creation time), so generated
    /// values that belong with it (email, username, age, later dates) agree (see <see cref="ObjectFacts.Adopt"/>). Must be called before any value is generated.
    /// </summary>
    internal void Adopt(FakeMember member, object? value) => _facts.Adopt(Faker, member, value);

    /// <summary>
    /// Populates an existing nested object through the model of its runtime type (so a <c>Circle</c> in an <c>IShape</c>
    /// member is filled as a circle), stopping at the depth limit and on cycles (the object is then left as it is).
    /// </summary>
    internal object PopulateNested(Type type, object existing, PopulateMode mode)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (Depth + 1 > Settings.MaxDepth)
            return existing;
        var runtimeType = existing.GetType();
        if (runtimeType != type && (ModelRegistry.CanGet(runtimeType) || type.IsInterface || type.IsAbstract))
            type = runtimeType;
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._type == type)
                return existing;
        }

        var model = ModelRegistry.Get(type);
        if (model is null)
            return existing;
        var nested = new FakeScope(Faker, Settings, Settings.Conventions?.SlotsFor(type, model), Faker.Random.Long(0, int.MaxValue), type, this);
        return Comparisons.Apply(model, model.PopulateObject(existing, nested, mode), nested)!;
    }

    /// <summary>The name of the type being generated, for messages.</summary>
    internal string TypeName => _type.Name;

    /// <summary>Whether something other than the member's own producer gives <paramref name="member"/> its value: a rule, the feed, a custom convention, an ignore.</summary>
    internal bool HasSource(int member) => _slots?[member] is { Kind: not RuleKind.Default };

    // Created on the first record: most objects keep, break, record and claim nothing.
    private ObjectLedger? _ledger;

    /// <summary>What this object recorded about its members, or <see langword="null"/> when it recorded nothing.</summary>
    internal ObjectLedger? Ledger => _ledger;

    /// <summary>Records that <c>Populate</c> keeps the value <paramref name="member"/> already has.</summary>
    internal void Keep(int member) => (_ledger ??= new()).Keep(member);

    /// <summary>Records that the member being produced must differ from the member its <c>[Compare]</c> names (an <c>Invalid</c> case).</summary>
    internal void BreakComparison() => (_ledger ??= new()).BreakComparison(_member);

    /// <summary>Whether the model should set <paramref name="member"/> while creating the object.</summary>
    /// <remarks>False for ignored members and for members set afterwards by dependent rules.</remarks>
    public bool Include(int member) =>
        _slots is null || _slots[member] is not { Kind: RuleKind.Ignore or RuleKind.Dependent };

    /// <summary>Returns the value for <paramref name="member"/>: from a generator rule if there is one, otherwise from <paramref name="fallback"/>.</summary>
    public TValue Get<TValue>(int member, Func<FakeScope, TValue> fallback)
    {
        _member = member; // the nested objects created for this member (also elements of its collection) learn their position from it
        var slot = _slots?[member];
        if (slot is null)
            return fallback(this);
        if (!slot.Unique && slot.Filter is null && !slot.RecordsKeyPart && slot.Gives == GivenValue.None)
            return Produce(slot, fallback);

        TValue value;
        if (!slot.Unique && slot.Filter is null)
            value = Produce(slot, fallback);
        else if (slot.RetriesObject)
            value = ProduceForObjectRetry(slot, fallback);
        else
            value = ProduceChecked(slot, fallback);
        if (slot.RecordsKeyPart)
            (_ledger ??= new()).RecordKeyPart(member, value);
        if (slot.Gives != GivenValue.None)
            _facts.Give(Faker, _slots, slot.Gives, value);

        return value;
    }

    /// <summary>Produces a value that has to be unique or satisfy a condition, drawing the member again until it does.</summary>
    private TValue ProduceChecked<TValue>(RuleSlot slot, Func<FakeScope, TValue> fallback)
    {
        // A condition is checked first, so only accepted values use up the unique ones.
        var attempts = slot.Attempts;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var value = Produce(slot, fallback);
            if (slot.Filter is { } accepts && !accepts(value))
                continue;
            if (!slot.Unique || slot.TryClaim(value))
                return value;
        }

        if (slot.Filter is null)
            throw new InvalidOperationException($"Could not generate a unique value for {_type.Name}.{slot.MemberName} after {attempts} attempts. Give the member a source with more distinct values, or raise maxAttempts.");
        var unique = slot.Unique ? "unique and " : "";
        throw new InvalidOperationException(
                $"Could not generate {_type.Name}.{slot.MemberName} {unique}satisfying its Where condition after {attempts} attempts. "
                + "Almost every generated value is rejected: give the member values that can pass with .With(…), loosen the condition, or raise maxAttempts.");
    }

    /// <summary>Produces a unique identity value once; a duplicate marks the object for another attempt instead of drawing the member again.</summary>
    private TValue ProduceForObjectRetry<TValue>(RuleSlot slot, Func<FakeScope, TValue> fallback)
    {
        var value = Produce(slot, fallback);
        (_ledger ??= new()).Produced(slot, value);
        return value;
    }

    /// <summary>
    /// Claims the unique identity values and <paramref name="combinations"/> of an accepted object; false when one was produced before or another thread
    /// claimed one of them first (the object is then generated again).
    /// </summary>
    internal bool TryClaimPending(IReadOnlyList<(UniqueCombination Combination, object?[] Values)>? combinations = null) =>
        // Only root scopes have unique slots, and they share the generator's slots array, so it serialises the claims of every object of the run.
        ObjectLedger.TryClaim(_ledger, _slots!, combinations);

    /// <summary>Untyped <see cref="Get{TValue}"/> used by the reflection model.</summary>
    internal object? GetObject(int member, Func<FakeScope, object?> fallback) => Get(member, fallback);

    /// <summary>Creates a nested object of type <typeparamref name="TValue"/>, or <see langword="default"/> past the depth limit or on a cycle.</summary>
    public TValue? Nested<TValue>() => (TValue?)NestedObject(typeof(TValue));

    /// <summary>Creates a list of <see cref="CollectionCount"/> elements.</summary>
    public List<TElement> List<TElement>(Func<FakeScope, TElement> element, MemberConstraints? constraints = null)
    {
        var count = CollectionCount(constraints);
        var list = new List<TElement>(count);
        for (var i = 0; i < count; i++)
        {
            if (TryElement(element, out var value))
                list.Add(value);
        }

        return list;
    }

    /// <summary>Adds <paramref name="values"/> to <paramref name="target"/> (a read-only member's collection); with <paramref name="replace"/> it is emptied first. Does nothing without a target or for a read-only one.</summary>
    public static void AddAll<TElement>(ICollection<TElement>? target, IEnumerable<TElement>? values, bool replace = false)
    {
        if (target is null || values is null || target.IsReadOnly)
            return;
        if (replace)
            target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    /// <summary>Adds the entries of <paramref name="values"/> to <paramref name="target"/> (a read-only member's dictionary); with <paramref name="replace"/> it is emptied first.</summary>
    public static void AddAll<TKey, TValue>(IDictionary<TKey, TValue>? target, IEnumerable<KeyValuePair<TKey, TValue>>? values, bool replace = false)
        where TKey : notnull
    {
        if (target is null || values is null || target.IsReadOnly)
            return;
        if (replace)
            target.Clear();
        foreach (var (key, value) in values)
            target[key] = value;
    }

    /// <summary>Creates a collection of <typeparamref name="TCollection"/> from the <see cref="List{TElement}"/> that <see cref="List{TElement}"/> would build (immutable lists, queues, stacks…).</summary>
    public TCollection ListAs<TCollection, TElement>(Func<List<TElement>, TCollection> convert, Func<FakeScope, TElement> element, MemberConstraints? constraints = null) =>
        convert(List(element, constraints));

    /// <summary>Creates a collection of <typeparamref name="TCollection"/> from the set that <see cref="Set{TElement}"/> would build (immutable and frozen sets, sorted sets).</summary>
    public TCollection SetAs<TCollection, TElement>(Func<HashSet<TElement>, TCollection> convert, Func<FakeScope, TElement> element, MemberConstraints? constraints = null) =>
        convert(Set(element, constraints));

    /// <summary>Creates a collection of <typeparamref name="TCollection"/> from the dictionary that <see cref="Dictionary{TKey, TValue}"/> would build (immutable, frozen, sorted and concurrent dictionaries).</summary>
    public TCollection DictionaryAs<TCollection, TKey, TValue>(Func<Dictionary<TKey, TValue>, TCollection> convert, Func<FakeScope, TKey> key, Func<FakeScope, TValue> value, MemberConstraints? constraints = null)
        where TKey : notnull =>
        convert(Dictionary(key, value, constraints));

    /// <summary>Creates an array of <see cref="CollectionCount"/> elements.</summary>
    public TElement[] Array<TElement>(Func<FakeScope, TElement> element, MemberConstraints? constraints = null)
    {
        var array = new TElement[CollectionCount(constraints)];
        var length = 0;
        for (var i = 0; i < array.Length; i++)
        {
            if (TryElement(element, out var value))
                array[length++] = value;
        }

        return length == array.Length ? array : array[..length];
    }

    /// <summary>Creates a two-dimensional array of <see cref="CollectionCount"/> rows and as many columns, filled row by row.</summary>
    public TElement[,] Array2<TElement>(Func<FakeScope, TElement> element)
    {
        var rows = CollectionCount();
        var columns = CollectionCount();
        var array = new TElement[rows, columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
                array[row, column] = element(this);
        }

        return array;
    }

    /// <summary>Creates a set of up to <see cref="CollectionCount"/> distinct elements.</summary>
    public HashSet<TElement> Set<TElement>(Func<FakeScope, TElement> element, MemberConstraints? constraints = null)
    {
        var count = CollectionCount(constraints);
        var set = new HashSet<TElement>(count); // sized up front; a set without removals enumerates in insertion order whatever its capacity
        // A cut-off element still counts once, as the null it used to be, so the loop draws exactly what it always did.
        var cutOff = 0;
        for (var i = 0; i < count * 10 && set.Count + cutOff < count; i++)
        {
            if (TryElement(element, out var value))
                set.Add(value);
            else
                cutOff = 1;
        }

        return set;
    }

    /// <summary>Creates a dictionary of up to <see cref="CollectionCount"/> entries (duplicate keys are skipped).</summary>
    public Dictionary<TKey, TValue> Dictionary<TKey, TValue>(Func<FakeScope, TKey> key, Func<FakeScope, TValue> value, MemberConstraints? constraints = null)
        where TKey : notnull
    {
        var count = CollectionCount(constraints);
        var dictionary = new Dictionary<TKey, TValue>(count);
        HashSet<TKey>? cutOff = null; // keys whose value was cut off: they still count, so the loop draws exactly what it always did
        for (var i = 0; i < count * 10 && dictionary.Count + (cutOff?.Count ?? 0) < count; i++)
        {
            // The value is only produced for a new key, like the reflection model, so both consume randomness alike.
            var k = key(this);
            if (k is null || dictionary.ContainsKey(k) || cutOff?.Contains(k) == true)
                continue;
            if (TryElement(value, out var v))
                dictionary.Add(k, v);
            else
                (cutOff ??= new HashSet<TKey>(dictionary.Comparer)).Add(k);
        }
        return dictionary;
    }

    /// <summary>Returns 16 random bytes, or as many as the member's <c>[MinLength]</c>/<c>[MaxLength]</c>/<c>[Length]</c> allow nearest to 16 (used for <c>byte[]</c> members).</summary>
    public byte[] Bytes(MemberConstraints? constraints) =>
        Bytes(constraints is null ? 16 : Math.Clamp(16, constraints.MinLength ?? 0, Math.Max(constraints.MinLength ?? 0, constraints.MaxLength ?? int.MaxValue)));

    /// <summary>Returns <paramref name="length"/> random bytes.</summary>
    public byte[] Bytes(int length = 16)
    {
        var bytes = new byte[length];
        Faker.Random.Bytes(bytes);
        return bytes;
    }

    /// <summary>
    /// A random collection size within the generator's configured range, narrowed to the member's
    /// <c>[MinLength]</c>/<c>[MaxLength]</c>/<c>[Length]</c> when it has them.
    /// </summary>
    public int CollectionCount(MemberConstraints? constraints = null)
    {
        if (_forcedCount >= 0)
        {
            var forced = _forcedCount;
            _forcedCount = -1; // for the member's own collection only, not for collections nested in its elements
            return forced;
        }

        var min = Settings.MinCollectionSize;
        var max = Settings.MaxCollectionSize;
        if (constraints is { MinLength: { } atLeast })
            min = Math.Max(min, atLeast);
        if (constraints is { MaxLength: { } atMost })
            max = Math.Min(max, atMost);
        if (max < min)
            max = constraints?.MaxLength ?? min; // the annotation wins over the generator's default range
        if (max < min)
            min = max;
        return Faker.Random.Int(min, max);
    }

    /// <summary>Whether a nullable member should be left <see langword="null"/>, per the generator's null probability.</summary>
    public bool MaybeNull() => Settings.NullProbability > 0 && Faker.Random.Bool(Settings.NullProbability);

    /// <summary>
    /// Creates a nested object, stopping at the depth limit and on cycles. A generator registered for the type
    /// (<see cref="ObjectGenerator{T}.WithGeneratorFor{TNested}"/>) creates it; otherwise a type with subtypes
    /// (<see cref="ObjectGenerator{T}.Subtype{TBase, T1}"/>, <c>Fake.Subtypes</c>, <c>[JsonDerivedType]</c>) is created as one of
    /// them through its model, and an interface or abstract class without subtypes stays <see langword="null"/>.
    /// </summary>
    internal object? NestedObject(Type type)
    {
        if (Depth + 1 > Settings.MaxDepth)
            return CutOff();

        // A generator registered for the declared type decides everything, subtype included.
        if (FindGenerator(type) is { } declared)
            return CreateWith(declared, type);

        var declaredType = type;
        if (ConcreteType(type) is not { } concrete)
            return CutOff();
        type = concrete;

        // The subtype that was picked may have a generator of its own.
        if (type != declaredType && FindGenerator(type) is { } picked)
            return CreateWith(picked, type);

        if (IsBeingCreated(type))
            return CutOff(); // cycle: Customer → Order → Customer

        var model = ModelRegistry.Get(type);
        if (model is null)
            return CutOff();
        var (slots, children) = RulesFor(type, model);
        var nested = new FakeScope(Faker, Settings, slots, Faker.Random.Long(0, int.MaxValue), type, this, children);
        return Comparisons.Apply(model, model.CreateObject(nested), nested);
    }

    /// <summary>
    /// The type to create for a member declared as <paramref name="type"/>: one of its subtypes when it has any (the generator's and <c>Fake.Subtypes</c>,
    /// else <c>[JsonDerivedType]</c>; a draw only when there are several), the type itself when it is concrete, <see langword="null"/> for an interface or
    /// abstract class without subtypes.
    /// </summary>
    private Type? ConcreteType(Type type)
    {
        var registered = Settings.Subtypes.TryGetValue(type, out var listed);
        if (registered)
            Settings.Usage?.Subtype(type);
        var subtypes = registered ? listed : Subtypes.DefaultsOf(type);
        if (registered || subtypes.Length > 0)
            return subtypes.Length == 1 ? subtypes[0] : subtypes[Faker.Random.Index(subtypes.Length)];
        return type.IsInterface || type.IsAbstract ? null : type;
    }

    /// <summary>
    /// The rules for the members of a nested <paramref name="type"/>: custom conventions and path rules reach them through slots, so both kinds of model
    /// apply them in <see cref="Get{TValue}"/>; a path rule position also gives the positions below it.
    /// </summary>
    private (RuleSlot?[]? Slots, PathNode?[]? Children) RulesFor(Type type, IFakeModel model)
    {
        var slots = Settings.Conventions?.SlotsFor(type, model); // also when a path rule position replaces them: binding the conventions marks them as used
        if (_children is not null && (uint)_member < (uint)_children.Length && _children[_member] is { } position)
        {
            var bound = position.Bind(type, model, Settings.Conventions);
            return (bound.Slots, bound.Children);
        }

        return (slots, null);
    }

    /// <summary>Returns <see langword="null"/> for an object that can't be created here, and marks it so a collection leaves the element out.</summary>
    private object? CutOff()
    {
        _cutOff = true;
        return null;
    }

    /// <summary>The generator registered for <paramref name="type"/> by this scope's generator or, failing that, by the nearest enclosing one.</summary>
    private INestedGenerator? FindGenerator(Type type)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope.Settings.TypeGenerators is { } generators && generators.TryGetValue(type, out var generator))
            {
                scope.Settings.Usage?.Generator(type);
                return generator;
            }
        }

        return null;
    }

    /// <summary>Creates <paramref name="requested"/> with <paramref name="generator"/>, unless that would repeat a type already being created.</summary>
    private object? CreateWith(INestedGenerator generator, Type requested) =>
        IsBeingCreated(requested) || IsBeingCreated(generator.GeneratedType) ? CutOff() : generator.CreateNested(this);

    /// <summary>Whether an object of <paramref name="type"/> is being created further up the graph.</summary>
    private bool IsBeingCreated(Type type)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._type == type)
                return true;
        }

        return false;
    }

    private TValue Produce<TValue>(RuleSlot slot, Func<FakeScope, TValue> fallback) => slot.Kind switch
    {
        RuleKind.Constant => (TValue)slot.Value!,
        // Typed callers (generated models) hit the typed delegate; the reflection model asks for object.
        RuleKind.Factory => slot.Value is Func<Faker, TValue> typed ? typed(Faker) : (TValue)slot.BoxedFactory!(Faker)!,
        RuleKind.Generator => (TValue)((INestedGenerator)slot.Value!).CreateNested(this)!,
        RuleKind.Convention => Convert<TValue>(((Func<FakeScope, object?>)slot.Value!)(this)),
        RuleKind.Constraint => ProduceConstraint(slot, fallback),
        RuleKind.Ignore => default!,
        _ => fallback(this),
    };

    // Apart from Produce, so the closure over the fallback is only allocated for constraint rules, not on every call.
    private TValue ProduceConstraint<TValue>(RuleSlot slot, Func<FakeScope, TValue> fallback) =>
        Convert<TValue>(((Func<FakeScope, Func<FakeScope, object?>, object?>)slot.Value!)(this, s => fallback(s)));

    [return: NotNullIfNotNull(nameof(value))]
    private static TValue Convert<TValue>(object? value) => value is null ? default! : (TValue)value;
}
