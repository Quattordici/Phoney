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
        where TGenerated : TValue =>
        AddRule(member, new Rule(RuleKind.Generator, generator ?? throw new ArgumentNullException(nameof(generator))));

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

    /// <summary>Creates instances with <paramref name="factory"/> instead of a constructor; rules are applied afterwards, conventions are not.</summary>
    public Generator<T> CreateWith(Func<Faker, T> factory) =>
        Derive(_config with { Factory = factory ?? throw new ArgumentNullException(nameof(factory)) });

    /// <summary>Runs <paramref name="action"/> on every generated object, after all members are set.</summary>
    public Generator<T> AfterCreate(Action<Faker, T> action) =>
        Derive(_config with { AfterCreate = _config.AfterCreate.Add(action ?? throw new ArgumentNullException(nameof(action))) });

    /// <summary>Generates one object.</summary>
    public T Generate()
    {
        var run = Start();
        return run.Create(_state.NextIndex(1));
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
        return list;
    }

    /// <summary>Generates objects lazily and endlessly; take what you need: <c>gen.Stream().Take(10)</c>.</summary>
    public IEnumerable<T> Stream()
    {
        var run = Start();
        while (true)
            yield return run.Create(_state.NextIndex(1));
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
        return run.Populate(instance, _state.NextIndex(1), mode);
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

        return result;
    }

    /// <inheritdoc />
    Type INestedGenerator.GeneratedType => typeof(T);

    /// <inheritdoc />
    object? INestedGenerator.CreateNested(FakeScope parent) =>
        Start(parent.Faker.Random.Long(), parent.Faker).Create(0, parent);

    /// <summary>The base seed: the configured one, or one random seed per generator instance.</summary>
    private long BaseSeed => _config.Seed ?? _state.RandomSeed;

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
                var generator = Derive(_config with { Seed = seed, Rules = _config.Rules.SetItem(member.Name, rule), RuleCount = _config.RuleCount + 1 });
                cases.Add(new ConstraintCase<T>(member.Name, descriptions[i].Description, generator.Generate()));
            }
        }

        return cases;
    }

    /// <summary>Picks the producer of a <see cref="ConstraintRequest"/>: the requested case, or one per object when none was requested.</summary>
    private static Func<FakeScope, Func<FakeScope, object?>, object?> ResolveConstraint(FakeMember member, ConstraintRequest request)
    {
        var cases = request.Boundary ? ConstraintCases.Boundary(member) : ConstraintCases.Invalid(member);
        if (cases.Count == 0)
        {
            throw new InvalidOperationException(request.Boundary
                ? $"{typeof(T).Name}.{member.Name} has no limits to sit on: add [Range], [StringLength], [MinLength] or [MaxLength] to it."
                : $"{typeof(T).Name}.{member.Name} has no data annotations that can be broken: add [Range], [StringLength], [MinLength], [MaxLength], [Required], [RegularExpression], [EmailAddress], [AllowedValues] or [DeniedValues] to it.");
        }

        if (request.Index is { } index)
            return cases[index].Produce;
        return cases.Count == 1 ? cases[0].Produce : (scope, valid) => cases[scope.Faker.Random.Index(cases.Count)].Produce(scope, valid);
    }

    /// <summary>Adds (or replaces) the rule of a member; the rule remembers when it was added, because dictionaries don't keep that order.</summary>
    private Generator<T> AddRule<TValue>(Expression<Func<T, TValue>> member, Rule rule) =>
        Derive(_config with { Rules = _config.Rules.SetItem(MemberName(member), rule with { Added = _config.RuleCount }), RuleCount = _config.RuleCount + 1 });

    /// <summary>Prepares a run: compiled rules, a faker for the locale and the base seed. Nested generators inherit the parent's locale and reference date.</summary>
    private Run Start(long? seed = null, Faker? parentFaker = null)
    {
        var compiled = _state.Compiled ??= Compile();
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

            var value = rule.Kind == RuleKind.Constraint ? ResolveConstraint(members[index], (ConstraintRequest)rule.Value!) : rule.Value;
            slots[index] = new RuleSlot(rule.Kind, value, _config.Unique.TryGetValue(name, out var attempts), attempts, rule.Boxed);
        }

        // Unique members without an explicit rule keep their convention value but still need a slot.
        foreach (var (name, attempts) in _config.Unique.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            if (!known.TryGetValue(name, out var index))
                throw new InvalidOperationException($"{typeof(T).Name}.{name} is not a generated member.");
            slots[index] ??= new RuleSlot(RuleKind.Default, null, unique: true, attempts);
        }

        // Custom conventions (per generator, then global) fill members that have no rule.
        var conventions = _config.Conventions.AddRange(FakeConventions.All);
        for (var i = 0; i < members.Count; i++)
        {
            if (slots[i] is { Kind: not RuleKind.Default })
                continue; // an explicit rule wins over conventions
            var match = conventions.FirstOrDefault(c => c.Matches(members[i]));
            if (match is not null)
                slots[i] = new RuleSlot(RuleKind.Convention, match.Factory, slots[i]?.Unique == true, slots[i]?.MaxAttempts ?? 0);
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

        // Subtypes and custom conventions apply to the whole object graph, so they travel with the settings into nested scopes.
        var settings = _config.Settings with
        {
            Subtypes = FakeSubtypes.All.SetItems(_config.Subtypes),
            Conventions = NestedConventions.For(conventions),
            TypeGenerators = MergedTypeGenerators(_config.TypeGenerators),
        };
        return new Compiled(slots, [.. dependent], settings);
    }

    /// <summary>One generation run: a faker reused for every item, reseeded per item for order-independent results.</summary>
    private sealed class Run(Generator<T> generator, Compiled compiled, Faker faker, long seed)
    {
        /// <summary>Creates item <paramref name="index"/>; its seed depends only on the base seed and the index.</summary>
        public T Create(long index, FakeScope? parent = null)
        {
            faker.Random.Reseed(Randomizer.DeriveSeed(seed, index));
            var config = generator._config;
            var model = generator._model;
            var scope = new FakeScope(faker, compiled.Settings, compiled.Slots, index, typeof(T), parent);

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
            foreach (var action in config.AfterCreate)
                action(faker, item);
            return item;
        }

        /// <summary>Populates <paramref name="instance"/> as item <paramref name="index"/> of the sequence.</summary>
        public T Populate(T instance, long index, PopulateMode mode)
        {
            faker.Random.Reseed(Randomizer.DeriveSeed(seed, index));
            var config = generator._config;
            var model = generator._model;
            var scope = new FakeScope(faker, compiled.Settings, compiled.Slots, index, typeof(T));

            var item = model.Populate(instance, scope, mode);
            foreach (var (member, rule) in compiled.Dependent)
            {
                if (mode == PopulateMode.All || model.IsUnset(item, member))
                    item = model.Set(item, member, rule(faker, item));
            }

            foreach (var action in config.AfterCreate)
                action(faker, item);
            return item;
        }
    }

    /// <summary>Rules bound to member indices, plus rules that run after creation.</summary>
    private sealed record Compiled(RuleSlot?[] Slots, (int Member, Func<Faker, T, object?> Rule)[] Dependent, GenerationSettings Settings);

    /// <summary>What <see cref="Invalid{TValue}"/> and <see cref="Boundary{TValue}"/> ask for: <paramref name="Index"/> picks one case of the member, <see langword="null"/> lets each object pick.</summary>
    private sealed record ConstraintRequest(bool Boundary, int? Index);

    /// <summary>A configured rule; <paramref name="Boxed"/> is the object-returning twin of a typed factory, <paramref name="Added"/> its position among the rules added.</summary>
    private sealed record Rule(RuleKind Kind, object? Value, Func<Faker, object?>? Boxed = null, int Added = 0);

    /// <summary>Mutable per-instance state; derived generators start fresh.</summary>
    private sealed class State
    {
        private long _next;

        /// <summary>Base seed used when no seed is configured.</summary>
#pragma warning disable RS0030 // Base seed of an unseeded generator; a configured seed replaces it.
        public long RandomSeed { get; } = System.Random.Shared.NextInt64();
#pragma warning restore RS0030

        /// <summary>Rules compiled against the model on first use.</summary>
        public Compiled? Compiled { get; set; }

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
        ImmutableDictionary<string, int> Unique,
        ImmutableList<CustomConvention> Conventions,
        ImmutableDictionary<Type, ImmutableArray<Type>> Subtypes,
        ImmutableDictionary<Type, INestedGenerator> TypeGenerators,
        ImmutableList<Action<Faker, T>> AfterCreate,
        Func<Faker, T>? Factory,
        bool Strict,
        GenerationSettings Settings)
    {
        public static readonly Config Default = new(
            null, null, null, null,
            ImmutableDictionary.Create<string, Rule>(StringComparer.Ordinal),
            0,
            ImmutableDictionary.Create<string, int>(StringComparer.Ordinal),
            [],
            ImmutableDictionary<Type, ImmutableArray<Type>>.Empty,
            ImmutableDictionary<Type, INestedGenerator>.Empty,
            [],
            null,
            false,
            GenerationSettings.Default);
    }

    /// <summary>Extracts <c>Name</c> from <c>x =&gt; x.Name</c> (also through conversions like <c>(object)x.Name</c>).</summary>
    private static string MemberName<TValue>(Expression<Func<T, TValue>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = expression.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert ? convert.Operand : expression.Body;
        if (body is MemberExpression { Member: PropertyInfo or FieldInfo } access && access.Expression == expression.Parameters[0])
            return access.Member.Name;
        throw new ArgumentException(
            $"'{expression}' must select a member of {typeof(T).Name} directly, like x => x.Name. For nested members use .With(x => x.Address, Fake.For<Address>().With(...)).",
            nameof(expression));
    }
}
