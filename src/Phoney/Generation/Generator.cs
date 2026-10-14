using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using Phoney.Data;
using Phoney.Generation;

namespace Phoney;

/// <summary>
/// Generates populated <typeparamref name="T"/> objects. Members are filled by name conventions (FirstName,
/// Email, City, Price, CreatedAt…) and type, and can be overridden with fluent rules.
/// </summary>
/// <remarks>
/// <para>
/// Generators are immutable: every configuration method returns a new generator, so a configured generator can
/// be shared, and derived variants are cheap: <c>var admins = users.With(u =&gt; u.Role, "Admin");</c>.
/// Generating is thread-safe.
/// </para>
/// <para>
/// With a <see cref="Seed"/>, item <c>n</c> is always the same object regardless of how many items are generated
/// per call or whether <see cref="GenerateParallel"/> is used, because every item gets its own derived seed.
/// </para>
/// <para>Order of population: constructor arguments, then members in declaration order, then rules that depend
/// on the object (<c>With(x =&gt; x.Email, (f, x) =&gt; …)</c>) in the order they were added, then <see cref="AfterCreate"/> actions.</para>
/// </remarks>
/// <typeparam name="T">The type to generate.</typeparam>
public sealed class Generator<T> : INestedGenerator
{
    private readonly FakeModel<T> _model;
    private readonly Config _config;
    private readonly State _state = new();

    /// <summary>Creates a generator over an explicit model (source-generated models use this constructor).</summary>
    public Generator(FakeModel<T> model) : this(model ?? throw new ArgumentNullException(nameof(model)), Config.Default)
    {
    }

    private Generator(FakeModel<T> model, Config config)
    {
        _model = model;
        _config = config;
    }

    /// <summary>Uses <paramref name="locale"/> instead of <see cref="Fake.Locale"/>.</summary>
    public Generator<T> Locale(string locale) => Derive(_config with { Locale = Locales.Get(locale).Code });

    /// <summary>
    /// Makes generation reproducible: the same seed always yields the same sequence of objects. Relative dates use
    /// <see cref="Faker.DefaultSeededReferenceDate"/> unless <see cref="ReferenceDate"/> is set, so they are stable over time.
    /// </summary>
    public Generator<T> Seed(long seed) => Derive(_config with { Seed = seed });

    /// <summary>Sets the point in time relative dates are computed from (default: now, or a fixed date when seeded).</summary>
    public Generator<T> ReferenceDate(DateTimeOffset referenceDate) => Derive(_config with { ReferenceDate = referenceDate });

