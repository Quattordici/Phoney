namespace Phoney.Generation;

/// <summary>
/// What one object is told about itself, and where each fact goes: a person's sex, age, birthdate, name or country to its <see cref="Generation.Identity"/>, its
/// creation time to its <see cref="Timeline"/>. Facts arrive three ways: values rules give (<see cref="Give{TValue}"/>, when their member is produced), values
/// <c>Populate</c> keeps (<see cref="Adopt"/>, before anything is generated), and constants, which draw nothing and are taken when their part is first needed
/// (the identity when it settles, the creation time when it is first read), so a value given before that wins and no draw moves.
/// </summary>
/// <remarks>A scope is allocated for every object, so this is a struct inside it and the identity is created on first use.</remarks>
internal struct ObjectFacts
{
    private Identity? _identity;
    private Timeline _timeline;

    /// <summary>The person this object describes, settled (its sex drawn) after the identity constants of <paramref name="slots"/> are taken.</summary>
    public Identity Identity(Faker faker, RuleSlot?[]? slots)
    {
        var identity = Unsettled(faker);
        if (!identity.IsSettled)
        {
            // An age, country or country code a constant gives is taken first, so the birthdate is drawn for that age and the other country member matches
            // even when it is declared first.
            foreach (var slot in slots ?? [])
            {
                if (slot is { Kind: RuleKind.Constant, Gives: GivenValue.Age or GivenValue.Country or GivenValue.CountryCode or GivenValue.BirthDate or GivenValue.Sex })
                    identity.Give(slot.Gives, slot.Value);
            }

            identity.Settle();
        }

        return identity;
    }

    /// <summary>The object's creation time, drawn once (see <see cref="Timeline.Created"/>).</summary>
    public DateTimeOffset Created(Faker faker, FakeScope? causalParent, RuleSlot?[]? slots) => _timeline.Created(faker, causalParent, slots);

    /// <summary>The creation time the object was given, or <see langword="null"/> when it is drawn.</summary>
    public DateTimeOffset? GivenCreated(RuleSlot?[]? slots) => _timeline.Given(slots);

    /// <summary>Whether a "created"-style member already received the creation time.</summary>
    public bool CreatedClaimed
    {
        readonly get => _timeline.Claimed;
        set => _timeline.Claimed = value;
    }

    /// <summary>
    /// Takes in a value <c>Populate</c> keeps (a set <c>FirstName</c>, birthdate, sex/gender enum or creation time), so generated values that belong with it
    /// (email, age, later dates) agree. Must be called before any value is generated.
    /// </summary>
    public void Adopt(Faker faker, FakeMember member, object? value)
    {
        if (value is null)
            return;
        if (member.Convention == ConventionKind.PastDate)
            _timeline.Give(value); // the object's own creation time: its updated and later dates are generated after it
        else
            Unsettled(faker).Adopt(member, value);
    }

    /// <summary>
    /// Takes in the value a rule just gave a member that says something about the object (<see cref="RuleSlot.Gives"/>). A sex or birthdate the identity can use is
    /// drawn here, where its member is, as the member's convention would have: the draw is part of the object's sequence either way, and leaving it for whoever
    /// reads the person next would move everything generated in between. A value the identity can't use (a text for a sex) draws nothing.
    /// </summary>
    public void Give<TValue>(Faker faker, RuleSlot?[]? slots, GivenValue given, TValue value)
    {
        if (given == GivenValue.Created)
        {
            _timeline.Give(value);
            return;
        }

        object? boxed = value; // boxed once for both
        Unsettled(faker).Give(given, boxed);
        if (!Generation.Identity.CanGive(given, boxed))
            return;
        var identity = Identity(faker, slots);
        if (given == GivenValue.BirthDate)
            _ = identity.BirthDate;
    }

    /// <summary>The identity before its person is drawn: it collects what rules give and what populated objects keep.</summary>
    private Identity Unsettled(Faker faker) => _identity ??= new Identity(faker);
}
