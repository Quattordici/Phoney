using System.Collections.Immutable;

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
    bool CausalTimelines = false,
    bool ParameterDefaults = false)
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

    /// <summary>The address's country name.</summary>
    Country,

    /// <summary>The address's country code.</summary>
    CountryCode,

    /// <summary>The person's date of birth.</summary>
    BirthDate,

    /// <summary>The person's sex (a sex or gender enum).</summary>
    Sex,
}

/// <summary>
/// A rule bound to a member for one compiled generator. A slot never changes once it exists: <see cref="MemberPlan"/> makes it once with everything it carries,
/// so slots can be shared between threads, nested objects and path positions without anyone writing to them.
/// </summary>
internal sealed class RuleSlot
{
    private readonly HashSet<object?>? _seen;

    /// <summary>Binds <paramref name="decision"/> to <paramref name="member"/>, with the <paramref name="checks"/> of its produced values.</summary>
    public RuleSlot(FakeMember member, SlotDecision decision, SlotChecks checks = default)
    {
        Kind = decision.Kind;
        Value = decision.Value;
        BoxedFactory = decision.Boxed;
        MaxAttempts = checks.UniqueAttempts;
        MemberName = member.Name;
        Gives = Kind is RuleKind.Constant or RuleKind.Factory or RuleKind.Convention ? GivenValueOf(member) : GivenValue.None;
        _seen = checks.UniqueAttempts > 0 ? [] : null;
        Filter = checks.Filter;
        FilterAttempts = checks.FilterAttempts;
        RetriesObject = checks.RetriesObject;
        RecordsKeyPart = checks.RecordsKeyPart;
    }

    /// <summary>How the rule produces values.</summary>
    public RuleKind Kind { get; }

    /// <summary>The constant, delegate or generator, depending on <see cref="Kind"/>.</summary>
    public object? Value { get; }

    /// <summary>For <see cref="RuleKind.Factory"/>: the factory returning <see cref="object"/>, for untyped (reflection) callers.</summary>
    public Func<Faker, object?>? BoxedFactory { get; }

    /// <summary>Whether produced values must be unique.</summary>
    public bool Unique => _seen is not null;

    /// <summary>A condition from <c>Where(x =&gt; x.Member, …)</c> that produced values must satisfy, or <see langword="null"/>.</summary>
    public Func<object?, bool>? Filter { get; }

    /// <summary>Attempts per value before <see cref="Filter"/> gives up.</summary>
    public int FilterAttempts { get; }

    /// <summary>The name of the member the slot belongs to, for the messages of <see cref="Unique"/> and <see cref="Filter"/>.</summary>
    public string MemberName { get; }

    /// <summary>How many values may be produced for one result: the smallest limit of the checks that apply (uniqueness, condition).</summary>
    public int Attempts => (Unique, Filter) switch
    {
        (false, _) => FilterAttempts,
        (true, null) => MaxAttempts,
        _ => Math.Min(MaxAttempts, FilterAttempts),
    };

    /// <summary>Attempts per value before uniqueness gives up.</summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// Whether a duplicate makes the whole object be generated again instead of the member: set for members whose value comes from the object's
    /// <see cref="Identity"/>, where drawing the member again returns the same cached value (and the name, email and address must keep agreeing).
    /// </summary>
    public bool RetriesObject { get; }

    /// <summary>Whether the produced value is kept on the scope for a <see cref="UniqueCombination"/> (a constructor parameter, which can't be read back from the object).</summary>
    public bool RecordsKeyPart { get; }

    /// <summary>
    /// What the produced value tells the object about itself, when a rule, feed or custom convention gives the member its value: the creation time
    /// (<see cref="FakeScope.Created"/>) that the "updated" and later dates are built around, or the age the birthdate is drawn for.
    /// </summary>
    public GivenValue Gives { get; }

    /// <summary>What a rule, feed or custom convention that gives <paramref name="member"/> its value tells the object (see <see cref="Gives"/>).</summary>
    private static GivenValue GivenValueOf(FakeMember member) => member.Convention switch
    {
        ConventionKind.PastDate => GivenValue.Created,
        ConventionKind.Age => GivenValue.Age,
        ConventionKind.Country => GivenValue.Country,
        ConventionKind.CountryCode => GivenValue.CountryCode,
        ConventionKind.BirthDate => GivenValue.BirthDate,
        ConventionKind.Sex or ConventionKind.Gender => GivenValue.Sex,
        _ when Identity.IsSexEnum(member.Type) => GivenValue.Sex, // an enum such as Gender { Female, Male } is the person's sex whatever the member is called
        _ => GivenValue.None,
    };

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