    /// <summary>
    /// Reads "now" for relative dates from <paramref name="timeProvider"/> (e.g. a <c>FakeTimeProvider</c>), also when
    /// seeded. An explicit <see cref="ReferenceDate"/> takes precedence.
    /// </summary>
    public Generator<T> TimeProvider(TimeProvider timeProvider) =>
        Derive(_config with { TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)) });

    /// <summary>Sets a member to a fixed value.</summary>
    public Generator<T> With<TValue>(Expression<Func<T, TValue>> member, TValue value) =>
        AddRule(member, new Rule(RuleKind.Constant, value));

    /// <summary>Generates a member with <paramref name="factory"/>, e.g. <c>.With(x =&gt; x.Age, f =&gt; f.Number.Int(18, 65))</c>.</summary>
    public Generator<T> With<TValue>(Expression<Func<T, TValue>> member, Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddRule(member, new Rule(RuleKind.Factory, factory, f => factory(f)));
    }

    /// <summary>
    /// Generates a member from the object after its other members are set, e.g.
    /// <c>.With(x =&gt; x.Email, (f, x) =&gt; f.Internet.Email(x.FirstName, x.LastName))</c>.
    /// </summary>
    public Generator<T> With<TValue>(Expression<Func<T, TValue>> member, Func<Faker, T, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddRule(member, new Rule(RuleKind.Dependent, (Func<Faker, T, object?>)((f, x) => factory(f, x))));
    }

    /// <summary>
    /// Generates a nested member with its own generator, e.g. <c>.With(x =&gt; x.Address, Fake.For&lt;Address&gt;().Locale("sv"))</c>;
    /// the generator may produce a subtype of the member's type: <c>.With(x =&gt; x.Shape, Fake.For&lt;Circle&gt;())</c>.
    /// </summary>
    public Generator<T> With<TValue, TGenerated>(Expression<Func<T, TValue>> member, Generator<TGenerated> generator)
        where TGenerated : TValue
    {
        ArgumentNullException.ThrowIfNull(generator);
        NestedGenerators.RejectIfNotNestable(generator, nameof(generator));
        return AddRule(member, new Rule(RuleKind.Generator, generator));
    }

    /// <summary>
    /// Generates <typeparamref name="T1"/> for every member of type <typeparamref name="TBase"/> in the generated objects
    /// (also nested ones), e.g. <c>.Subtype&lt;IShape, Circle&gt;()</c>. Replaces earlier subtypes of <typeparamref name="TBase"/>
    /// for this generator and takes precedence over <see cref="Fake.Subtypes"/> and <c>[JsonDerivedType]</c>.
    /// </summary>
    public Generator<T> Subtype<TBase, T1>()
        where T1 : TBase => Subtype<TBase>(typeof(T1));

    /// <summary>Generates one of the subtypes, picked at random, for every member of type <typeparamref name="TBase"/>: <c>.Subtype&lt;IShape, Circle, Square&gt;()</c>.</summary>
    public Generator<T> Subtype<TBase, T1, T2>()
        where T1 : TBase
        where T2 : TBase => Subtype<TBase>(typeof(T1), typeof(T2));

    /// <summary>Generates one of the subtypes, picked at random, for every member of type <typeparamref name="TBase"/>.</summary>
    public Generator<T> Subtype<TBase, T1, T2, T3>()
        where T1 : TBase
        where T2 : TBase
        where T3 : TBase => Subtype<TBase>(typeof(T1), typeof(T2), typeof(T3));

    /// <summary>Generates one of the subtypes, picked at random, for every member of type <typeparamref name="TBase"/>.</summary>
    public Generator<T> Subtype<TBase, T1, T2, T3, T4>()
        where T1 : TBase
        where T2 : TBase
        where T3 : TBase
        where T4 : TBase => Subtype<TBase>(typeof(T1), typeof(T2), typeof(T3), typeof(T4));

    /// <summary>Generates one of <paramref name="subtypes"/>, picked at random in the given order, for every member of type <typeparamref name="TBase"/>.</summary>
    /// <exception cref="ArgumentException">A subtype is abstract, an interface, open generic or not assignable to <typeparamref name="TBase"/>.</exception>
    public Generator<T> Subtype<TBase>(params Type[] subtypes) =>
        Derive(_config with { Subtypes = _config.Subtypes.SetItem(typeof(TBase), Subtypes.Validate(typeof(TBase), subtypes)) });

    /// <summary>
    /// Creates every nested <typeparamref name="TNested"/> in the generated objects with <paramref name="generator"/>: members,
    /// collection elements and dictionary values, at any depth, e.g. <c>.Use(Fake.For&lt;Address&gt;().Locale("de"))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule for the member (<c>With(x =&gt; x.Billing, …)</c>) wins over the type's generator, which wins over
    /// <see cref="Subtype{TBase, T1}"/>, <c>Fake.Subtypes</c> and the type's model. The generator is self-contained: it brings its
    /// own rules, locale and settings. A generator used by an inner <see cref="Use{TNested}"/> wins over this one, while types
    /// it does not register are still created by the generators registered here. A later registration for the same type replaces an earlier one.
    /// </para>
    /// <para>
    /// The generated object itself is never replaced, depth limits and cycles still stop nesting with <see langword="null"/>, and objects you
    /// pass to <c>Populate</c> are filled member by member: the generator creates the nested objects that are missing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><typeparamref name="TNested"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public Generator<T> Use<TNested>(Generator<TNested> generator) => Use<TNested, TNested>(generator);

    /// <summary>
    /// Creates every nested <typeparamref name="TBase"/> (an interface, abstract class or base class) with the generator of a
    /// subtype, e.g. <c>.Use&lt;IShape, Circle&gt;(Fake.For&lt;Circle&gt;().With(c =&gt; c.Radius, 2))</c>.
    /// </summary>
    /// <remarks>See <see cref="Use{TNested}"/> for precedence and scope.</remarks>
    /// <exception cref="ArgumentException"><typeparamref name="TBase"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public Generator<T> Use<TBase, TGenerated>(Generator<TGenerated> generator)
        where TGenerated : TBase
    {
        ArgumentNullException.ThrowIfNull(generator);
        var type = typeof(TBase);
        NestedGenerators.ValidateType(type, nameof(generator));
        NestedGenerators.RejectIfNotNestable(generator, nameof(generator));

        return Derive(_config with { TypeGenerators = _config.TypeGenerators.SetItem(type, generator) });
    }

    /// <summary>
    /// Breaks one limit of a member's data annotations, e.g. <c>.Invalid(x =&gt; x.Quantity)</c> gives a quantity just outside its
    /// <c>[Range]</c>, a name one character too long, or an empty <c>[Required]</c> value. Which violation an object gets is
    /// picked per object (reproducible with a <see cref="Seed"/>); every other member stays valid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Violations are derived from <c>[Required]</c>, <c>[StringLength]</c>, <c>[MinLength]</c>/<c>[MaxLength]</c> (strings and collections),
    /// <c>[Range]</c> (numbers and dates), <c>[RegularExpression]</c>, <c>[EmailAddress]</c>/<c>[Phone]</c>/<c>[Url]</c>/<c>[CreditCard]</c>/<c>[Base64String]</c>,
    /// <c>[AllowedValues]</c> and <c>[DeniedValues]</c>, and checked with the real validators. A value that breaks one limit can break
    /// others of the same member (an empty string is also too short); the other members are untouched. The object is the one
    /// <see cref="Generate()"/> would give, with only this member replaced.
    /// </para>
    /// <para>Use <see cref="InvalidCases"/> to get every violation of every member at once.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: the member has no data annotations that can be broken.</exception>
    public Generator<T> Invalid<TValue>(Expression<Func<T, TValue>> member) =>
        AddRule(member, new Rule(RuleKind.Constraint, new ConstraintRequest(Boundary: false, null)));

    /// <summary>
    /// Sets a member exactly on a limit of its data annotations, e.g. <c>.Boundary(x =&gt; x.Quantity)</c> gives the smallest or the
    /// largest value its <c>[Range]</c> allows, a name of the minimum or maximum length, or the minimum or maximum element count.
    /// The value is valid; which edge an object gets is picked per object. See <see cref="Invalid{TValue}"/> for the annotations that are read.
    /// </summary>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: the member has no limits to sit on (patterns, formats and lists of allowed values define a value but have no edge).</exception>
    public Generator<T> Boundary<TValue>(Expression<Func<T, TValue>> member) =>
        AddRule(member, new Rule(RuleKind.Constraint, new ConstraintRequest(Boundary: true, null)));

    /// <summary>
    /// Every way to break the data annotations of the generated type, one object per violation: a quantity below and above its
    /// <c>[Range]</c>, a name too short and too long, an empty and a missing <c>[Required]</c> value… Useful to test that validation rejects each of them.
    /// </summary>
    /// <remarks>
    /// All objects are the one <see cref="Generate()"/> gives for the <see cref="Seed"/> (a random one if none is set), each with a single member
    /// replaced, so a case differs from a valid object only in the member it is about. Rules for that member are replaced; ignored members are skipped.
    /// </remarks>
    /// <example>
    /// <code>
    /// foreach (var c in Fake.For&lt;Order&gt;().Seed(1).InvalidCases())
    ///     validator.Validate(c.Value).IsValid.ShouldBeFalse(c.ToString());   // "Quantity: above the maximum (10)"
    /// </code>
    /// </example>
    public IReadOnlyList<ConstraintCase<T>> InvalidCases() => Cases(boundary: false);

    /// <summary>
    /// Every value exactly on a limit of the generated type's data annotations, one valid object per edge: the smallest and largest value of a
    /// <c>[Range]</c>, strings of the minimum and maximum length, the minimum and maximum element count. Useful for off-by-one tests.
    /// See <see cref="InvalidCases"/> for how the objects are made.
    /// </summary>
    public IReadOnlyList<ConstraintCase<T>> BoundaryCases() => Cases(boundary: true);

    /// <summary>
    /// Sets a member to one of <paramref name="values"/>, e.g. existing ids or objects:
    /// <c>.WithOneOf(o =&gt; o.Customer, customers)</c>.
    /// </summary>
    public Generator<T> WithOneOf<TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("Provide at least one value.", nameof(values));
        return With(member, f => f.Random.Element(values));
    }

    /// <summary>
    /// Sets a member from a random item of <paramref name="source"/>, so object graphs line up:
    /// <c>.WithOneOf(o =&gt; o.CustomerId, customers, c =&gt; c.Id)</c>.
    /// </summary>
    public Generator<T> WithOneOf<TSource, TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TSource> source, Func<TSource, TValue> select)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(select);
        if (source.Count == 0)
            throw new ArgumentException("Provide at least one item.", nameof(source));
        return With(member, f => select(f.Random.Element(source)));
    }

    /// <summary>Leaves a member at its default value.</summary>
    public Generator<T> Ignore<TValue>(Expression<Func<T, TValue>> member) => AddRule(member, new Rule(RuleKind.Ignore, null));

    /// <summary>
    /// Makes a member unique across everything this generator instance produces. Retries up to
    /// <paramref name="maxAttempts"/> times per object, then throws.
    /// </summary>
    public Generator<T> Unique<TValue>(Expression<Func<T, TValue>> member, int maxAttempts = 1000)
    {
        var name = MemberName(member);
        return Derive(_config with { Unique = _config.Unique.SetItem(name, maxAttempts) });
    }

    /// <summary>
    /// Applies <paramref name="factory"/> to every member of type <typeparamref name="TValue"/> whose name matches
    /// <paramref name="nameMatches"/> and has no explicit rule, also in nested objects (before app-wide <see cref="Fake.Conventions"/>), e.g. <c>.WithConvention&lt;string&gt;(n =&gt; n.EndsWith("Code"), f =&gt; f.Random.Replace("??-###"))</c>.
    /// </summary>
    public Generator<T> WithConvention<TValue>(Func<string, bool> nameMatches, Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(nameMatches);
        ArgumentNullException.ThrowIfNull(factory);
        var convention = new CustomConvention(typeof(TValue), nameMatches, s => factory(s.Faker));
        return Derive(_config with { Conventions = _config.Conventions.Add(convention) });
    }

    /// <summary>Gives every member of type <typeparamref name="TValue"/> (also its nullable form) the same <paramref name="value"/>. See <see cref="WithType{TValue}(Func{Faker, TValue})"/>.</summary>
    public Generator<T> WithType<TValue>(TValue value) => WithType(_ => value);

    /// <summary>
    /// Generates every member of type <typeparamref name="TValue"/> (also its nullable form) with <paramref name="factory"/>, whatever its name and
    /// also in nested objects, e.g. <c>.WithType&lt;DateOnly&gt;(f =&gt; f.Date.Past().Date)</c> or <c>.WithType("N/A")</c> for every string.
    /// </summary>
    /// <remarks>
    /// A rule for the member itself (<c>With</c>, <c>Ignore</c>…) wins; this comes before name conventions and before app-wide conventions
    /// (<c>Fake.Conventions</c>). It sets members: the elements of collections and dictionaries keep their generated values, and data
    /// annotations are not applied to the value. Same as <c>WithConvention&lt;TValue&gt;(_ =&gt; true, factory)</c>.
    /// </remarks>
    public Generator<T> WithType<TValue>(Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var convention = new CustomConvention(typeof(TValue), static _ => true, s => factory(s.Faker), nameof(WithType));
        return Derive(_config with { Conventions = _config.Conventions.Add(convention) });
    }

    /// <summary>
    /// Makes <c>Generate</c>, <c>GenerateParallel</c> and <c>Populate</c> throw when a rule whose reach depends on the object graph did not apply
    /// to anything that was generated: a path rule (<c>x =&gt; x.Billing.City</c>, <c>x =&gt; x.Lines.Each().Sku</c>), <see cref="Subtype{TBase, T1}"/>,
    /// <see cref="Use{TNested}"/> and <see cref="WithConvention{TValue}"/>. Rules that are silently skipped become test failures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule is used as soon as it applied once, to any object generated by this generator so far, so a rule that only reaches some objects
    /// (a nullable member, a collection that is sometimes empty) is fine as long as one object in the call had it. A rule stays unused when
    /// the object it is for never exists (a member that is always null or an empty collection, <see cref="MaxDepth"/>, a cycle, another
    /// subtype), when a rule for an enclosing member or a <see cref="Use{TNested}"/> generator creates or skips that object itself, when
    /// <see cref="Populate(T, PopulateMode)"/> keeps an existing object (path rules only shape objects that are created), or when nothing in the
    /// graph has that member or type. The exception lists every unused rule.
    /// </para>
    /// <para>
    /// Rules for the generated object's own members (<c>With(x =&gt; x.Name, …)</c>) always apply and are not checked, and neither is a generator
    /// that creates nested objects for another one: it cannot verify because it has no <c>Generate</c> call of its own, so passing such a generator to
    /// <c>With</c> or <c>Use</c> throws. <see cref="Stream"/> has no end to check at and is not supported; generate a fixed number of objects instead.
    /// <see cref="InvalidCases"/> and <see cref="BoundaryCases"/> are not verified. Nothing is checked when no object is generated.
    /// </para>
    /// </remarks>
    /// <param name="verify"><see langword="false"/> turns the check off again.</param>
    /// <exception cref="InvalidOperationException">After <c>Generate</c>, <c>GenerateParallel</c> or <c>Populate</c>: a rule did not apply to anything.</exception>
    /// <exception cref="NotSupportedException">From <see cref="Stream"/>.</exception>
    public Generator<T> VerifyRules(bool verify = true) => Derive(_config with { Verify = verify });

    /// <summary>
    /// Fails when a scalar member (string, number, date…) has neither a rule, a name convention nor data annotations
    /// that define its value (<c>[EmailAddress]</c>, <c>[Range]</c>, <c>[RegularExpression]</c>, <c>[AllowedValues]</c>), so new
    /// members can't silently get meaningless data. Objects and collections are populated recursively and don't count.
    /// </summary>
    public Generator<T> Strict(bool strict = true) => Derive(_config with { Strict = strict });

    /// <summary>Turns name conventions off: members get values based on their type only.</summary>
    public Generator<T> UseConventions(bool useConventions = true) => Derive(_config with { Settings = _config.Settings with { UseConventions = useConventions } });

    /// <summary>Probability (0–1) that nullable members (<c>int?</c>, <c>string?</c>…) are left <see langword="null"/>. Default 0.</summary>
    public Generator<T> NullProbability(double probability)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(probability);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(probability, 1);
        return Derive(_config with { Settings = _config.Settings with { NullProbability = probability } });
    }

    /// <summary>Sets how many elements generated collections get. Default 1–3.</summary>
    public Generator<T> CollectionSize(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        return Derive(_config with { Settings = _config.Settings with { MinCollectionSize = min, MaxCollectionSize = max } });
    }

    /// <summary>Sets how deep nested objects are generated (default 4); deeper members stay <see langword="null"/>.</summary>
    public Generator<T> MaxDepth(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        return Derive(_config with { Settings = _config.Settings with { MaxDepth = depth } });
    }

    /// <summary>
    /// Gives a member a value that depends on the object's position in the generator's sequence (0 for the first object it generates, continuing over calls), e.g.
    /// <c>.Sequence(x =&gt; x.Code, i =&gt; $"C{i + 1:D4}")</c> gives C0001, C0002… Item <em>n</em> always gets the value for <em>n</em>, whatever the seed, the batch size or
    /// <see cref="GenerateParallel"/>, and an object that <see cref="Where(Func{T, bool}, int)"/> generates again keeps its position.
    /// </summary>
    /// <remarks>
    /// For members of the generated object only. The rule is replaced by a later rule for the member (and replaces an earlier one). A generator with
    /// <c>Sequence</c> or <c>Cycle</c> rules can't be given to <c>With</c>, <c>Use</c> or <c>Fake.Generators.Add</c>: it counts the objects it generates itself, and every
    /// nested object would be number 0. <see cref="Populate(T, PopulateMode)"/> numbers the objects it fills in the same way and keeps members that are set.
    /// </remarks>
    /// <param name="member">The member to number: <c>x =&gt; x.Code</c>.</param>
    /// <param name="factory">The value for the object at a zero-based position.</param>
    public Generator<T> Sequence<TValue>(Expression<Func<T, TValue>> member, Func<long, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        MemberName(member); // a position belongs to the generated object, not to a member of a nested one
        return AddRule(member, new Rule(RuleKind.Convention, (Func<FakeScope, object?>)(scope => factory(scope.Index)), Positional: true));
    }

    /// <summary>
    /// Gives a member the <paramref name="values"/> in turn, by the object's position: the first object gets the first value, the second the second, and after the last
    /// the first again. See <see cref="Sequence{TValue}"/> for what that means for seeds, batches and nested generators.
    /// </summary>
    /// <param name="member">The member to give the values to: <c>x =&gt; x.Status</c>.</param>
    /// <param name="values">The values, in order (copied when the method is called).</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public Generator<T> Cycle<TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("Provide at least one value.", nameof(values));
        var copy = values.ToArray();
        return Sequence(member, i => copy[(int)(i % copy.Length)]);
    }

    /// <summary>
    /// Keeps only objects that satisfy <paramref name="predicate"/>: an object that does not is thrown away and generated again, from the
    /// same place in the seeded sequence, e.g. <c>.Where(o =&gt; o.Total &gt; 100)</c>. Several conditions must all hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The condition sees the finished object: dependent rules and <see cref="AfterCreate"/> actions have run. An object that satisfies
    /// it is exactly the object the generator would give without it, so adding a condition changes only the objects it rejects, and
    /// results still depend on the seed and the position only (also with <see cref="GenerateParallel"/>).
    /// </para>
    /// <para>
    /// Filtering is for conditions most objects meet. When almost nothing does, set the member with <c>With</c> instead of waiting for luck:
    /// a condition that is not met within <paramref name="maxAttempts"/> attempts throws (with several conditions the smallest limit applies).
    /// Creation runs for every attempt, so <see cref="AfterCreate"/> actions may run more than once per result, and a rejected object has
    /// used up its <see cref="Unique{TValue}"/> values.
    /// </para>
    /// <para>
    /// Also applies to what the generator creates as a nested generator (<c>With(x =&gt; x.Address, generator)</c>, <see cref="Use{TNested}"/>).
    /// <see cref="Populate(T, PopulateMode)"/> supports it with <see cref="PopulateMode.All"/> only, because values that are kept cannot be
    /// generated again. <see cref="InvalidCases"/> and <see cref="BoundaryCases"/> ignore it: their objects are made to sit outside or on the edge of the valid ones.
    /// </para>
    /// </remarks>
    /// <param name="predicate">The condition an object must satisfy.</param>
    /// <param name="maxAttempts">How many objects may be generated for one result before giving up (default 1000).</param>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: no object satisfied the condition within <paramref name="maxAttempts"/> attempts.</exception>
    public Generator<T> Where(Func<T, bool> predicate, int maxAttempts = 1000)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        return Derive(_config with { Filters = _config.Filters.Add(new Filter(predicate, maxAttempts)) });
    }

    /// <summary>
    /// Generates one member again until it satisfies <paramref name="predicate"/>, e.g. <c>.Where(o =&gt; o.Total, t =&gt; t &gt; 100)</c>: cheaper than
    /// <see cref="Where(Func{T, bool}, int)"/> because only that member is drawn again, and it filters what a rule or convention for the member produces
    /// (whichever of <c>With</c> and <c>Where</c> comes first). Several conditions on a member must all hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A member that passes on the first try is the value it would have without the condition. Values that are drawn again use the faker's next
    /// randomness, so members after it differ from an unfiltered object, but results still depend on the seed and the position only.
    /// With <see cref="Unique{TValue}"/> a value has to be accepted and new; with several conditions or <c>Unique</c> the smallest <paramref name="maxAttempts"/> applies.
    /// </para>
    /// <para>
    /// For members of the generated object only (not <c>x =&gt; x.Billing.City</c>). A member that is ignored, set from the object
    /// (<c>With(x =&gt; x.Email, (f, x) =&gt; …)</c>) or created by <see cref="CreateWith"/> has nothing to draw again and throws when the generator is first
    /// used. <see cref="Populate(T, PopulateMode)"/> filters the values it generates and keeps the ones it finds. <see cref="InvalidCases"/> and
    /// <see cref="BoundaryCases"/> ignore it.
    /// </para>
    /// </remarks>
    /// <param name="member">The member to filter: <c>x =&gt; x.Name</c>.</param>
    /// <param name="predicate">The condition its value must satisfy.</param>
    /// <param name="maxAttempts">How many values may be drawn for one result before giving up (default 1000).</param>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: no value satisfied the condition within <paramref name="maxAttempts"/> attempts, or the member cannot be filtered.</exception>
    public Generator<T> Where<TValue>(Expression<Func<T, TValue>> member, Func<TValue, bool> predicate, int maxAttempts = 1000)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        var name = MemberName(member);
        Func<object?, bool> accepts = value => predicate((TValue)value!);
        var filter = new MemberFilter(accepts, maxAttempts);
        if (_config.MemberFilters.TryGetValue(name, out var existing))
        {
            var earlier = existing.Accepts;
            filter = new MemberFilter(value => earlier(value) && accepts(value), Math.Min(existing.MaxAttempts, maxAttempts));
        }

        return Derive(_config with { MemberFilters = _config.MemberFilters.SetItem(name, filter) });
    }

    /// <summary>Creates instances with <paramref name="factory"/> instead of a constructor; rules are applied afterwards, conventions are not.</summary>
    public Generator<T> CreateWith(Func<Faker, T> factory) =>
        Derive(_config with { Factory = factory ?? throw new ArgumentNullException(nameof(factory)) });

    /// <summary>
    /// Points members that would close a cycle at the object that contains them, instead of leaving them <see langword="null"/>: with
    /// <c>Order → Lines → Line.Order</c>, every <c>Line.Order</c> is the order that holds the line, like an ORM's navigation properties.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After an object is created (before <see cref="Where(Func{T, bool}, int)"/> and <see cref="AfterCreate"/> see it), every member that is still
    /// <see langword="null"/> and whose type one of the objects above it satisfies gets the nearest such object. Members that have a value are kept, so
    /// a rule for the member wins. Nothing is drawn, so the rest of the data is the data of the same seed without it.
    /// </para>
    /// <para>
    /// It works through settable members (setters, <see langword="init"/>, public fields); constructor-only members of positional records can be neither read nor
    /// set and are left alone. The objects form real cycles afterwards: <c>ToString</c>, <c>Equals</c> and serializers of records and of libraries without cycle
    /// handling may not cope. A nested generator (<c>Use</c>, <c>With</c>) wires the graph it creates, which starts at its own objects. <see cref="Populate(T, PopulateMode)"/>
    /// fills the missing references of an existing graph the same way.
    /// </para>
    /// </remarks>
    /// <param name="enabled"><see langword="false"/> turns it off again.</param>
    public Generator<T> WithBackReferences(bool enabled = true) => Derive(_config with { BackReferences = enabled });

    /// <summary>
    /// Fills members from the rows of a CSV or JSON <see cref="DataFeed"/>: object <em>n</em> takes row <em>n</em> (the rows repeat), and each column fills the member of
    /// the same name, e.g. <c>.WithFeed(DataFeed.FromCsvFile("customers.csv"))</c> with columns <c>FirstName,LastName,City</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Column and member names are compared ignoring case, spaces and underscores (<c>first_name</c> fills <c>FirstName</c>); values are converted to the member's
    /// type with the invariant culture (strings, numbers, booleans, enums by name, dates and times, <see cref="Guid"/>, <see cref="Uri"/>; an empty value of a
    /// nullable member is <see langword="null"/>). A rule for the member (<c>With</c>, <c>Ignore</c>…) wins over the feed, and the feed wins over name conventions
    /// and <c>WithType</c>. Members without a column are generated as usual, independently of the row: add the columns the values must agree with.
    /// </para>
    /// <para>
    /// The row depends on the object's position only, so results are the same whichever way the objects are generated. A generator with a feed can't be given to
    /// <c>With</c>, <c>Use</c> or <c>Fake.Generators.Add</c>: every nested object would take the first row.
    /// </para>
    /// </remarks>
    /// <param name="feed">The rows.</param>
    /// <param name="order">Row <em>n</em> for object <em>n</em> (the default), or a reproducible random row for each object.</param>
    /// <param name="ignoreUnknownColumns">Skip columns that no member of the generated type matches instead of throwing.</param>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: a column matches no member (unless ignored), or a value can't be converted to its member's type; the message names the row and column.</exception>
    public Generator<T> WithFeed(DataFeed feed, FeedOrder order = FeedOrder.InOrder, bool ignoreUnknownColumns = false)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return Derive(_config with { Feed = new FeedSettings(feed, order, ignoreUnknownColumns) });
    }

    /// <summary>Runs <paramref name="action"/> on every generated object, after all members are set.</summary>
    public Generator<T> AfterCreate(Action<Faker, T> action) =>
        Derive(_config with { AfterCreate = _config.AfterCreate.Add(action ?? throw new ArgumentNullException(nameof(action))) });

    /// <summary>Generates one object.</summary>
    public T Generate()
    {
        var run = Start();
        var item = run.Create(_state.NextIndex(1));
        VerifyUsage();
        return item;
    }

    /// <summary>Generates <paramref name="count"/> objects.</summary>
    public List<T> Generate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var run = Start();
        var first = _state.NextIndex(count);
        var list = new List<T>(count);
        for (var i = 0; i < count; i++)
            list.Add(run.Create(first + i));
        if (count > 0)
            VerifyUsage();
        return list;
    }

    /// <summary>Generates objects lazily and endlessly; take what you need: <c>gen.Stream().Take(10)</c>.</summary>
    public IEnumerable<T> Stream()
    {
        if (_config.Verify)
            throw new NotSupportedException($"VerifyRules checks when a call has generated its objects, and a stream never ends. Generate a fixed number of {typeof(T).Name} objects with Generate(count), or call VerifyRules(false).");
        return Endless();

        IEnumerable<T> Endless()
        {
            var run = Start();
            while (true)
                yield return run.Create(_state.NextIndex(1));
        }
    }

    /// <summary>
    /// Generates <paramref name="count"/> objects on all cores. With a <see cref="Seed"/> the result equals
    /// <see cref="Generate(int)"/>, except for <see cref="Unique{TValue}"/> members: which object retries a duplicate
    /// depends on thread timing, so combine uniqueness with sequential generation when results must be stable.
    /// </summary>
    public T[] GenerateParallel(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var first = _state.NextIndex(count);
        var results = new T[count];
        var seed = BaseSeed;
        Parallel.For(
            0,
            count,
            () => Start(seed),
            (i, _, run) =>
            {
                results[i] = run.Create(first + i);
                return run;
            },
            static _ => { });
        if (count > 0)
            VerifyUsage();
        return results;
    }

    /// <summary>
    /// Fills in an object you already have, using the same conventions, rules and annotations as <see cref="Generate()"/>:
    /// <c>new Order { CustomerId = 42 }</c> keeps its customer id and gets everything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <paramref name="mode"/> decides which members are touched; the rules decide their values. Members that are ignored
    /// (<see cref="Ignore{TValue}"/>) are never touched. Rules that depend on the object and <see cref="AfterCreate"/> actions
    /// run afterwards; <see cref="CreateWith"/> is ignored because nothing is created.
    /// </para>
    /// <para>
    /// Kept values shape the generated ones: with a set <c>FirstName</c>, <c>LastName</c>, <c>BirthDate</c> or sex/gender enum, the email,
    /// username, age and avatar are generated to match.
    /// </para>
    /// <para>
    /// Populating draws from the same seeded sequence as generating, so with a <see cref="Seed"/> the result is reproducible.
    /// A member holding <c>false</c> or <c>0</c> counts as unset in <see cref="PopulateMode.MissingOnly"/>; protect such a value with <see cref="Ignore{TValue}"/>.
    /// </para>
    /// </remarks>
    /// <param name="instance">The object to fill in.</param>
    /// <param name="mode">Which members to fill in; by default only the ones that are still unset.</param>
    /// <returns><paramref name="instance"/>, or a populated copy when <typeparamref name="T"/> is a struct.</returns>
    public T Populate(T instance, PopulateMode mode = PopulateMode.MissingOnly)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));
        var run = Start();
        var item = run.Populate(instance, _state.NextIndex(1), mode);
        VerifyUsage();
        return item;
    }

    /// <summary>Fills in every object of <paramref name="instances"/> like <see cref="Populate(T, PopulateMode)"/>, each from its own place in the seeded sequence.</summary>
    /// <returns>The populated objects in the same order (copies for structs).</returns>
    public List<T> Populate(IEnumerable<T> instances, PopulateMode mode = PopulateMode.MissingOnly)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var items = instances as IReadOnlyCollection<T> ?? [.. instances];
        var run = Start();
        var first = _state.NextIndex(items.Count);
        var result = new List<T>(items.Count);
        var index = 0;
        foreach (var item in items)
        {
            if (item is null)
                throw new ArgumentException("The collection contains a null element.", nameof(instances));
            result.Add(run.Populate(item, first + index++, mode));
        }

        if (result.Count > 0)
            VerifyUsage();
        return result;
    }

    /// <inheritdoc />
    Type INestedGenerator.GeneratedType => typeof(T);

    /// <inheritdoc />
    string? INestedGenerator.CannotBeNested =>
        _config.Verify
            ? "verifies its rules (VerifyRules), but a nested generator is never checked: it has no Generate call of its own. Call VerifyRules(false) on it and verify the generator you call Generate on."
            : _config.Rules.Values.Any(r => r.Positional)
                ? "has Sequence or Cycle rules, which count the objects it generates itself, and as a nested generator every object would be number 0. Put the rule on the generator you call Generate on, or use With(x => x.Member, f => …) here."
                : _config.Feed is not null
                    ? "has a data feed (WithFeed), which takes a row per object it generates itself, and as a nested generator every object would take the first row. Put the feed on the generator you call Generate on."
                    : null;

    /// <inheritdoc />
    object? INestedGenerator.CreateNested(FakeScope parent) =>
        Start(parent.Faker.Random.Long(), parent.Faker).Create(0, parent);

    /// <summary>The base seed: the configured one, or one random seed per generator instance.</summary>
    private long BaseSeed => _config.Seed ?? _state.RandomSeed;

    /// <summary>Throws when <see cref="VerifyRules"/> is on and a graph-dependent rule has not applied to anything generated so far.</summary>
    private void VerifyUsage()
    {
        if (_state.Compiled?.Usage?.Unused() is not { Count: > 0 } unused)
            return;

        throw new InvalidOperationException(
            $"The {typeof(T).Name} generator has {(unused.Count == 1 ? "a rule" : $"{unused.Count} rules")} that did not apply to anything it generated:{Environment.NewLine}"
            + string.Join(Environment.NewLine, unused.Select(u => "  - " + u)) + Environment.NewLine
            + "A rule stays unused when the object it is for never exists (a member that is always null or an empty collection, MaxDepth, a cycle, another subtype), "
            + "when a rule for an enclosing member or a Use generator creates or skips that object itself, when Populate keeps an existing object, or when nothing in the graph has that member or type. "
            + "Remove the rule or change what is generated; VerifyRules(false) turns this check off.");
    }

    /// <summary>A new generator with <paramref name="config"/> and fresh state.</summary>
    private Generator<T> Derive(Config config) => new(_model, config);

    /// <summary>One object per case: the seeded base object with a single member replaced by the case.</summary>
    private List<ConstraintCase<T>> Cases(bool boundary)
    {
        var seed = BaseSeed; // one base object for all cases, so they differ in the member they are about only
        var cases = new List<ConstraintCase<T>>();
        foreach (var member in _model.Members)
        {
            if (_config.Rules.TryGetValue(member.Name, out var existing) && existing.Kind == RuleKind.Ignore)
                continue;

            var descriptions = boundary ? ConstraintCases.Boundary(member) : ConstraintCases.Invalid(member);
            for (var i = 0; i < descriptions.Count; i++)
            {
                var rule = new Rule(RuleKind.Constraint, new ConstraintRequest(boundary, i), Added: _config.RuleCount);
                var generator = Derive(_config with { Seed = seed, Rules = _config.Rules.SetItem(member.Name, rule), RuleCount = _config.RuleCount + 1, Filters = [], MemberFilters = _config.MemberFilters.Clear(), Verify = false });
                cases.Add(new ConstraintCase<T>(member.Name, descriptions[i].Description, generator.Generate()));
            }
        }

        return cases;
    }

    /// <summary>
    /// Adds (or replaces) the rule of a member of the generated type, or of a member of a nested object when the selector is a path
    /// (<c>x =&gt; x.Billing.City</c>). The rule remembers when it was added, because dictionaries don't keep that order.
    /// </summary>
    private Generator<T> AddRule<TValue>(Expression<Func<T, TValue>> member, Rule rule)
    {
        var path = MemberPath(member);
        if (path.Length == 1)
            return Derive(_config with { Rules = _config.Rules.SetItem(path[0], rule with { Added = _config.RuleCount }), RuleCount = _config.RuleCount + 1 });

        if (rule.Kind == RuleKind.Dependent)
        {
            throw new ArgumentException(
                $"'{member}' is a member of a nested object, so its rule can't depend on the object ({typeof(T).Name}). Use a rule without the object parameter.",
                nameof(member));
        }

        var spec = new PathSpec(path, new PathRule(rule.Kind, rule.Value, rule.Boxed));
        return Derive(_config with { PathRules = _config.PathRules.SetItem(string.Join('.', path), spec), RuleCount = _config.RuleCount + 1 });
    }

    /// <summary>Prepares a run: compiled rules, a faker for the locale and the base seed. Nested generators inherit the parent's locale and reference date.</summary>
    private Run Start(long? seed = null, Faker? parentFaker = null)
    {
        var compiled = _state.Compiled ?? _state.Publish(Compile());
        var data = parentFaker is not null && _config.Locale is null ? parentFaker.Data : LocaleStore.Get(_config.Locale ?? Fake.Locale);
        var faker = new Faker(data, new Randomizer(0));
        // Explicit date, else the configured clock, else the parent's date (nested generators), else a fixed date for
        // seeded generators so their relative dates don't drift with the clock; unseeded generators use the current time.
        if (_config.ReferenceDate is null && _config.TimeProvider is { } clock)
            faker.TimeProvider = clock;
        else if ((_config.ReferenceDate ?? parentFaker?.ReferenceDate ?? (_config.Seed is not null ? Faker.DefaultSeededReferenceDate : null)) is { } date)
            faker.ReferenceDate = date;
        return new Run(this, compiled, faker, seed ?? BaseSeed);
    }

    /// <summary>The app-wide type generators with this generator's own on top, or <see langword="null"/> when there are none.</summary>
    private static ImmutableDictionary<Type, INestedGenerator>? MergedTypeGenerators(ImmutableDictionary<Type, INestedGenerator> own)
    {
        var all = FakeGenerators.All.SetItems(own);
        return all.IsEmpty ? null : all;
    }

    /// <summary>Binds the rules (by member name) to the model's member indices and validates them.</summary>
    private Compiled Compile()
    {
        var members = _model.Members;
        var slots = new RuleSlot?[members.Count];
        var dependent = new List<(int Member, Func<Faker, T, object?> Rule)>();
        var usage = _config.Verify ? RegisterUsage() : null;
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < members.Count; i++)
            known[members[i].Name] = i;

        // In the order the rules were added: dependent rules draw from the faker, and the first invalid rule is the one reported.
        foreach (var (name, rule) in _config.Rules.OrderBy(r => r.Value.Added))
        {
            if (!known.TryGetValue(name, out var index))
                throw new InvalidOperationException($"{typeof(T).Name}.{name} can't be generated: it is not a public settable member or constructor parameter.");
            if (rule.Kind == RuleKind.Dependent)
            {
                if (members[index].IsConstructorParameter)
                    throw new InvalidOperationException($"{typeof(T).Name}.{name} is a constructor parameter, so it can't depend on the object. Use a rule without the object parameter.");
                dependent.Add((index, (Func<Faker, T, object?>)rule.Value!));
            }

            var value = rule.Kind == RuleKind.Constraint ? ConstraintCases.Resolve(typeof(T).Name, members[index], (ConstraintRequest)rule.Value!) : rule.Value;
            slots[index] = new RuleSlot(rule.Kind, value, _config.Unique.TryGetValue(name, out var attempts), attempts, rule.Boxed);
        }

        // Unique members without an explicit rule keep their convention value but still need a slot.
        foreach (var (name, attempts) in _config.Unique.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            if (!known.TryGetValue(name, out var index))
                throw new InvalidOperationException($"{typeof(T).Name}.{name} is not a generated member.");
            slots[index] ??= new RuleSlot(RuleKind.Default, null, unique: true, attempts);
        }

        if (_config.Feed is { } feed)
            ApplyFeed(feed, members, slots);

        // Custom conventions (per generator, then global) fill members that have no rule.
        var conventions = _config.Conventions.AddRange(FakeConventions.All);
        for (var i = 0; i < members.Count; i++)
        {
            if (slots[i] is { Kind: not RuleKind.Default })
                continue; // an explicit rule wins over conventions
            var match = conventions.FirstOrDefault(c => c.Matches(members[i]));
            if (match is not null)
            {
                usage?.Convention(match);
                slots[i] = new RuleSlot(RuleKind.Convention, match.Factory, slots[i]?.Unique == true, slots[i]?.MaxAttempts ?? 0);
            }
        }

        if (_config.Strict)
        {
            var uncovered = members
                .Where((m, i) => slots[i] is null && m.IsScalar && m.Constraints?.DefinesValue != true
                    && (m.Convention == ConventionKind.None || !_config.Settings.UseConventions))
                .Select(m => m.Name)
                .ToList();
            if (uncovered.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Strict generator for {typeof(T).Name}: no rule or convention for {string.Join(", ", uncovered)}. Add .With(...) or .Ignore(...) for them.");
            }
        }

        // Last, so a member that only has a condition does not count as covered by the strict check above.
        foreach (var (name, filter) in _config.MemberFilters.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!known.TryGetValue(name, out var index))
                throw new InvalidOperationException($"{typeof(T).Name}.{name} can't be generated: it is not a public settable member or constructor parameter, so a Where condition on it has nothing to check.");
            if (slots[index] is { Kind: RuleKind.Ignore })
                throw new InvalidOperationException($"{typeof(T).Name}.{name} is ignored, so its Where condition has nothing to check. Remove the Ignore or the condition.");
            if (slots[index] is { Kind: RuleKind.Dependent })
                throw new InvalidOperationException($"{typeof(T).Name}.{name} depends on the object, so it is set after the object is created and a Where condition on it can't draw it again. Check it with .Where(x => …) on the object instead.");
            if (_config.Factory is not null)
                throw new InvalidOperationException($"{typeof(T).Name}.{name} has a Where condition, but CreateWith decides the values of the objects it creates. Check the object with .Where(x => …) instead.");

            if (_config.Settings.UseConventions && slots[index] is null or { Kind: RuleKind.Default } && IdentityBound.Contains(members[index].Convention))
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name}.{name} comes from the object's identity (one name, email, address and birthdate per object, so that they agree), and drawing it again gives the same value. "
                    + $"Give it a value with .With(x => x.{name}, f => …) and filter that, or check the whole object with .Where(x => …).");
            }

            var slot = slots[index] ??= new RuleSlot(RuleKind.Default, null, unique: false, maxAttempts: 0);
            slot.Filter = filter.Accepts;
            slot.FilterAttempts = filter.MaxAttempts;
            slot.MemberName = name;
        }

        // Subtypes and custom conventions apply to the whole object graph, so they travel with the settings into nested scopes.
        var settings = _config.Settings with
        {
            Subtypes = FakeSubtypes.All.SetItems(_config.Subtypes),
            Conventions = NestedConventions.For(conventions, usage),
            TypeGenerators = MergedTypeGenerators(_config.TypeGenerators),
            Usage = usage,
        };
        return new Compiled(slots, [.. dependent], settings, PathRules.Build(typeof(T), members, _config.PathRules, usage), usage);
    }

    /// <summary>
    /// Gives each member that has a column in the feed (and no rule of its own) its values, converted once now so that a bad value fails here, with its row and column,
    /// instead of in the middle of a run. The slot picks the row from the object's position.
    /// </summary>
    private void ApplyFeed(FeedSettings settings, IReadOnlyList<FakeMember> members, RuleSlot?[] slots)
    {
        var feed = settings.Feed;
        var unknown = new List<string>();
        for (var column = 0; column < feed.Columns.Count; column++)
        {
            var name = DataFeed.Normalize(feed.Columns[column]);
            var index = -1;
            for (var i = 0; i < members.Count && index < 0; i++)
            {
                if (DataFeed.Normalize(members[i].Name) == name)
                    index = i;
            }

            if (index < 0)
            {
                unknown.Add(feed.Columns[column]);
                continue;
            }

            if (slots[index] is { Kind: not RuleKind.Default })
                continue; // a rule for the member wins

            var member = members[index];
            var values = new object?[feed.Count];
            for (var row = 0; row < values.Length; row++)
            {
                try
                {
                    values[row] = FeedValues.Convert(feed.Value(row, column), member.Type);
                }
                catch (FormatException e)
                {
                    throw new InvalidOperationException(
                        $"Feed row {row + 1}, column '{feed.Columns[column]}' (value '{feed.Value(row, column)}') can't be used for {typeof(T).Name}.{member.Name} ({member.Type.Name}): {e.Message}.", e);
                }
            }

            var rowOf = RowSelector(settings.Order, values.Length);
            slots[index] = new RuleSlot(RuleKind.Convention, (Func<FakeScope, object?>)(scope => values[rowOf(scope.Index)]), slots[index]?.Unique == true, slots[index]?.MaxAttempts ?? 0);
        }

        if (unknown.Count > 0 && !settings.IgnoreUnknownColumns)
        {
            throw new InvalidOperationException(
                $"The feed has columns that {typeof(T).Name} has no member for: {string.Join(", ", unknown.Select(c => $"'{c}'"))}. Its members are {string.Join(", ", members.Select(m => m.Name))}. "
                + "Rename the columns (case, spaces and underscores don't matter), or pass ignoreUnknownColumns: true to skip them.");
        }
    }

    /// <summary>The row an object takes: its position modulo the number of rows, or a hash of the generator's seed and the position.</summary>
    private Func<long, int> RowSelector(FeedOrder order, int rows)
    {
        if (order == FeedOrder.InOrder)
            return index => (int)(index % rows);

        var seed = unchecked(BaseSeed ^ 0x5EEDFEEDL);
        return index => (int)((ulong)Randomizer.DeriveSeed(seed, index) % (ulong)rows);
    }

    /// <summary>Registers the graph-dependent rules this generator was given (in a stable order) so their use can be recorded; path rules register while their nodes are built.</summary>
    private RuleUsage RegisterUsage()
    {
        var usage = new RuleUsage();
        foreach (var type in _config.Subtypes.Keys.OrderBy(t => t.FullName, StringComparer.Ordinal))
            usage.AddSubtype(type);
        foreach (var type in _config.TypeGenerators.Keys.OrderBy(t => t.FullName, StringComparer.Ordinal))
            usage.AddGenerator(type);
        for (var i = 0; i < _config.Conventions.Count; i++)
        {
            var convention = _config.Conventions[i];
            usage.AddConvention(convention, $"{convention.Source}<{convention.Type.Name}>" + (_config.Conventions.Count > 1 ? $" (number {i + 1} of {_config.Conventions.Count})" : ""));
        }

        return usage;
    }

    /// <summary>One generation run: a faker reused for every item, reseeded per item for order-independent results.</summary>
    private sealed class Run(Generator<T> generator, Compiled compiled, Faker faker, long seed)
    {
        /// <summary>Creates item <paramref name="index"/>; its seed depends only on the base seed and the index (and the attempt, when a condition rejects an object).</summary>
        public T Create(long index, FakeScope? parent = null)
        {
            var filters = generator._config.Filters;
            var itemSeed = Randomizer.DeriveSeed(seed, index);
            if (filters.IsEmpty)
                return CreateOnce(index, parent, itemSeed);

            var limit = Filter.AttemptLimit(filters);
            for (var attempt = 0; attempt < limit; attempt++)
            {
                var item = CreateOnce(index, parent, Filter.AttemptSeed(itemSeed, attempt));
                if (Filter.Accepts(filters, item))
                    return item;
            }

            throw Filter.Exhausted(typeof(T), filters, limit);
        }

        /// <summary>Populates <paramref name="instance"/> as item <paramref name="index"/> of the sequence.</summary>
        public T Populate(T instance, long index, PopulateMode mode)
        {
            var filters = generator._config.Filters;
            var itemSeed = Randomizer.DeriveSeed(seed, index);
            if (filters.IsEmpty)
                return PopulateOnce(instance, index, mode, itemSeed);

            if (mode != PopulateMode.All)
            {
                throw new InvalidOperationException(
                    $"A Where condition can't be combined with PopulateMode.{mode}: the values {typeof(T).Name} keeps can't be generated again when the condition rejects the object. Use PopulateMode.All, or Generate().");
            }

            var limit = Filter.AttemptLimit(filters);
            var item = instance;
            for (var attempt = 0; attempt < limit; attempt++)
            {
                item = PopulateOnce(item, index, mode, Filter.AttemptSeed(itemSeed, attempt));
                if (Filter.Accepts(filters, item))
                    return item;
            }

            throw Filter.Exhausted(typeof(T), filters, limit);
        }

        private T CreateOnce(long index, FakeScope? parent, long itemSeed)
        {
            faker.Random.Reseed(itemSeed);
            var config = generator._config;
            var model = generator._model;
            var scope = new FakeScope(faker, compiled.Settings, compiled.Slots, index, typeof(T), parent, compiled.RootChildren);

            T item;
            if (config.Factory is { } factory)
            {
                item = factory(faker);
                for (var i = 0; i < compiled.Slots.Length; i++)
                {
                    // Only explicit rules apply to factory-created objects; the factory decides everything else.
                    if (compiled.Slots[i] is { Kind: RuleKind.Constant or RuleKind.Factory or RuleKind.Generator or RuleKind.Convention or RuleKind.Constraint } && !model.Members[i].IsConstructorParameter)
                        item = model.Set(item, i, scope.GetObject(i, model.Fallback(i))); // only a constraint reads the member's own value
                }
            }
            else
            {
                item = model.Create(scope);
            }

            foreach (var (member, rule) in compiled.Dependent)
                item = model.Set(item, member, rule(faker, item));
            if (config.BackReferences && item is not null)
                Phoney.Generation.BackReferences.Apply(item);
            foreach (var action in config.AfterCreate)
                action(faker, item);
            return item;
        }

        private T PopulateOnce(T instance, long index, PopulateMode mode, long itemSeed)
        {
            faker.Random.Reseed(itemSeed);
            var config = generator._config;
            var model = generator._model;
            var scope = new FakeScope(faker, compiled.Settings, compiled.Slots, index, typeof(T), null, compiled.RootChildren);

            var item = model.Populate(instance, scope, mode);
            foreach (var (member, rule) in compiled.Dependent)
            {
                if (mode == PopulateMode.All || model.IsUnset(item, member))
                    item = model.Set(item, member, rule(faker, item));
            }

            if (config.BackReferences && item is not null)
                Phoney.Generation.BackReferences.Apply(item);
            foreach (var action in config.AfterCreate)
                action(faker, item);
            return item;
        }
    }

    /// <summary>The conventions whose value is read from the object's <see cref="Identity"/>, which is generated once per object.</summary>
    private static readonly HashSet<ConventionKind> IdentityBound =
    [
        ConventionKind.FirstName, ConventionKind.LastName, ConventionKind.Username, ConventionKind.Email, ConventionKind.StreetAddress, ConventionKind.City,
        ConventionKind.ZipCode, ConventionKind.State, ConventionKind.Country, ConventionKind.CountryCode, ConventionKind.NationalId, ConventionKind.Age,
        ConventionKind.BirthDate, ConventionKind.Gender, ConventionKind.Sex,
    ];

    /// <summary>The feed of <see cref="WithFeed"/> and how it is used.</summary>
    private sealed record FeedSettings(DataFeed Feed, FeedOrder Order, bool IgnoreUnknownColumns);

    /// <summary>A condition on one member from <c>Where(x =&gt; x.Member, …)</c> and how often the member may be drawn again for it.</summary>
    private sealed record MemberFilter(Func<object?, bool> Accepts, int MaxAttempts);

    /// <summary>A condition from <see cref="Where(Func{T, bool}, int)"/> and how often an object may be generated again for it.</summary>
    private sealed record Filter(Func<T, bool> Predicate, int MaxAttempts)
    {
        /// <summary>The smallest limit of the conditions: the object has to satisfy all of them, so the strictest one gives up first.</summary>
        public static int AttemptLimit(ImmutableList<Filter> filters) => filters.Min(f => f.MaxAttempts);

        /// <summary>The first attempt is the unfiltered object; later ones draw from a seed derived from it, so they depend on the seed, position and attempt only.</summary>
        public static long AttemptSeed(long itemSeed, int attempt) => attempt == 0 ? itemSeed : Randomizer.DeriveSeed(itemSeed, attempt);

        public static bool Accepts(ImmutableList<Filter> filters, T item)
        {
            foreach (var filter in filters)
            {
                if (!filter.Predicate(item))
                    return false;
            }

            return true;
        }

        public static InvalidOperationException Exhausted(Type type, ImmutableList<Filter> filters, int attempts) =>
            new($"Could not generate a {type.Name} that satisfies {(filters.Count == 1 ? "the Where condition" : "all Where conditions")} after {attempts} attempts. "
                + "Almost every generated object is rejected: set the members the condition looks at with .With(…) so that it can hold, loosen the condition, or raise maxAttempts.");
    }

    /// <summary>Rules bound to member indices, plus rules that run after creation.</summary>
    private sealed record Compiled(RuleSlot?[] Slots, (int Member, Func<Faker, T, object?> Rule)[] Dependent, GenerationSettings Settings, PathNode?[]? RootChildren, RuleUsage? Usage);

    /// <summary>A configured rule; <paramref name="Boxed"/> is the object-returning twin of a typed factory, <paramref name="Added"/> its position among the rules added, <paramref name="Positional"/> whether its value depends on the object's position (<c>Sequence</c>, <c>Cycle</c>).</summary>
    private sealed record Rule(RuleKind Kind, object? Value, Func<Faker, object?>? Boxed = null, int Added = 0, bool Positional = false);

    /// <summary>Mutable per-instance state; derived generators start fresh.</summary>
    private sealed class State
    {
        private long _next;

        /// <summary>Base seed used when no seed is configured.</summary>
#pragma warning disable RS0030 // Base seed of an unseeded generator; a configured seed replaces it.
        public long RandomSeed { get; } = System.Random.Shared.NextInt64();
#pragma warning restore RS0030

        private Compiled? _compiled;

        /// <summary>Rules compiled against the model on first use.</summary>
        public Compiled? Compiled => Volatile.Read(ref _compiled);

        /// <summary>Stores <paramref name="compiled"/> unless another thread compiled first, and returns the one every run shares (so uniqueness and rule usage are tracked in one place).</summary>
        public Compiled Publish(Compiled compiled) => Interlocked.CompareExchange(ref _compiled, compiled, null) ?? compiled;

        /// <summary>Reserves <paramref name="count"/> consecutive item indices.</summary>
        public long NextIndex(int count) => Interlocked.Add(ref _next, count) - count;
    }

    /// <summary>Immutable configuration; every builder method returns a copy.</summary>
    private sealed record Config(
        string? Locale,
        long? Seed,
        DateTimeOffset? ReferenceDate,
        TimeProvider? TimeProvider,
        ImmutableDictionary<string, Rule> Rules,
        int RuleCount,
        ImmutableDictionary<string, PathSpec> PathRules,
        ImmutableDictionary<string, int> Unique,
        ImmutableList<CustomConvention> Conventions,
        ImmutableDictionary<Type, ImmutableArray<Type>> Subtypes,
        ImmutableDictionary<Type, INestedGenerator> TypeGenerators,
        ImmutableList<Action<Faker, T>> AfterCreate,
        ImmutableList<Filter> Filters,
        ImmutableDictionary<string, MemberFilter> MemberFilters,
        Func<Faker, T>? Factory,
        bool Strict,
        bool Verify,
        bool BackReferences,
        FeedSettings? Feed,
        GenerationSettings Settings)
    {
        public static readonly Config Default = new(
            null, null, null, null,
            ImmutableDictionary.Create<string, Rule>(StringComparer.Ordinal),
            0,
            ImmutableDictionary.Create<string, PathSpec>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, int>(StringComparer.Ordinal),
            [],
            ImmutableDictionary<Type, ImmutableArray<Type>>.Empty,
            ImmutableDictionary<Type, INestedGenerator>.Empty,
            [],
            [],
            ImmutableDictionary.Create<string, MemberFilter>(StringComparer.Ordinal),
            null,
            false,
            false,
            false,
            null,
            GenerationSettings.Default);
    }

    /// <summary>Extracts <c>Name</c> from <c>x =&gt; x.Name</c> (also through conversions like <c>(object)x.Name</c>); for rules that exist per member of the generated type only.</summary>
    private static string MemberName<TValue>(Expression<Func<T, TValue>> expression)
    {
        var path = MemberPath(expression);
        if (path.Length != 1)
        {
            throw new ArgumentException(
                $"'{expression}' must select a member of {typeof(T).Name} directly, like x => x.Name: this setting exists per member of the generated type, not for members of nested objects.",
                nameof(expression));
        }

        return path[0];
    }

    /// <summary>
    /// The member names of a selector: <c>x =&gt; x.Name</c> is <c>[Name]</c>, <c>x =&gt; x.Billing.City</c> is <c>[Billing, City]</c>, and
    /// <c>x =&gt; x.Lines.Each().Sku</c> is <c>[Lines, Sku]</c> (<c>Each()</c> only marks the collection; <c>Dictionary.Values</c> and
    /// <c>Nullable.Value</c> are skipped the same way).
    /// </summary>
    private static string[] MemberPath<TValue>(Expression<Func<T, TValue>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var names = new List<string>();
        var node = expression.Body;
        while (node != expression.Parameters[0])
        {
            switch (node)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
                    node = convert.Operand;
                    break;
                case MemberExpression { Member: PropertyInfo or FieldInfo, Expression: { } owner } access:
                    if (!IsTransparent(access.Member.Name, owner.Type))
                        names.Add(access.Member.Name);
                    node = owner;
                    break;
                case MethodCallExpression { Method: { Name: nameof(FakePath.Each), DeclaringType: { } declaring }, Arguments: [var source] } when declaring == typeof(FakePath):
                    node = source;
                    break;
                default:
                    throw new ArgumentException(
                        $"'{expression}' must select a member of {typeof(T).Name} (x => x.Name), a member of a nested object (x => x.Address.City) or of every element of a collection (x => x.Lines.Each().Sku).",
                        nameof(expression));
            }
        }

        if (names.Count == 0)
            throw new ArgumentException($"'{expression}' must select a member of {typeof(T).Name}, like x => x.Name.", nameof(expression));
        names.Reverse();
        return [.. names];

        // The values of a dictionary are what Each() walks, and a nullable's value is the object itself.
        static bool IsTransparent(string member, Type owner) =>
            (member == "Values" && owner.IsGenericType && owner.GetGenericTypeDefinition() is var d && (d == typeof(Dictionary<,>) || d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)))
            || (member == "Value" && Nullable.GetUnderlyingType(owner) is not null);
    }
}
