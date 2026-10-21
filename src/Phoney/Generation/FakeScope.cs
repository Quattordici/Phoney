using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Phoney.Data;
using Phoney.Modules;

namespace Phoney.Generation;

/// <summary>Options shared by every object created in one generation run.</summary>
internal sealed record GenerationSettings(
    bool WithNameConventions,
    double NullProbability,
    int MinCollectionSize,
    int MaxCollectionSize,
    int MaxDepth,
    ImmutableDictionary<Type, ImmutableArray<Type>> Subtypes,
    NestedConventions? Conventions = null,
    ImmutableDictionary<Type, INestedGenerator>? TypeGenerators = null,
    RuleUsage? Usage = null,
    bool CausalTimelines = false)
{
    public static readonly GenerationSettings Default = new(true, 0, 1, 3, 4, ImmutableDictionary<Type, ImmutableArray<Type>>.Empty);
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

    /// <summary>A nested <see cref="ObjectGenerator{T}"/>.</summary>
    Generator,

    /// <summary><c>Func&lt;FakeScope, object?&gt;</c> from a custom convention.</summary>
    Convention,

    /// <summary>The model's own convention or type default (used to attach uniqueness without a rule).</summary>
    Default,

    /// <summary>A value that breaks or sits on the limit of the member's data annotations, built from the member's normal value.</summary>
    Constraint,
}

