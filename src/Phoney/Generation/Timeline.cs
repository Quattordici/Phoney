namespace Phoney.Generation;

/// <summary>
/// The creation time of one object, which its "updated" and later dates are built around, so <c>UpdatedAt</c> is never before <c>CreatedAt</c>. It is drawn once,
/// on first use. A time the object is given (a rule's value, or a date a populated object keeps) replaces the drawn one, which is still drawn so that the values
/// after it stay the same; the first given time counts, and only while nothing has been built around a drawn one.
/// </summary>
/// <remarks>A mutable struct held by the <see cref="FakeScope"/>, which allocates one per object: it is kept compact (no nullable structs).</remarks>
internal struct Timeline
{
    private DateTimeOffset _created;
    private bool _drawn;
    private bool _given;

    /// <summary>Whether a "created"-style member already received the creation time.</summary>
    public bool Claimed { get; set; }

    /// <summary>
    /// The creation time. With <paramref name="causalParent"/> (causal timelines) the object is created between its parent's creation and the reference date;
    /// either way it is one draw.
    /// </summary>
    /// <param name="faker">The faker the time is drawn from.</param>
    /// <param name="causalParent">The scope of the containing object when timelines are causal, otherwise <see langword="null"/>.</param>
    /// <param name="slots">The object's rule slots, searched for a constant creation time.</param>
    public DateTimeOffset Created(Faker faker, FakeScope? causalParent, RuleSlot?[]? slots)
    {
        if (!_drawn)
        {
            if (!_given)
                GiveConstant(slots);
            var drawn = causalParent is not null
                ? faker.Date.Between(causalParent.Created, FakeScope.Later(causalParent.Created, faker.ReferenceDate))
                : faker.Date.Past();
            if (!_given)
                _created = drawn;
            _drawn = true;
        }

        return _created;
    }

    /// <summary>The creation time the object was given (see <see cref="Give{TValue}"/>), or <see langword="null"/> when it is drawn.</summary>
    public DateTimeOffset? Given(RuleSlot?[]? slots)
    {
        if (!_given && !_drawn)
            GiveConstant(slots); // a future date declared before the "created" member: a constant draws nothing, so taking it early moves no draws
        return _given ? _created : null;
    }

    /// <summary>
    /// Makes <paramref name="value"/> (a <see cref="DateTimeOffset"/>, <see cref="DateTime"/> or <see cref="DateOnly"/>; anything else is ignored) the object's
    /// creation time: the value a rule gives a "created"-style member, or the one an object being populated keeps. The first one counts, and only while no
    /// date of the object has been built around a drawn creation time yet. A <see cref="DateTime"/> that isn't local is read as UTC.
    /// </summary>
    public void Give<TValue>(TValue value)
    {
        if (_given || _drawn)
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
            _given = true;
        }
    }

    /// <summary>
    /// Gives the object the creation time of a constant rule before a date declared ahead of the "created" member is built around a drawn one.
    /// A constant draws nothing, so taking it early moves no draws; factories and conventions draw, so they count only when their member is reached.
    /// </summary>
    private void GiveConstant(RuleSlot?[]? slots)
    {
        if (slots is not null && Array.Find(slots, static slot => slot is { Gives: GivenValue.Created, Kind: RuleKind.Constant }) is { } constant)
            Give(constant.Value);
    }
}