/// <summary>What a value that a rule gives a member tells the object about itself (see <see cref="RuleSlot.Gives"/>).</summary>
internal enum GivenValue
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The object's creation time.</summary>
    Created,

    /// <summary>The person's age.</summary>
    Age,
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

    /// <summary>A condition from <c>Where(x =&gt; x.Member, …)</c> that produced values must satisfy, or <see langword="null"/>. Set while the generator compiles, before the slot is shared.</summary>
    public Func<object?, bool>? Filter { get; set; }

    /// <summary>Attempts per value before <see cref="Filter"/> gives up.</summary>
    public int FilterAttempts { get; set; }

    /// <summary>The name of the member the slot belongs to, for the messages of <see cref="Unique"/> and <see cref="Filter"/>.</summary>
    public string? MemberName { get; set; }

    /// <summary>How many values may be produced for one result: the smallest limit of the checks that apply (uniqueness, condition).</summary>
    public int Attempts => (Unique, Filter) switch
    {
        (false, _) => FilterAttempts,
        (true, null) => MaxAttempts,
        _ => Math.Min(MaxAttempts, FilterAttempts),
    };

    /// <summary>Attempts per value before uniqueness gives up.</summary>
    public int MaxAttempts { get; } = maxAttempts;

    /// <summary>
    /// Whether a duplicate makes the whole object be generated again instead of the member: set for members whose value comes from the object's
    /// <see cref="Identity"/>, where drawing the member again returns the same cached value (and the name, email and address must keep agreeing).
    /// </summary>
    public bool RetriesObject { get; set; }

    /// <summary>Whether the produced value is kept on the scope for a <see cref="UniqueCombination"/> (a constructor parameter, which can't be read back from the object).</summary>
    public bool RecordsKeyPart { get; set; }

    /// <summary>
    /// What the produced value tells the object about itself, when a rule, feed or custom convention gives the member its value: the creation time
    /// (<see cref="FakeScope.Created"/>) that the "updated" and later dates are built around, or the age the birthdate is drawn for.
    /// </summary>
    public GivenValue Gives { get; private set; }

    /// <summary>
    /// Marks the slots of "created"-style date and age members whose values come from a rule, feed or custom convention (see <see cref="Gives"/>).
    /// Called while slots are bound, before they are shared.
    /// </summary>
    internal static void MarkGivenValues(IReadOnlyList<FakeMember> members, RuleSlot?[] slots)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { Kind: RuleKind.Constant or RuleKind.Factory or RuleKind.Convention } slot)
            {
                slot.Gives = members[i].Convention switch
                {
                    ConventionKind.PastDate => GivenValue.Created,
                    ConventionKind.Age => GivenValue.Age,
                    _ => GivenValue.None,
                };
            }
        }
    }

    /// <summary>Records <paramref name="value"/>; false when it was produced before.</summary>
    public bool TryClaim(object? value)
    {
        lock (_seen!)
            return _seen.Add(value);
    }

    /// <summary>Whether <paramref name="value"/> was produced before, without recording it.</summary>
    public bool IsClaimed(object? value)
    {
        lock (_seen!)
            return _seen.Contains(value);
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
    private readonly PathNode?[]? _children;
    private int _member = -1;
    private readonly FakeScope? _parent;
    private readonly Type _type;
    private Identity? _identity;
    private IdentityHints? _hints;

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
    internal Identity Identity => _identity ??= CreateIdentity();

    /// <summary>
    /// Creates the identity; an age a constant rule gives is handed to it first, so the birthdate is drawn for that age even when the birthdate is
    /// declared before the age. A constant draws nothing, so taking it early moves no draws.
    /// </summary>
    private Identity CreateIdentity()
    {
        if (_slots is not null && System.Array.Find(_slots, static slot => slot is { Gives: GivenValue.Age, Kind: RuleKind.Constant }) is { } constant)
            GiveAge(constant.Value);
        return new Identity(Faker, _hints);
    }

    /// <summary>
    /// Makes <paramref name="value"/> (a whole number) the age the object's birthdate is drawn for, while the birthdate isn't drawn yet. The first one counts;
    /// a negative age, or one too old for the reference date, is left to the rule and the birthdate drawn as usual.
    /// </summary>
    private void GiveAge(object? value)
    {
        int? given = value switch
        {
            int i => i,
            long l and >= 0 and <= int.MaxValue => (int)l,
            short h => h,
            byte b => b,
            sbyte sb => sb,
            ushort us => us,
            uint ui and <= int.MaxValue => (int)ui,
            _ => null,
        };
        if (given is not { } age || age < 0 || age >= Faker.ReferenceDate.UtcDateTime.Year)
            return;
        if (_identity is { } identity)
            identity.GiveAge(age);
        else
            (_hints ??= new IdentityHints()).Age ??= age;
    }

    /// <summary>
    /// The object's creation time: the first "created"-style date member gets it, and "updated"-style members fall
    /// between it and the reference date, so <c>UpdatedAt</c> is never before <c>CreatedAt</c>. With causal timelines a nested
    /// object is created between its parent's creation and the reference date (still one draw). A creation time the object is
    /// given (<see cref="GiveCreated"/>) replaces the drawn one, which is still drawn so that the values after it stay the same.
    /// </summary>
    internal DateTimeOffset Created
    {
        get
        {
            if (!_hasCreated)
            {
                if (!_createdGiven)
                    GiveConstantCreated();
                var drawn = Settings.CausalTimelines && _parent is { } parent
                    ? Faker.Date.Between(parent.Created, Later(parent.Created, Faker.ReferenceDate))
                    : Faker.Date.Past();
                if (!_createdGiven)
                    _created = drawn;
                _hasCreated = true;
            }

            return _created;
        }
    }

    /// <summary>The creation time the object was given (see <see cref="GiveCreated"/>), or <see langword="null"/> when it is drawn.</summary>
    internal DateTimeOffset? GivenCreated => _createdGiven ? _created : null;

    /// <summary>
    /// Makes <paramref name="value"/> (a <see cref="DateTimeOffset"/>, <see cref="DateTime"/> or <see cref="DateOnly"/>; anything else is ignored) the object's
    /// creation time: the value a rule gives a "created"-style member, or the one an object being populated keeps. The first one counts, and only while no
    /// date of the object has been built around a drawn creation time yet. A <see cref="DateTime"/> that isn't local is read as UTC.
    /// </summary>
    private void GiveCreated<TValue>(TValue value)
    {
        if (_createdGiven || _hasCreated)
            return;
        DateTimeOffset? given = value switch
        {
            DateTimeOffset moment => moment,
            DateTime { Kind: DateTimeKind.Local } local => new DateTimeOffset(local),
            DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)),
            DateOnly day => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            _ => null,
        };
        if (given is { } created)
        {
            _created = created;
            _createdGiven = true;
        }
    }

    /// <summary>
    /// Gives the object the creation time of a constant rule before a date declared ahead of the "created" member is built around a drawn one.
    /// A constant draws nothing, so taking it early moves no draws; factories and conventions draw, so they count only when their member is reached.
    /// </summary>
    private void GiveConstantCreated()
    {
        if (_slots is not null && System.Array.Find(_slots, static slot => slot is { Gives: GivenValue.Created, Kind: RuleKind.Constant }) is { } constant)
            GiveCreated(constant.Value);
    }

    /// <summary>The later of two moments.</summary>
    internal static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    /// <summary>Whether a "created"-style member already received <see cref="Created"/>.</summary>
    internal bool CreatedClaimed { get; set; }

    // A scope is allocated for every object, so its state is kept compact: no nullable structs, -1 for "no forced count".
    private DateTimeOffset _created;
    private bool _hasCreated;
    private bool _createdGiven;
    private int _forcedCount = -1;

    /// <summary>Makes the next collection created in this scope get exactly <paramref name="count"/> elements (to break or hit a length limit).</summary>
    internal void ForceNextCollectionCount(int? count) => _forcedCount = count ?? -1;

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

    private static bool IsEmpty(IEnumerable sequence)
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
    /// Tells the object's identity about a value that is kept as it is while populating (a set <c>FirstName</c>, birthdate or
    /// sex/gender enum), so generated values that belong to the same person (email, username, age) agree with it.
    /// Must be called before any value is generated.
    /// </summary>
    internal void Adopt(FakeMember member, object? value)
    {
        if (value is null)
            return;
        var hints = _hints ??= new IdentityHints();
        switch (member.Convention)
        {
            case ConventionKind.FirstName when value is string first:
                hints.FirstName = first;
                break;
            case ConventionKind.LastName when value is string last:
                hints.LastName = last;
                break;
            case ConventionKind.PastDate:
                GiveCreated(value); // the object's own creation time: its updated and later dates are generated after it
                break;
            case ConventionKind.Country when value is string country && country.Length > 0:
                hints.Country = country;
                break;
            case ConventionKind.CountryCode when value is string code && code.Length > 0:
                hints.CountryCode = code;
                break;
            case ConventionKind.Age:
                GiveAge(value); // the birthdate is drawn for it, unless a kept birthdate says otherwise
                break;
            case ConventionKind.BirthDate:
                hints.BirthDate = value switch
                {
                    DateOnly d => d,
                    DateTime t => DateOnly.FromDateTime(t),
                    DateTimeOffset o => DateOnly.FromDateTime(o.UtcDateTime),
                    _ => hints.BirthDate,
                };
                break;
            default:
                // A sex/gender enum with Female and Male members says which names and avatar fit the person.
                if (value is Enum e && (member.Convention is ConventionKind.Sex or ConventionKind.Gender || IsSexEnum(e.GetType())))
                {
                    var name = e.ToString();
                    if (name.Equals("Female", StringComparison.OrdinalIgnoreCase))
                        hints.Sex = Modules.Sex.Female;
                    else if (name.Equals("Male", StringComparison.OrdinalIgnoreCase))
                        hints.Sex = Modules.Sex.Male;
                }

                break;
        }
    }

    private static bool IsSexEnum(Type type) =>
        type.Name.Contains("Gender", StringComparison.OrdinalIgnoreCase) || type.Name.Contains("Sex", StringComparison.OrdinalIgnoreCase);

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
        return model.PopulateObject(existing, new FakeScope(Faker, Settings, Settings.Conventions?.SlotsFor(type, model), Faker.Random.Long(0, int.MaxValue), type, this), mode);
    }

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
            (_keyParts ??= [])[member] = value;
        if (slot.Gives == GivenValue.Created)
            GiveCreated(value);
        else if (slot.Gives == GivenValue.Age)
            GiveAge(value);
        return value;
    }

    // Values of constructor parameters that are part of a unique combination, by member index (see RuleSlot.RecordsKeyPart).
    private Dictionary<int, object?>? _keyParts;

    /// <summary>The value produced for <paramref name="member"/> when it is part of a unique combination.</summary>
    internal bool TryGetKeyPart(int member, out object? value)
    {
        value = null;
        return _keyParts is not null && _keyParts.TryGetValue(member, out value);
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

    // Unique identity values of this object, claimed only once the whole object is accepted (see RuleSlot.RetriesObject).
    private List<(RuleSlot Slot, object? Value)>? _pendingClaims;

    /// <summary>Whether a unique identity value of this object was produced before, so the object has to be generated again.</summary>
    internal bool HasDuplicate { get; private set; }

    /// <summary>Produces a unique identity value once; a duplicate marks the object for another attempt instead of drawing the member again.</summary>
    private TValue ProduceForObjectRetry<TValue>(RuleSlot slot, Func<FakeScope, TValue> fallback)
    {
        var value = Produce(slot, fallback);
        if (slot.IsClaimed(value))
            HasDuplicate = true;
        else
            (_pendingClaims ??= []).Add((slot, value));
        return value;
    }

    /// <summary>
    /// Claims the unique identity values and <paramref name="combinations"/> of an accepted object; false when one was produced before or another thread
    /// claimed one of them first (the object is then generated again).
    /// </summary>
    internal bool TryClaimPending(IReadOnlyList<(UniqueCombination Combination, object?[] Values)>? combinations = null)
    {
        if (HasDuplicate)
            return false;
        if (_pendingClaims is null && combinations is null)
            return true;

        // All or nothing, so an object that loses one value to another thread doesn't keep the others claimed. Only root scopes have such slots,
        // and they share the generator's slots array, so it serialises the claims of every object of the run.
        lock (_slots!)
        {
            foreach (var (slot, value) in _pendingClaims ?? [])
            {
                if (slot.IsClaimed(value))
                    return false;
            }

            foreach (var (combination, values) in combinations ?? [])
            {
                if (combination.IsClaimed(values))
                    return false;
            }

            foreach (var (slot, value) in _pendingClaims ?? [])
                slot.TryClaim(value);
            foreach (var (combination, values) in combinations ?? [])
                combination.Claim(values);
        }

        return true;
    }

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
        return model.CreateObject(new FakeScope(Faker, Settings, slots, Faker.Random.Long(0, int.MaxValue), type, this, children));
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

/// <summary>A generator that can create objects nested inside another generator's objects.</summary>
internal interface INestedGenerator
{
    /// <summary>The type of the objects the generator creates.</summary>
    Type GeneratedType { get; }

    /// <summary>
    /// Why the generator can't create nested objects (it verifies its rules, which only happens at its own <c>Generate</c>; it numbers its objects, and every nested
    /// object is position 0), or <see langword="null"/>. Registering it anyway would silently do the wrong thing, so <c>With</c>, <c>WithGeneratorFor</c> and <c>Fake.Generators</c> refuse it.
    /// </summary>
    string? CannotBeNested { get; }

    object? CreateNested(FakeScope parent);
}

/// <summary>
/// The person (and home) an object describes, created on first use so unrelated objects pay nothing. Each part is
/// generated when first needed, so conventions only consume randomness for members the type actually has.
/// </summary>
internal sealed class Identity(Faker faker, IdentityHints? hints)
{
    private string? _firstName = hints?.FirstName;
    private string? _lastName = hints?.LastName;
    private string? _username;
    private string? _email;
    private DateOnly? _birthDate = hints?.BirthDate;
    private int? _age = hints?.Age;
    private AddressProfile? _address;
    private string? _country = hints?.Country;
    private string? _countryCode = hints?.CountryCode;
    private string? _nationalId;

    /// <summary>The person's sex; drives gendered names, avatars and sex/gender enums.</summary>
    public Sex Sex { get; } = hints?.Sex ?? faker.Person.Sex();

    /// <summary>First name, matching <see cref="Sex"/>.</summary>
    public string FirstName => _firstName ??= faker.Person.FirstName(Sex);

    /// <summary>Last name, matching <see cref="Sex"/> where the locale has gendered last names.</summary>
    public string LastName => _lastName ??= faker.Person.LastName(Sex);

    /// <summary>Username derived from the names.</summary>
    public string Username => _username ??= faker.Internet.Username(FirstName, LastName);

    /// <summary>Email address derived from the names.</summary>
    public string Email => _email ??= faker.Internet.Email(FirstName, LastName);

    /// <summary>Date of birth (18–80 years before the reference date).</summary>
    public DateOnly BirthDate => _birthDate ??= _age is { } age ? faker.Date.Birthdate(age, age) : faker.Date.Birthdate(); // one draw either way

    /// <summary>Draws <see cref="BirthDate"/> for <paramref name="age"/>, when it isn't drawn (or given) yet and no age was given before.</summary>
    public void GiveAge(int age)
    {
        if (_birthDate is null)
            _age ??= age;
    }

    /// <summary>Age in whole years at the reference date, matching <see cref="BirthDate"/>.</summary>
    public int Age => AgeAt(BirthDate, DateOnly.FromDateTime(faker.ReferenceDate.UtcDateTime));

    /// <summary>One address, so street, city, zip code, state and country agree.</summary>
    public AddressProfile Address => _address ??= faker.Location.Address();

    /// <summary>
    /// The address's country, or in a locale without a country of its own (<c>en</c>, <c>de</c>…) a drawn one that names the same country as a
    /// <see cref="CountryCode"/> the object already has. It is drawn either way, so every member after it draws what it drew before.
    /// </summary>
    public string Country()
    {
        if (Address.Country is { } own)
            return own;
        var drawn = faker.Location.Country();
        if (_countryCode is { } code && CountryNames.NameOf(code) is { } name && faker.Data.TryStrings(DataKeys.LocationCountry) is { } names && Array.IndexOf((string[])names.Values, name) >= 0)
            return name;
        _country ??= drawn;
        return drawn;
    }

    /// <summary>The address's country code, or a drawn one that matches a <see cref="Country"/> the object already has (see there).</summary>
    public string CountryCode()
    {
        if (Address.CountryCode is { } own)
            return own;
        var drawn = faker.Location.CountryCode();
        if (_country is { } country && CountryNames.CodeOf(country) is { } code)
            return code;
        _countryCode ??= drawn;
        return drawn;
    }

    /// <summary>The locale country's personal identity number, encoding <see cref="BirthDate"/> and <see cref="Sex"/>; empty when the country has no scheme.</summary>
    public string NationalId => _nationalId ??= faker.NationalId.TryPerson(BirthDate, Sex) ?? "";

    /// <summary>Whole years between <paramref name="birthDate"/> and <paramref name="today"/>.</summary>
    internal static int AgeAt(DateOnly birthDate, DateOnly today) =>
        today.Year - birthDate.Year - (today < birthDate.AddYears(today.Year - birthDate.Year) ? 1 : 0);
}

/// <summary>Values of an object that is being populated and already has them, which its identity must agree with.</summary>
internal sealed class IdentityHints
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public DateOnly? BirthDate { get; set; }

    /// <summary>A country a populated object keeps, which a generated country code matches.</summary>
    public string? Country { get; set; }

    /// <summary>A country code a populated object keeps, which a generated country matches.</summary>
    public string? CountryCode { get; set; }

    /// <summary>An age a rule gives or a populated object keeps; a <see cref="BirthDate"/> wins over it.</summary>
    public int? Age { get; set; }

    public Sex? Sex { get; set; }
}
