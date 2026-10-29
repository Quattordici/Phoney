using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using Phoney.Data;
using Phoney.Generation;
using Phoney.Modules;

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
/// <para>Order of population: constructor arguments, then members (base class first; within a class fields, then properties, each in declaration order; read-only collections last), then rules that depend
/// on the object (<c>With(x =&gt; x.Email, (f, x) =&gt; …)</c>) in the order they were added, then <see cref="AfterCreate"/> actions.</para>
/// </remarks>
/// <typeparam name="T">The type to generate.</typeparam>
public sealed class ObjectGenerator<T> : INestedGenerator
{
    private readonly FakeModel<T> _model;

    /// <summary>The model the generator runs against; <see cref="DataExport"/> reads the members of the objects through it.</summary>
    internal FakeModel<T> Model => _model;
    private readonly Config _config;
    private readonly State _state;

    /// <summary>Creates a generator over an explicit model (source-generated models use this constructor).</summary>
    public ObjectGenerator(FakeModel<T> model) : this(model ?? throw new ArgumentNullException(nameof(model)), Config.Default)
    {
    }

    private ObjectGenerator(FakeModel<T> model, Config config, State? state = null)
    {
        _model = model;
        _config = config;
        _state = state ?? new State(null);
    }

    /// <summary>Whether the compiled rules hold nothing of one generator instance (unique sets, rule usage, a seed-dependent feed order), so copies that only change run settings can share them.</summary>
    private bool CompiledIsShareable => _config.Unique.IsEmpty && _config.UniqueKeys.IsEmpty && !_config.Verify && _config.Feed is null;

    /// <summary>A copy with fresh per-instance state (positions, random base seed) that reuses the rules this generator already compiled.</summary>
    internal ObjectGenerator<T> Fresh() => new(_model, _config, new State(CompiledIsShareable ? _state : null));

    /// <summary>Like <see cref="Derive"/> for settings the compiled rules don't depend on (seed, locale, dates).</summary>
    private ObjectGenerator<T> DeriveRun(Config config) => new(_model, config, new State(CompiledIsShareable ? _state : null));

    /// <summary>Uses <paramref name="locale"/> instead of <see cref="Fake.Locale"/>.</summary>
    public ObjectGenerator<T> Locale(string locale) => DeriveRun(_config with { Locale = Locales.Get(locale).Code });

    /// <summary>
    /// Makes generation reproducible: the same seed always yields the same sequence of objects. Relative dates use
    /// <see cref="Faker.DefaultSeededReferenceDate"/> unless <see cref="ReferenceDate"/> is set, so they are stable over time.
    /// </summary>
    public ObjectGenerator<T> Seed(long seed) => DeriveRun(_config with { Seed = seed });

    /// <summary>Sets the point in time relative dates are computed from (default: now, or a fixed date when seeded).</summary>
    public ObjectGenerator<T> ReferenceDate(DateTimeOffset referenceDate) => DeriveRun(_config with { Clock = _config.Clock with { Date = referenceDate } });

    /// <summary>
    /// Reads "now" for relative dates from <paramref name="timeProvider"/> (e.g. a <c>FakeTimeProvider</c>), also when
    /// seeded. An explicit <see cref="ReferenceDate"/> takes precedence.
    /// </summary>
    public ObjectGenerator<T> TimeProvider(TimeProvider timeProvider) =>
        DeriveRun(_config with { Clock = _config.Clock with { Clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)) } });

    /// <summary>Sets a member to a fixed value.</summary>
    public ObjectGenerator<T> With<TValue>(Expression<Func<T, TValue>> member, TValue value) =>
        AddRule(member, new Rule(RuleKind.Constant, value));

    /// <summary>Generates a member with <paramref name="factory"/>, e.g. <c>.With(x =&gt; x.Age, f =&gt; f.Number.Int(18, 65))</c>.</summary>
    public ObjectGenerator<T> With<TValue>(Expression<Func<T, TValue>> member, Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddRule(member, new Rule(RuleKind.Factory, factory, f => factory(f)));
    }

    /// <summary>
    /// Generates a member from the object after its other members are set, e.g.
    /// <c>.With(x =&gt; x.Email, (f, x) =&gt; f.Internet.Email(x.FirstName, x.LastName))</c>.
    /// </summary>
    public ObjectGenerator<T> With<TValue>(Expression<Func<T, TValue>> member, Func<Faker, T, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddRule(member, new Rule(RuleKind.Dependent, (Func<Faker, T, object?>)((f, x) => factory(f, x))));
    }

    /// <summary>
    /// Generates a nested member with its own generator, e.g. <c>.With(x =&gt; x.Address, Fake.For&lt;Address&gt;().Locale("sv"))</c>;
    /// the generator may produce a subtype of the member's type: <c>.With(x =&gt; x.Shape, Fake.For&lt;Circle&gt;())</c>.
    /// </summary>
    public ObjectGenerator<T> With<TValue, TGenerated>(Expression<Func<T, TValue>> member, ObjectGenerator<TGenerated> generator)
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
    public ObjectGenerator<T> Subtype<TBase, T1>()
        where T1 : TBase => Subtype<TBase>(typeof(T1));

    /// <summary>Generates one of the subtypes, picked at random, for every member of type <typeparamref name="TBase"/>: <c>.Subtype&lt;IShape, Circle, Square&gt;()</c>.</summary>
    public ObjectGenerator<T> Subtype<TBase, T1, T2>()
        where T1 : TBase
        where T2 : TBase => Subtype<TBase>(typeof(T1), typeof(T2));

#pragma warning disable S2436 // each type parameter is one more subtype, checked against TBase at compile time; params Type[] can't check that
    /// <summary>Generates one of the subtypes, picked at random, for every member of type <typeparamref name="TBase"/>.</summary>
    public ObjectGenerator<T> Subtype<TBase, T1, T2, T3>()
        where T1 : TBase
        where T2 : TBase
        where T3 : TBase => Subtype<TBase>(typeof(T1), typeof(T2), typeof(T3));

    /// <summary>Generates one of the subtypes, picked at random, for every member of type <typeparamref name="TBase"/>.</summary>
    public ObjectGenerator<T> Subtype<TBase, T1, T2, T3, T4>()
        where T1 : TBase
        where T2 : TBase
        where T3 : TBase
        where T4 : TBase => Subtype<TBase>(typeof(T1), typeof(T2), typeof(T3), typeof(T4));
#pragma warning restore S2436

    /// <summary>Generates one of <paramref name="subtypes"/>, picked at random in the given order, for every member of type <typeparamref name="TBase"/>.</summary>
    /// <exception cref="ArgumentException">A subtype is abstract, an interface, open generic or not assignable to <typeparamref name="TBase"/>.</exception>
    public ObjectGenerator<T> Subtype<TBase>(params Type[] subtypes) =>
        Derive(_config with { Subtypes = _config.Subtypes.SetItem(typeof(TBase), Subtypes.Validate(typeof(TBase), subtypes)) });

    /// <summary>
    /// Creates every nested <typeparamref name="TNested"/> in the generated objects with <paramref name="generator"/>: members,
    /// collection elements and dictionary values, at any depth, e.g. <c>.WithGeneratorFor(Fake.For&lt;Address&gt;().Locale("de"))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule for the member (<c>With(x =&gt; x.Billing, …)</c>) wins over the type's generator, which wins over
    /// <see cref="Subtype{TBase, T1}"/>, <c>Fake.Subtypes</c> and the type's model. The generator is self-contained: it brings its
    /// own rules, locale and settings. A generator used by an inner <see cref="WithGeneratorFor{TNested}"/> wins over this one, while types
    /// it does not register are still created by the generators registered here. A later registration for the same type replaces an earlier one.
    /// </para>
    /// <para>
    /// The generated object itself is never replaced, depth limits and cycles still stop nesting with <see langword="null"/>, and objects you
    /// pass to <c>Populate</c> are filled member by member: the generator creates the nested objects that are missing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><typeparamref name="TNested"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public ObjectGenerator<T> WithGeneratorFor<TNested>(ObjectGenerator<TNested> generator) => WithGeneratorFor<TNested, TNested>(generator);

    /// <summary>
    /// Creates every nested <typeparamref name="TBase"/> (an interface, abstract class or base class) with the generator of a
    /// subtype, e.g. <c>.Use&lt;IShape, Circle&gt;(Fake.For&lt;Circle&gt;().With(c =&gt; c.Radius, 2))</c>.
    /// </summary>
    /// <remarks>See <see cref="WithGeneratorFor{TNested}"/> for precedence and scope.</remarks>
    /// <exception cref="ArgumentException"><typeparamref name="TBase"/> is a scalar (string, number, date, enum…) or an array.</exception>
    public ObjectGenerator<T> WithGeneratorFor<TBase, TGenerated>(ObjectGenerator<TGenerated> generator)
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
    public ObjectGenerator<T> Invalid<TValue>(Expression<Func<T, TValue>> member) =>
        AddRule(member, new Rule(RuleKind.Constraint, new ConstraintRequest(Boundary: false, null)));

    /// <summary>
    /// Sets a member exactly on a limit of its data annotations, e.g. <c>.Boundary(x =&gt; x.Quantity)</c> gives the smallest or the
    /// largest value its <c>[Range]</c> allows, a name of the minimum or maximum length, or the minimum or maximum element count.
    /// The value is valid; which edge an object gets is picked per object. See <see cref="Invalid{TValue}"/> for the annotations that are read.
    /// </summary>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: the member has no limits to sit on (patterns, formats and lists of allowed values define a value but have no edge).</exception>
    public ObjectGenerator<T> Boundary<TValue>(Expression<Func<T, TValue>> member) =>
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
    public ObjectGenerator<T> WithOneOf<TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException($"WithOneOf({member}) needs at least one value to pick from.", nameof(values));
        return With(member, f => f.Random.Element(values));
    }

    /// <summary>
    /// Sets a member to one of the values, some more often than others: <c>.WithOneOf(o =&gt; o.Status, (Status.Paid, 70), (Status.Pending, 25), (Status.Failed, 5))</c>
    /// gives 70 % paid, 25 % pending and 5 % failed.
    /// </summary>
    /// <remarks>
    /// The weights are relative (70, 25 and 5 mean the same as 14, 5 and 1), a weight of zero is never picked, and a value can only be missing from the choices by leaving it out. Each
    /// object makes exactly one random draw for the member, whatever the weights are, and the choices are checked and copied when the rule is added.
    /// </remarks>
    /// <param name="member">The member to set: <c>x =&gt; x.Status</c> (also a member of a nested object or a collection element).</param>
    /// <param name="choices">The values with their weights. A weight is not negative and finite; at least one is greater than zero.</param>
    /// <exception cref="ArgumentException">There are no choices, a weight is negative, not a number or infinite, or no weight is greater than zero.</exception>
    public ObjectGenerator<T> WithOneOf<TValue>(Expression<Func<T, TValue>> member, params (TValue Value, double Weight)[] choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var picker = WeightedPicker<TValue>.Create(choices.Select(c => c.Value), choices.Select(c => c.Weight), nameof(choices));
        return With(member, f => picker.Pick(f.Random));
    }

    /// <summary>
    /// Sets a member to one of <paramref name="values"/>, the one at the same position in <paramref name="weights"/> deciding how often:
    /// <c>.WithOneOf(o =&gt; o.Country, countries, populations)</c>.
    /// </summary>
    /// <param name="member">The member to set: <c>x =&gt; x.Country</c>.</param>
    /// <param name="values">The values to choose from.</param>
    /// <param name="weights">One weight per value (relative; zero is never picked; at least one is greater than zero).</param>
    /// <exception cref="ArgumentException">There are no values, the lists differ in length, a weight is negative, not a number or infinite, or no weight is greater than zero.</exception>
    public ObjectGenerator<T> WithOneOf<TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TValue> values, IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(weights);
        if (values.Count != weights.Count)
            throw new ArgumentException($"Provide one weight per value: {values.Count} values and {weights.Count} weights.", nameof(weights));
        var picker = WeightedPicker<TValue>.Create(values, weights, nameof(values));
        return With(member, f => picker.Pick(f.Random));
    }

    /// <summary>
    /// Sets a member from a random item of <paramref name="source"/>, the items with a larger <paramref name="weight"/> more often:
    /// <c>.WithOneOf(o =&gt; o.CustomerId, customers, c =&gt; c.Id, c =&gt; c.OrderCount)</c>. The weights are read once, when the rule is added.
    /// </summary>
    /// <param name="member">The member to set: <c>x =&gt; x.CustomerId</c>.</param>
    /// <param name="source">The items to choose from.</param>
    /// <param name="select">What the member gets from the chosen item.</param>
    /// <param name="weight">How much an item counts (relative; zero is never picked; at least one is greater than zero).</param>
    /// <exception cref="ArgumentException">There are no items, a weight is negative, not a number or infinite, or no weight is greater than zero.</exception>
    public ObjectGenerator<T> WithOneOf<TSource, TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TSource> source, Func<TSource, TValue> select, Func<TSource, double> weight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(weight);
        var picker = WeightedPicker<TSource>.Create(source, source.Select(weight), nameof(source));
        return With(member, f => select(picker.Pick(f.Random)));
    }

    /// <summary>
    /// Sets a member from a random item of <paramref name="source"/>, so object graphs line up:
    /// <c>.WithOneOf(o =&gt; o.CustomerId, customers, c =&gt; c.Id)</c>.
    /// </summary>
    public ObjectGenerator<T> WithOneOf<TSource, TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TSource> source, Func<TSource, TValue> select)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(select);
        if (source.Count == 0)
            throw new ArgumentException($"WithOneOf({member}) needs at least one item to pick from.", nameof(source));
        return With(member, f => select(f.Random.Element(source)));
    }

    /// <summary>
    /// Gives a collection member a random subset of <paramref name="values"/>, a different one for every object:
    /// <c>.WithSubsetOf(u =&gt; u.Roles, roles, 2)</c> gives each user two of the roles, <c>.WithSubsetOf(u =&gt; u.Roles, roles, (1, 3))</c> one to three.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values of a subset are distinct positions of the list (a list with equal values can give equal values), returned in the order of the list, and every value is equally likely to be in
    /// it. It is taken from the object's own seeded randomness, so a seed gives the same subsets, item <em>n</em> is the same however you generate, and the two models agree. A subset of a size range
    /// draws its size first, then one number per value. The list is copied when the rule is added.
    /// </para>
    /// <para>
    /// This overload is for members of type <c>List&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c> and
    /// <c>IEnumerable&lt;T&gt;</c> (a <c>List&lt;T&gt;</c> is made); there are overloads for arrays and sets. Other collection types are refused when the rule is added.
    /// </para>
    /// </remarks>
    /// <param name="member">The collection member: <c>x =&gt; x.Roles</c> (also in a nested object or on every element: <c>x =&gt; x.Teams.Each().Members</c>).</param>
    /// <param name="values">The values to take a subset of.</param>
    /// <param name="size">How many values: a number (<c>3</c>) or an inclusive range (<c>(1, 3)</c>).</param>
    /// <exception cref="ArgumentException">The size is larger than the list, or the member's type cannot hold a list.</exception>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, IEnumerable<TValue>>> member, IReadOnlyList<TValue> values, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, size);
        RequireMemberAccepts(member, typeof(List<TValue>));
        return With(member, f => (IEnumerable<TValue>)picker.List(f.Random));
    }

    /// <summary>Gives an array member a random subset of <paramref name="values"/>; see <see cref="WithSubsetOf{TValue}(Expression{Func{T, IEnumerable{TValue}}}, IReadOnlyList{TValue}, SubsetSize)"/>.</summary>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, TValue[]>> member, IReadOnlyList<TValue> values, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, size);
        return With(member, f => picker.Array(f.Random));
    }

    /// <summary>Gives a <see cref="HashSet{T}"/> member a random subset of <paramref name="values"/>; see <see cref="WithSubsetOf{TValue}(Expression{Func{T, IEnumerable{TValue}}}, IReadOnlyList{TValue}, SubsetSize)"/>.</summary>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, HashSet<TValue>>> member, IReadOnlyList<TValue> values, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, size);
        return With(member, f => picker.Set(f.Random));
    }

    /// <summary>Gives an <see cref="ISet{T}"/> member a random subset of <paramref name="values"/> (a <see cref="HashSet{T}"/>); see <see cref="WithSubsetOf{TValue}(Expression{Func{T, IEnumerable{TValue}}}, IReadOnlyList{TValue}, SubsetSize)"/>.</summary>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, ISet<TValue>>> member, IReadOnlyList<TValue> values, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, size);
        return With(member, f => (ISet<TValue>)picker.Set(f.Random));
    }

    /// <summary>
    /// Gives a collection member a random subset of <paramref name="values"/> in which heavier values are more likely to be included:
    /// <c>.WithSubsetOf(u =&gt; u.Roles, roles, weights, 2)</c> gives each user two roles, the common ones more often than the rare ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values are taken one at a time, each time by weight among those not taken yet, which is how the chance of being in the subset follows the weights: a value that is twice as
    /// heavy as another is more than twice as likely to be taken first, and the more are taken the more the lighter ones get their turn. A subset of one is a plain weighted pick, and a subset of
    /// all the values that can be taken is those values. A weight of zero is never included. Everything else is as for the subset without weights: the values are in the order of the list, one
    /// random number is drawn per value (after the size, for a size range), a seed gives the same subsets, and the lists are copied when the rule is added.
    /// </para>
    /// <para>Every value costs a pass over the list, so a subset of 5 from a list of 100 000 is slower than without weights; for lists of up to a few thousand values it is not noticeable.</para>
    /// </remarks>
    /// <param name="member">The collection member: <c>x =&gt; x.Roles</c>.</param>
    /// <param name="values">The values to take a subset of.</param>
    /// <param name="weights">One weight per value (relative; zero is never included).</param>
    /// <param name="size">How many values: a number (<c>3</c>) or an inclusive range (<c>(1, 3)</c>).</param>
    /// <exception cref="ArgumentException">The lists differ in length, a weight is negative, not a number or infinite, the size is larger than the number of values with a weight above zero, or the member's type cannot hold a list.</exception>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, IEnumerable<TValue>>> member, IReadOnlyList<TValue> values, IReadOnlyList<double> weights, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, weights, size);
        RequireMemberAccepts(member, typeof(List<TValue>));
        return With(member, f => (IEnumerable<TValue>)picker.List(f.Random));
    }

    /// <summary>Gives an array member a weighted random subset of <paramref name="values"/>; see <see cref="WithSubsetOf{TValue}(Expression{Func{T, IEnumerable{TValue}}}, IReadOnlyList{TValue}, IReadOnlyList{double}, SubsetSize)"/>.</summary>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, TValue[]>> member, IReadOnlyList<TValue> values, IReadOnlyList<double> weights, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, weights, size);
        return With(member, f => picker.Array(f.Random));
    }

    /// <summary>Gives a <see cref="HashSet{T}"/> member a weighted random subset of <paramref name="values"/>; see <see cref="WithSubsetOf{TValue}(Expression{Func{T, IEnumerable{TValue}}}, IReadOnlyList{TValue}, IReadOnlyList{double}, SubsetSize)"/>.</summary>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, HashSet<TValue>>> member, IReadOnlyList<TValue> values, IReadOnlyList<double> weights, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, weights, size);
        return With(member, f => picker.Set(f.Random));
    }

    /// <summary>Gives an <see cref="ISet{T}"/> member a weighted random subset of <paramref name="values"/> (a <see cref="HashSet{T}"/>); see <see cref="WithSubsetOf{TValue}(Expression{Func{T, IEnumerable{TValue}}}, IReadOnlyList{TValue}, IReadOnlyList{double}, SubsetSize)"/>.</summary>
    public ObjectGenerator<T> WithSubsetOf<TValue>(Expression<Func<T, ISet<TValue>>> member, IReadOnlyList<TValue> values, IReadOnlyList<double> weights, SubsetSize size)
    {
        var picker = SubsetPicker<TValue>.Create(values, weights, size);
        return With(member, f => (ISet<TValue>)picker.Set(f.Random));
    }

    /// <summary>Leaves a member at its default value.</summary>
    public ObjectGenerator<T> Ignore<TValue>(Expression<Func<T, TValue>> member) => AddRule(member, new Rule(RuleKind.Ignore, null));

    /// <summary>
    /// Makes a member unique across everything this generator instance produces. Retries up to
    /// <paramref name="maxAttempts"/> times per object, then throws.
    /// </summary>
    /// <remarks>
    /// Select several members with an anonymous type to make their combination unique while each may repeat, as in a join table or a per-tenant key:
    /// <c>.Unique(x =&gt; new { x.UserId, x.RoleId })</c>. The combination is read from the finished object (after rules that depend on the object),
    /// and an object whose combination was produced before is generated again as a whole, so objects without a duplicate are the ones you get without the rule.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is less than 1.</exception>
    /// <exception cref="ArgumentException"><paramref name="member"/> doesn't select a member, or an anonymous type of members, of <typeparamref name="T"/>.</exception>
    public ObjectGenerator<T> Unique<TValue>(Expression<Func<T, TValue>> member, int maxAttempts = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentNullException.ThrowIfNull(member);
        if (member.Body is NewExpression { Members: not null } combination)
        {
            var names = CombinationMembers(member, combination);
            if (names.Length > 1)
                return Derive(_config with { UniqueKeys = _config.UniqueKeys.SetItem(string.Join(",", names), maxAttempts) });
            return Derive(_config with { Unique = _config.Unique.SetItem(names[0], maxAttempts) });
        }

        var name = MemberName(member);
        return Derive(_config with { Unique = _config.Unique.SetItem(name, maxAttempts) });
    }

    /// <summary>The member names of <c>x =&gt; new { x.A, x.B }</c>, in order and without repeats.</summary>
    private static string[] CombinationMembers<TValue>(Expression<Func<T, TValue>> expression, NewExpression combination)
    {
        var names = new List<string>(combination.Arguments.Count);
        foreach (var argument in combination.Arguments)
        {
            var node = argument;
            while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
                node = convert.Operand;
            if (node is not MemberExpression { Member: PropertyInfo or FieldInfo } access || access.Expression != expression.Parameters[0])
            {
                throw new ArgumentException(
                    $"'{expression}' must select members of {typeof(T).Name} directly, like x => new {{ x.UserId, x.RoleId }}: '{argument}' is not one.", nameof(expression));
            }

            if (!names.Contains(access.Member.Name))
                names.Add(access.Member.Name);
        }

        return names.Count > 0 ? [.. names] : throw new ArgumentException($"'{expression}' must select at least one member of {typeof(T).Name}.", nameof(expression));
    }

    /// <summary>
    /// Applies <paramref name="factory"/> to every member of type <typeparamref name="TValue"/> whose name matches
    /// <paramref name="nameMatches"/> and has no explicit rule, also in nested objects (before app-wide <see cref="Fake.Conventions"/>), e.g. <c>.WithConvention&lt;string&gt;(n =&gt; n.EndsWith("Code"), f =&gt; f.Random.ReplaceSymbols("??-###"))</c>.
    /// </summary>
    public ObjectGenerator<T> WithConvention<TValue>(Func<string, bool> nameMatches, Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(nameMatches);
        ArgumentNullException.ThrowIfNull(factory);
        var convention = new CustomConvention(typeof(TValue), nameMatches, s => factory(s.Faker));
        return Derive(_config with { Conventions = _config.Conventions.Add(convention) });
    }

    /// <summary>Gives every member of type <typeparamref name="TValue"/> (also its nullable form) the same <paramref name="value"/>. See <see cref="WithEveryMemberOfType{TValue}(Func{Faker, TValue})"/>.</summary>
    public ObjectGenerator<T> WithEveryMemberOfType<TValue>(TValue value) => WithEveryMemberOfType(_ => value);

    /// <summary>
    /// Generates every member of type <typeparamref name="TValue"/> (also its nullable form) with <paramref name="factory"/>, whatever its name and
    /// also in nested objects, e.g. <c>.WithEveryMemberOfType&lt;DateOnly&gt;(f =&gt; f.Date.Past().Date)</c> or <c>.WithEveryMemberOfType("N/A")</c> for every string.
    /// </summary>
    /// <remarks>
    /// A rule for the member itself (<c>With</c>, <c>Ignore</c>…) wins; this comes before name conventions and before app-wide conventions
    /// (<c>Fake.Conventions</c>). It sets members: the elements of collections and dictionaries keep their generated values, and data
    /// annotations are not applied to the value. Same as <c>WithConvention&lt;TValue&gt;(_ =&gt; true, factory)</c>.
    /// </remarks>
    public ObjectGenerator<T> WithEveryMemberOfType<TValue>(Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var convention = new CustomConvention(typeof(TValue), static _ => true, s => factory(s.Faker), nameof(WithEveryMemberOfType));
        return Derive(_config with { Conventions = _config.Conventions.Add(convention) });
    }

    /// <summary>
    /// Makes <c>Generate</c>, <c>GenerateParallel</c> and <c>Populate</c> throw when a rule whose reach depends on the object graph did not apply
    /// to anything that was generated: a path rule (<c>x =&gt; x.Billing.City</c>, <c>x =&gt; x.Lines.Each().Sku</c>), <see cref="Subtype{TBase, T1}"/>,
    /// <see cref="WithGeneratorFor{TNested}"/> and <see cref="WithConvention{TValue}"/>. Rules that are silently skipped become test failures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule is used as soon as it applied once, to any object generated by this generator so far, so a rule that only reaches some objects
    /// (a nullable member, a collection that is sometimes empty) is fine as long as one object in the call had it. A rule stays unused when
    /// the object it is for never exists (a member that is always null or an empty collection, <see cref="MaxDepth"/>, a cycle, another
    /// subtype), when a rule for an enclosing member or a <see cref="WithGeneratorFor{TNested}"/> generator creates or skips that object itself, when
    /// <see cref="Populate(T, PopulateMode)"/> keeps an existing object (path rules only shape objects that are created), or when nothing in the
    /// graph has that member or type. The exception lists every unused rule.
    /// </para>
    /// <para>
    /// Rules for the generated object's own members (<c>With(x =&gt; x.Name, …)</c>) always apply and are not checked, and neither is a generator
    /// that creates nested objects for another one: it cannot verify because it has no <c>Generate</c> call of its own, so passing such a generator to
    /// <c>With</c> or <c>WithGeneratorFor</c> throws. <see cref="Stream"/> has no end to check at and is not supported; generate a fixed number of objects instead.
    /// <see cref="InvalidCases"/> and <see cref="BoundaryCases"/> are not verified. Nothing is checked when no object is generated.
    /// </para>
    /// </remarks>
    /// <param name="verify"><see langword="false"/> turns the check off again.</param>
    /// <exception cref="InvalidOperationException">After <c>Generate</c>, <c>GenerateParallel</c> or <c>Populate</c>: a rule did not apply to anything.</exception>
    /// <exception cref="NotSupportedException">From <see cref="Stream"/>.</exception>
    public ObjectGenerator<T> VerifyRules(bool verify = true) => Derive(_config with { Verify = verify });

    /// <summary>
    /// Fails when a scalar member (string, number, date…) has neither a rule, a name convention nor data annotations
    /// that define its value (<c>[EmailAddress]</c>, <c>[Range]</c>, <c>[RegularExpression]</c>, <c>[AllowedValues]</c>), so new
    /// members can't silently get meaningless data. Objects and collections are populated recursively and don't count.
    /// </summary>
    public ObjectGenerator<T> Strict(bool strict = true) => Derive(_config with { Strict = strict });

    /// <summary>Turns name conventions off: members get values based on their type only.</summary>
    public ObjectGenerator<T> WithNameConventions(bool useConventions = true) => Derive(_config with { Settings = _config.Settings with { WithNameConventions = useConventions } });

    /// <summary>
    /// Makes the dates of a graph tell one story: a nested object (an order of a customer, a line of an order) is created between the creation of the object
    /// that contains it and the reference date, so <c>order.CreatedAt</c> is never before <c>customer.CreatedAt</c>, and its "updated" and "recent" dates follow.
    /// </summary>
    /// <remarks>
    /// It changes the range of the one creation date each object draws, not the number of draws, so the other members keep their values. The parent's creation
    /// date is drawn when a child asks for it, so a parent without a "created"-style member draws it then, which moves the data after it.
    /// The bound is the parent's creation date: a constant rule on its created-style member or a value <c>Populate</c> keeps sets it, and a factory or custom
    /// convention sets it when that member is produced before the nested object. Objects of a nested generator (<c>WithGeneratorFor</c>, <c>With</c>) follow that generator's setting.
    /// </remarks>
    /// <param name="enabled"><see langword="false"/> turns it off again.</param>
    public ObjectGenerator<T> WithCausalTimelines(bool enabled = true) => Derive(_config with { Settings = _config.Settings with { CausalTimelines = enabled } });

    /// <summary>
    /// Gives optional constructor parameters (<c>int priority = 3</c>) the default their type declares instead of a generated value, in this object and in
    /// every object nested in it. Explicit rules, path rules, <c>Unique</c>, <c>Where</c> conditions and feeds still decide the members they name.
    /// </summary>
    /// <remarks>
    /// A default draws nothing, so the members after it get other values than without this option (the data is still the same for a seed).
    /// Objects of a nested generator (<c>WithGeneratorFor</c>, <c>With</c>) follow that generator's setting.
    /// </remarks>
    /// <param name="enabled"><see langword="false"/> turns it off again.</param>
    public ObjectGenerator<T> WithParameterDefaults(bool enabled = true) => Derive(_config with { Settings = _config.Settings with { ParameterDefaults = enabled } });

    /// <summary>Probability (0–1) that nullable members (<c>int?</c>, <c>string?</c>…) are left <see langword="null"/>. Default 0.</summary>
    public ObjectGenerator<T> NullProbability(double probability)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(probability);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(probability, 1);
        return Derive(_config with { Settings = _config.Settings with { NullProbability = probability } });
    }

    /// <summary>Sets how many elements generated collections get. Default 1–3.</summary>
    public ObjectGenerator<T> CollectionSize(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        return Derive(_config with { Settings = _config.Settings with { MinCollectionSize = min, MaxCollectionSize = max } });
    }

    /// <summary>Sets how deep nested objects are generated (default 4); deeper members stay <see langword="null"/>.</summary>
    public ObjectGenerator<T> MaxDepth(int depth)
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
    /// <c>Sequence</c> or <c>Cycle</c> rules can't be given to <c>With</c>, <c>WithGeneratorFor</c> or <c>Fake.Generators.Add</c>: it counts the objects it generates itself, and every
    /// nested object would be number 0. <see cref="Populate(T, PopulateMode)"/> numbers the objects it fills in the same way and keeps members that are set.
    /// </remarks>
    /// <param name="member">The member to number: <c>x =&gt; x.Code</c>.</param>
    /// <param name="factory">The value for the object at a zero-based position.</param>
    public ObjectGenerator<T> Sequence<TValue>(Expression<Func<T, TValue>> member, Func<long, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        MemberName(member); // a position belongs to the generated object, not to a member of a nested one
        return AddRule(member, new Rule(RuleKind.Convention, (Func<FakeScope, object?>)(scope => factory(scope.Index)), Positional: true));
    }

    /// <summary>
    /// Gives a number member values from a normal (Gaussian) distribution, e.g. <c>.WithNormalDistribution(x =&gt; x.Height, 175, 8)</c>: most heights are near 175, about two thirds within 8 of it.
    /// Realistic measurements, ages, scores and durations look like this, where a plain range gives every value the same chance.
    /// </summary>
    /// <remarks>
    /// Works for every numeric member type (<c>int</c>, <c>decimal</c>, <c>double</c>, <c>byte</c>, <c>Half</c>, <c>BigInteger</c>…, also nullable) and for members of nested objects and collection elements
    /// (<c>x =&gt; x.Lines.Each().Quantity</c>); like <c>With</c> it takes the member's rule slot, so a later rule for the member replaces it. Whole-number types are rounded to integers, other types to two
    /// decimals; a value the type cannot hold becomes its largest or smallest, so give <paramref name="min"/> and <paramref name="max"/> when that matters. The rule does not read the member's
    /// <c>[Range]</c>: pass the same limits to keep the values valid. Item <em>n</em> is the same however you generate, and the numbers are the same on every platform.
    /// </remarks>
    /// <param name="member">The number member: <c>x =&gt; x.Height</c>.</param>
    /// <param name="mean">The centre of the distribution.</param>
    /// <param name="standardDeviation">The spread: about 68 % of the values lie within one deviation of the mean, 95 % within two. Zero gives the mean every time.</param>
    /// <param name="min">The smallest value (a value below it is drawn again), or <see langword="null"/> for none.</param>
    /// <param name="max">The largest value (a value above it is drawn again), or <see langword="null"/> for none.</param>
    /// <param name="decimals">The number of decimals to round to (0 to 15), or <see langword="null"/> for the type's default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mean"/> or a limit is not a number, <paramref name="standardDeviation"/> is negative or not finite, or <paramref name="decimals"/> is not 0 to 15.</exception>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public ObjectGenerator<T> WithNormalDistribution<TNumber>(Expression<Func<T, TNumber>> member, double mean, double standardDeviation, double? min = null, double? max = null, int? decimals = null)
        where TNumber : INumber<TNumber>
    {
        NumberModule.ValidateNormal(mean, standardDeviation, min, max, decimals);
        return With(member, faker => faker.Number.Normal<TNumber>(mean, standardDeviation, min, max, decimals));
    }

    /// <inheritdoc cref="WithNormalDistribution{TNumber}(Expression{Func{T, TNumber}}, double, double, double?, double?, int?)"/>
    public ObjectGenerator<T> WithNormalDistribution<TNumber>(Expression<Func<T, TNumber?>> member, double mean, double standardDeviation, double? min = null, double? max = null, int? decimals = null)
        where TNumber : struct, INumber<TNumber>
    {
        NumberModule.ValidateNormal(mean, standardDeviation, min, max, decimals);
        return With(member, faker => (TNumber?)faker.Number.Normal<TNumber>(mean, standardDeviation, min, max, decimals));
    }

    /// <summary>
    /// Gives a member the <paramref name="values"/> in turn, by the object's position: the first object gets the first value, the second the second, and after the last
    /// the first again. See <see cref="Sequence{TValue}"/> for what that means for seeds, batches and nested generators.
    /// </summary>
    /// <param name="member">The member to give the values to: <c>x =&gt; x.Status</c>.</param>
    /// <param name="values">The values, in order (copied when the method is called).</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public ObjectGenerator<T> Cycle<TValue>(Expression<Func<T, TValue>> member, IReadOnlyList<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException($"Cycle({member}) needs at least one value to cycle through.", nameof(values));
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
    /// Also applies to what the generator creates as a nested generator (<c>With(x =&gt; x.Address, generator)</c>, <see cref="WithGeneratorFor{TNested}"/>).
    /// <see cref="Populate(T, PopulateMode)"/> supports it with <see cref="PopulateMode.All"/> only, because values that are kept cannot be
    /// generated again. <see cref="InvalidCases"/> and <see cref="BoundaryCases"/> ignore it: their objects are made to sit outside or on the edge of the valid ones.
    /// </para>
    /// </remarks>
    /// <param name="predicate">The condition an object must satisfy.</param>
    /// <param name="maxAttempts">How many objects may be generated for one result before giving up (default 1000).</param>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: no object satisfied the condition within <paramref name="maxAttempts"/> attempts.</exception>
    public ObjectGenerator<T> Where(Func<T, bool> predicate, int maxAttempts = 1000)
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
    public ObjectGenerator<T> Where<TValue>(Expression<Func<T, TValue>> member, Func<TValue, bool> predicate, int maxAttempts = 1000)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        var name = MemberName(member);
        Func<object?, bool> accepts = value => predicate((TValue)value!);
        var filter = new MemberCondition(accepts, maxAttempts);
        if (_config.MemberFilters.TryGetValue(name, out var existing))
        {
            var earlier = existing.Accepts;
            filter = new MemberCondition(value => earlier(value) && accepts(value), Math.Min(existing.MaxAttempts, maxAttempts));
        }

        return Derive(_config with { MemberFilters = _config.MemberFilters.SetItem(name, filter) });
    }

    /// <summary>Creates instances with <paramref name="factory"/> instead of a constructor; rules are applied afterwards, conventions are not.</summary>
    public ObjectGenerator<T> CreateWith(Func<Faker, T> factory) =>
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
    /// handling may not cope. A nested generator (<c>WithGeneratorFor</c>, <c>With</c>) wires the graph it creates, which starts at its own objects. <see cref="Populate(T, PopulateMode)"/>
    /// fills the missing references of an existing graph the same way.
    /// </para>
    /// </remarks>
    /// <param name="enabled"><see langword="false"/> turns it off again.</param>
    public ObjectGenerator<T> WithBackReferences(bool enabled = true) => Derive(_config with { BackReferences = enabled });

    /// <summary>
    /// Copies a key of the generated object to a foreign key in the objects below it, so a generated aggregate joins up:
    /// <c>.WithParentKey(c =&gt; c.Orders.Each().CustomerId, c =&gt; c.Id)</c> gives every order its customer's id.
    /// </summary>
    /// <remarks>
    /// The keys are set on the finished object, after rules that depend on the object and <see cref="WithBackReferences"/>, and before
    /// <see cref="Where(Func{T, bool}, int)"/>, <see cref="Unique{TValue}"/> and <see cref="AfterCreate"/> see it, so a key that a rule gives the parent is the one copied.
    /// Nothing is drawn: the rest of the data is the data of the same seed without it. <see cref="Populate(T, PopulateMode)"/> sets the keys of an existing graph the same way,
    /// replacing what the foreign keys hold.
    /// </remarks>
    /// <param name="foreignKey">The foreign key, in a nested object or in each element of a collection: <c>c =&gt; c.Orders.Each().CustomerId</c>.</param>
    /// <param name="key">Reads the key from the generated object.</param>
    /// <exception cref="ArgumentException"><paramref name="foreignKey"/> is not a settable member of an object inside <typeparamref name="T"/>, or a value type is on the path.</exception>
    public ObjectGenerator<T> WithParentKey<TKey>(Expression<Func<T, TKey>> foreignKey, Func<T, TKey> key) => WithParentKey<T, TKey>(foreignKey, key);

    /// <summary>
    /// Copies a key of the nearest <typeparamref name="TParent"/> above a foreign key to it, for keys deeper in the graph:
    /// <c>.WithParentKey(c =&gt; c.Orders.Each().Lines.Each().OrderId, (Order o) =&gt; o.Id)</c> gives every line the id of its own order.
    /// See <see cref="WithParentKey{TKey}(Expression{Func{T, TKey}}, Func{T, TKey})"/> for when the keys are set.
    /// </summary>
    /// <param name="foreignKey">The foreign key, in a nested object or in each element of a collection.</param>
    /// <param name="key">Reads the key from the parent; write its parameter type (<c>(Order o) =&gt; o.Id</c>) to say which object is the parent.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="foreignKey"/> is not a settable member of an object inside <typeparamref name="T"/>, a value type is on the path, or no object on the path above it is a <typeparamref name="TParent"/>.
    /// </exception>
    public ObjectGenerator<T> WithParentKey<TParent, TKey>(Expression<Func<T, TKey>> foreignKey, Func<TParent, TKey> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var steps = PathSteps(foreignKey);
        if (steps.Count < 2)
        {
            throw new ArgumentException(
                $"'{foreignKey}' must select a member of an object inside {typeof(T).Name}, like c => c.Orders.Each().CustomerId. Give a member of {typeof(T).Name} itself its value with .With(x => x.Member, (f, x) => …).",
                nameof(foreignKey));
        }

        if (steps[^1].Member is PropertyInfo { SetMethod: not { IsPublic: true } } or FieldInfo { IsInitOnly: true })
            throw new ArgumentException($"'{foreignKey}' must select a settable member: {steps[^1].Member.Name} has no public setter.", nameof(foreignKey));

        // The objects on the path are the owners of its members; the owner of the foreign key is not its own parent.
        if (steps.Skip(1).Select(step => step.Owner).FirstOrDefault(owner => owner.IsValueType) is { } valueType)
            throw new ArgumentException($"'{foreignKey}' goes through {valueType.Name}, and {valueType.Name} is a value type: a key set on a copy would be lost. Use a class for it.", nameof(foreignKey));

        if (!steps.Take(steps.Count - 1).Any(step => typeof(TParent).IsAssignableFrom(step.Owner)))
            throw new ArgumentException($"{typeof(TParent).Name} can't be the parent: no object on the path to {foreignKey.Body} is a {typeof(TParent).Name}.", nameof(key));

        var parentKey = new ParentKey([.. steps.Select(s => s.Member.Name)], typeof(TParent), parent => key((TParent)parent));
        return Derive(_config with { ParentKeys = _config.ParentKeys.RemoveAll(k => k.Name == parentKey.Name).Add(parentKey) });
    }

    /// <summary>
    /// Fills members from the rows of a CSV or JSON <see cref="DataFeed"/>: object <em>n</em> takes row <em>n</em> (the rows repeat), and each column fills the member of
    /// the same name, e.g. <c>.WithFeed(DataFeed.FromCsvFile("customers.csv"))</c> with columns <c>FirstName,LastName,City</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Column and member names are compared ignoring case, spaces and underscores (<c>first_name</c> fills <c>FirstName</c>); values are converted to the member's
    /// type with the invariant culture (strings, numbers, booleans, enums by name, dates and times, <see cref="Guid"/>, <see cref="Uri"/>; an empty value of a
    /// nullable member is <see langword="null"/>). A rule for the member (<c>With</c>, <c>Ignore</c>…) wins over the feed, and the feed wins over name conventions
    /// and <c>WithEveryMemberOfType</c>. Members without a column are generated as usual, independently of the row: add the columns the values must agree with.
    /// </para>
    /// <para>
    /// The row depends on the object's position only, so results are the same whichever way the objects are generated. A generator with a feed can't be given to
    /// <c>With</c>, <c>WithGeneratorFor</c> or <c>Fake.Generators.Add</c>: every nested object would take the first row.
    /// </para>
    /// </remarks>
    /// <param name="feed">The rows.</param>
    /// <param name="order">Row <em>n</em> for object <em>n</em> (the default), or a reproducible random row for each object.</param>
    /// <param name="ignoreUnknownColumns">Skip columns that no member of the generated type matches instead of throwing.</param>
    /// <exception cref="InvalidOperationException">At <c>Generate</c>: a column matches no member (unless ignored), or a value can't be converted to its member's type; the message names the row and column.</exception>
    public ObjectGenerator<T> WithFeed(DataFeed feed, FeedOrder order = FeedOrder.InOrder, bool ignoreUnknownColumns = false)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return Derive(_config with { Feed = new FeedSettings(feed, order, ignoreUnknownColumns) });
    }

    /// <summary>Runs <paramref name="action"/> on every generated object, after all members are set.</summary>
    public ObjectGenerator<T> AfterCreate(Action<Faker, T> action) =>
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

    /// <summary>
    /// Generates between <paramref name="min"/> and <paramref name="max"/> objects (both included); the count comes from the seed, so a seeded generator gives the
    /// same counts on every run.
    /// </summary>
    /// <remarks>
    /// The count is drawn from a stream of its own (from the base seed and the number of ranged calls so far, so an empty result doesn't repeat itself), never from the objects' randomness, so the objects are exactly
    /// the ones <see cref="Generate(int)"/> makes: changing the range changes how many there are, not what object <em>n</em> looks like.
    /// </remarks>
    /// <example><code>List&lt;Order&gt; orders = Fake.For&lt;Order&gt;().Seed(1).Generate(3, 10);</code></example>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative or <paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public List<T> Generate(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        return Generate(min == max ? min : new SeededRandom(SeededRandom.DeriveSeed(unchecked(BaseSeed ^ CountStreamSalt), _state.NextRangedCall())).Int(min, max));
    }

    /// <summary>Separates the stream the object count is drawn from from the streams of the objects themselves.</summary>
    private const long CountStreamSalt = 0x0C0DE5EED5L;

    /// <summary>Generates objects lazily and endlessly; take what you need: <c>gen.Stream().Take(10)</c>.</summary>
    public IEnumerable<T> Stream()
    {
        if (_config.Verify)
            throw new NotSupportedException($"VerifyRules checks when a call has generated its objects, and a stream never ends. Generate a fixed number of {typeof(T).Name} objects with Generate(count), or call VerifyRules(false).");
        return Endless();

#pragma warning disable S2190 // an endless stream by design: the caller stops enumerating (Take, First), which ends the iterator
        IEnumerable<T> Endless()
        {
            var run = Start();
            while (true)
                yield return run.Create(_state.NextIndex(1));
        }
#pragma warning restore S2190
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
        // Checked before anything is filled in: a null element must not leave the elements before it changed and the sequence moved.
        T[] items = [.. instances]; // one snapshot, so a sequence that can be read only once is still filled
        if (Array.Exists(items, item => item is null))
            throw new ArgumentException("The collection contains a null element.", nameof(instances));

        var run = Start();
        var first = _state.NextIndex(items.Length);
        var result = new List<T>(items.Length);
        var index = 0;
        foreach (var item in items)
            result.Add(run.Populate(item, first + index++, mode));

        if (result.Count > 0)
            VerifyUsage();
        return result;
    }

    /// <summary>
    /// Generates the object that belongs to <paramref name="key"/>: the same key gives the same object in every run, process and machine, whatever else
    /// this generator made before, e.g. <c>Fake.For&lt;Customer&gt;().GenerateFor("customer:42")</c>. With a <see cref="Seed"/>, the seed is mixed in, so
    /// another seed gives another mapping. The mapping is reproducible, not secret (generated values can reveal the seed): when an object must not lead back
    /// to its key, use a keyed hash of the real value (HMAC with a secret you keep out of the data) as the key.
    /// </summary>
    /// <remarks>
    /// Dates are relative to <see cref="Faker.DefaultSeededReferenceDate"/> unless a <see cref="ReferenceDate"/> or <see cref="TimeProvider"/> is set, so
    /// they don't drift either. Every keyed object is position 0 of a sequence, so <see cref="Sequence{TValue}"/> and <see cref="Cycle{TValue}"/> give it their first value.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The generator has a <see cref="Unique{TValue}"/> rule: a key that comes again must give the same object.</exception>
    public T GenerateFor(string key)
    {
        var itemSeed = KeyedSeed(key);
        var item = Start(stableDates: true).CreateFrom(0, null, itemSeed);
        VerifyUsage();
        return item;
    }

    /// <summary>
    /// Replaces the data of <paramref name="instance"/> with stand-ins that belong to its key: every member is generated again (<see cref="PopulateMode.All"/>)
    /// from <c>keyOf(instance)</c>, read before anything is changed, so the same real customer gets the same fake name, email and address in every table, run and
    /// process. Keep members such as ids with <see cref="Ignore{TValue}"/>. The mapping is reproducible, not secret, as for <see cref="GenerateFor"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var masked = Fake.For&lt;Customer&gt;().Ignore(c =&gt; c.Id).Pseudonymize(customers, c =&gt; c.Email);
    /// </code>
    /// </example>
    /// <returns><paramref name="instance"/>, or a changed copy when <typeparamref name="T"/> is a struct.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instance"/> or <paramref name="keyOf"/> is <see langword="null"/>, or the key is.</exception>
    /// <exception cref="InvalidOperationException">The generator has a <see cref="Unique{TValue}"/> rule: a key that comes again must give the same object.</exception>
    public T Pseudonymize(T instance, Func<T, string> keyOf)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));
        ArgumentNullException.ThrowIfNull(keyOf);
        var itemSeed = KeyedSeed(keyOf(instance));
        var item = Start(stableDates: true).PopulateFrom(instance, 0, PopulateMode.All, itemSeed);
        VerifyUsage();
        return item;
    }

    /// <summary>Like <see cref="Pseudonymize(T, Func{T, string})"/> for every object of <paramref name="instances"/>; every key is read before any object is changed.</summary>
    /// <returns>The changed objects in the same order (copies for structs).</returns>
    public List<T> Pseudonymize(IEnumerable<T> instances, Func<T, string> keyOf)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(keyOf);
        // Every key is read before anything changes: an object that is in the list twice must keep its real key the second time.
        var items = new List<(T Item, long Seed)>();
        foreach (var item in instances)
        {
            if (item is null)
                throw new ArgumentException("The collection contains a null element.", nameof(instances));
            items.Add((item, KeyedSeed(keyOf(item))));
        }

        var run = Start(stableDates: true);
        var result = new List<T>(items.Count);
        foreach (var (item, seed) in items)
            result.Add(run.PopulateFrom(item, 0, PopulateMode.All, seed));

        if (result.Count > 0)
            VerifyUsage();
        return result;
    }

    /// <summary>
    /// Checks that <paramref name="property"/> holds for <paramref name="count"/> generated objects, property-based testing style. When it returns
    /// <see langword="false"/> for one, the object is <em>shrunk</em>: generated again from simpler draws until no simpler object fails, and a
    /// <see cref="FakeCheckException{T}"/> reports that counterexample, the original, the seed and how to replay it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every candidate is made by this generator, so shrinking never leaves your rules, annotations and conventions: a counterexample is an object the
    /// generator could have made, with fewer collection elements, shorter texts, smaller numbers and earlier list values, and its names, email and dates still
    /// agree. Shrinking keeps to the kind of failure it started with (returning <see langword="false"/>, or the same exception type), and is deterministic.
    /// </para>
    /// <para>
    /// The seed is the generator's <see cref="Seed"/>, else <c>PHONEY_SEED</c>, else a new random one that the failure reports. The objects are those
    /// <see cref="Generate(int)"/> makes for that seed from position 0, and the generator's own sequence doesn't move.
    /// </para>
    /// </remarks>
    /// <example><code>Fake.For&lt;Order&gt;().Check(order =&gt; Pricing.Total(order) &gt;= 0);</code></example>
    /// <param name="property">What must hold for every object.</param>
    /// <param name="count">How many objects to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler objects shrinking may try; 0 reports the original object.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for an object.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    public void Check(Func<T, bool> property, int count = 100, int maxShrinks = 1000)
    {
        ArgumentNullException.ThrowIfNull(property);
        Check(item => PropertyCheck<T>.Outcome.Of(() => property(item)), count, maxShrinks);
    }

    /// <summary>
    /// Checks that <paramref name="property"/> doesn't throw for <paramref name="count"/> generated objects, so assertions of any test framework can be the
    /// property: <c>Check(order =&gt; Pricing.Total(order).ShouldBeGreaterThanOrEqualTo(0))</c>. A failing object is shrunk as for
    /// <see cref="Check(Func{T, bool}, int, int)"/>, and the exception the property threw for the counterexample is the
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    /// <param name="property">The assertions for one object.</param>
    /// <param name="count">How many objects to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler objects shrinking may try; 0 reports the original object.</param>
    /// <exception cref="FakeCheckException{T}">The property threw for an object.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    public void Check(Action<T> property, int count = 100, int maxShrinks = 1000)
    {
        ArgumentNullException.ThrowIfNull(property);
        Check(item => PropertyCheck<T>.Outcome.Of(() => property(item)), count, maxShrinks);
    }

    /// <summary>
    /// The asynchronous form of <see cref="Check(Action{T}, int, int)"/>, for properties that await (TUnit's assertions, a service under test): a
    /// property fails when its task faults, and a failing object is shrunk the same way.
    /// </summary>
    /// <param name="property">The asynchronous assertions for one object.</param>
    /// <param name="count">How many objects to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler objects shrinking may try; 0 reports the original object.</param>
    /// <param name="cancellationToken">Stops the check between objects.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for an object.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task CheckAsync(Func<T, Task> property, int count = 100, int maxShrinks = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(property);
        var check = StartCheck(count, maxShrinks, out var checking);
        await check.RunAsync(property, cancellationToken).ConfigureAwait(false);
        EndCheck(checking, count);
    }

    /// <summary>The synchronous check: every object of <see cref="PropertyCheck{T}.Objects"/> through <paramref name="evaluate"/>.</summary>
    private void Check(Func<T, PropertyCheck<T>.Outcome> evaluate, int count, int maxShrinks)
    {
        var check = StartCheck(count, maxShrinks, out var checking);
        check.Run(evaluate);
        EndCheck(checking, count);
    }

    /// <summary>
    /// Prepares a check on a copy of this generator with the check's seed, so the objects are those of positions 0, 1, 2… and this generator's sequence
    /// doesn't move. Shrinking makes an object again from a tape at its position; a fresh copy per candidate when uniqueness is tracked, so values taken
    /// by earlier candidates don't count.
    /// </summary>
    private PropertyCheck<T> StartCheck(int count, int maxShrinks, out ObjectGenerator<T> checking)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShrinks);
        var seed = CheckSeed.Choose(_config.Seed);
        var generator = checking = Derive(_config with { Seed = seed.Value });
        var tracksUniqueness = !_config.Unique.IsEmpty || !_config.UniqueKeys.IsEmpty;
        Run? run = null;
        return new PropertyCheck<T>(
            (index, tape) => (run ??= generator.Start()).CreateOnTape(index, tape),
            (index, tape) => (tracksUniqueness ? generator.Derive(generator._config).Start() : run ??= generator.Start()).CreateOnTape(index, tape),
            count,
            maxShrinks,
            seed);
    }

    /// <summary>Verifies the rules once the property held for every object.</summary>
    private static void EndCheck(ObjectGenerator<T> checking, int count)
    {
        if (count > 0)
            checking.VerifyUsage();
    }

    /// <summary>The item seed of <paramref name="key"/>: from the key and the configured seed only (never the random base seed of an unseeded generator), so it is stable everywhere.</summary>
    private long KeyedSeed(string key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key), "The key of an object can't be null.");
        if (!_config.Unique.IsEmpty || !_config.UniqueKeys.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Unique can't be combined with GenerateFor or Pseudonymize on {typeof(T).Name}: a key that comes again must give the same object, "
                + "which Unique would reject as a duplicate and replace. Remove the Unique rule; different keys already give different objects almost always.");
        }

        return SeededRandom.DeriveSeed(unchecked((_config.Seed ?? 0) ^ KeyStreamSalt), SeededRandom.KeySeed(key));
    }

    /// <summary>Separates keyed objects from the objects of the generator's own sequence.</summary>
    private const long KeyStreamSalt = 0x6B65_7965_64L;

    /// <inheritdoc />
    Type INestedGenerator.GeneratedType => typeof(T);

    /// <inheritdoc />
    string? INestedGenerator.CannotBeNested
    {
        get
        {
            if (_config.Verify)
                return "verifies its rules (VerifyRules), but a nested generator is never checked: it has no Generate call of its own. Call VerifyRules(false) on it and verify the generator you call Generate on.";
            if (_config.Rules.Values.Any(r => r.Positional))
                return "has Sequence or Cycle rules, which count the objects it generates itself, and as a nested generator every object would be number 0. Put the rule on the generator you call Generate on, or use With(x => x.Member, f => …) here.";
            return _config.Feed is not null
                ? "has a data feed (WithFeed), which takes a row per object it generates itself, and as a nested generator every object would take the first row. Put the feed on the generator you call Generate on."
                : null;
        }
    }

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
            + "when a rule for an enclosing member or a WithGeneratorFor generator creates or skips that object itself, when Populate keeps an existing object, or when nothing in the graph has that member or type. "
            + "Remove the rule or change what is generated; VerifyRules(false) turns this check off.");
    }

    /// <summary>A new generator with <paramref name="config"/> and fresh state.</summary>
    private ObjectGenerator<T> Derive(Config config) => new(_model, config);

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
    private ObjectGenerator<T> AddRule<TValue>(Expression<Func<T, TValue>> member, Rule rule)
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
    private Run Start(long? seed = null, Faker? parentFaker = null, bool stableDates = false)
    {
        var compiled = _state.Compiled ?? _state.Publish(Compile());
        var data = parentFaker is not null && _config.Locale is null ? parentFaker.Data : LocaleStore.Get(_config.Locale ?? Fake.Locale);
        // A new faker per run, never a pooled one: rules, AfterCreate actions, conventions and Stream() enumerators can keep the faker, and a
        // pooled faker would be reseeded under them by later runs, silently changing seeded data (a nested run also needs its own while the parent's
        // is in use). The facade's per-call cost is in compiling rules, which Fresh() shares; keep a generator to avoid the faker too.
        var faker = new Faker(data, new SeededRandom(0) { Tape = parentFaker?.Random.Tape }); // a nested object's draws belong to the checked object's tape
        _config.Clock.Apply(faker, parentFaker, _config.Seed is not null || stableDates);
        return new Run(this, compiled, faker, seed ?? BaseSeed);
    }

    /// <summary>Binds the rules (by member name) to the model's member indices and validates them.</summary>
    private Compiled Compile()
    {
        var registrations = Registrations.Current; // read first: a registration made while compiling makes the result stale, never wrongly current
        var members = _model.Members;
        var usage = _config.Verify ? RegisterUsage() : null;
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < members.Count; i++)
            known[members[i].Name] = i;

        var dependent = new List<(int Member, Func<Faker, T, object?> Rule)>();
        var rules = RuleDecisions(members, known, dependent);
        var unique = UniqueAttempts(known);
        var feed = _config.Feed is { } feedSettings ? FeedSource.Decisions(typeof(T).Name, feedSettings, BaseSeed, members, rules) : null;
        var conventions = registrations.ConventionsWith(_config.Conventions);
        var plan = MemberPlan.Build(members, new MemberPlanInputs
        {
            Rules = rules,
            Feed = feed,
            Unique = unique,
            ParameterDefaults = _config.Settings.ParameterDefaults,
            Conventions = conventions,
            Usage = usage,
            Conditions = _config.MemberFilters,
            UniqueKeys = _config.UniqueKeys,
            Strict = _config.Strict,
            NameConventions = _config.Settings.WithNameConventions,
            CreatesWithFactory = _config.Factory is not null,
            TypeName = typeof(T).Name,
        });

        // Subtypes and custom conventions apply to the whole object graph, so they travel with the settings into nested scopes.
        var settings = _config.Settings with
        {
            Subtypes = registrations.SubtypesWith(_config.Subtypes),
            Conventions = NestedConventions.For(conventions, usage, _config.Settings.ParameterDefaults),
            TypeGenerators = registrations.GeneratorsWith(_config.TypeGenerators),
            Usage = usage,
        };
        return new Compiled(plan.Slots, [.. dependent], settings, PathRules.Build(typeof(T), members, _config.PathRules, usage), usage, plan.ObjectRetries, plan.Combinations, registrations);
    }

    /// <summary>The explicit rules by member index, in the order they were added; the rules that depend on the object are also collected in <paramref name="dependent"/>.</summary>
    private Dictionary<int, SlotDecision> RuleDecisions(IReadOnlyList<FakeMember> members, Dictionary<string, int> known, List<(int Member, Func<Faker, T, object?> Rule)> dependent)
    {
        var rules = new Dictionary<int, SlotDecision>();

        // In the order the rules were added: dependent rules draw from the faker, and the first invalid rule is the one reported.
        foreach (var (name, rule) in _config.Rules.OrderBy(r => r.Value.Added))
        {
            if (!known.TryGetValue(name, out var index))
                throw new InvalidOperationException($"{typeof(T).Name}.{name} can't be generated: it is not a public settable member or constructor parameter.");
            if (rule.Kind == RuleKind.Dependent)
                dependent.Add((index, DependentRule(members[index], rule)));

            var value = rule.Kind == RuleKind.Constraint ? ConstraintCases.Resolve(typeof(T).Name, members[index], (ConstraintRequest)rule.Value!) : rule.Value;
            rules[index] = new SlotDecision(rule.Kind, value, rule.Boxed);
        }

        return rules;
    }

    /// <summary>The attempts allowed per value for each <c>Unique</c> member, by member index.</summary>
    private Dictionary<int, int> UniqueAttempts(Dictionary<string, int> known)
    {
        var unique = new Dictionary<int, int>();
        foreach (var (name, attempts) in _config.Unique.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            if (!known.TryGetValue(name, out var index))
                throw new InvalidOperationException($"{typeof(T).Name}.{name} is not a generated member.");
            unique[index] = attempts;
        }

        return unique;
    }

    /// <summary>A rule that reads the object, checked against members that are set before the object exists.</summary>
    private static Func<Faker, T, object?> DependentRule(FakeMember member, Rule rule)
    {
        if (member.IsConstructorParameter)
            throw new InvalidOperationException($"{typeof(T).Name}.{member.Name} is a constructor parameter, so it can't depend on the object. Use a rule without the object parameter.");
        if (member.FillsExisting)
            throw new InvalidOperationException($"{typeof(T).Name}.{member.Name} is a read-only collection that is filled in place, so it can't be set from the object. Use a rule without the object parameter.");
        return (Func<Faker, T, object?>)rule.Value!;
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
    private sealed class Run(ObjectGenerator<T> generator, Compiled compiled, Faker faker, long seed)
    {
        /// <summary>Creates item <paramref name="index"/> with its draws recorded on, or replayed from, <paramref name="tape"/>.</summary>
        public T CreateOnTape(long index, ChoiceTape tape)
        {
            faker.Random.Tape = tape;
            try
            {
                return Create(index);
            }
            finally
            {
                faker.Random.Tape = null;
            }
        }

        /// <summary>Creates item <paramref name="index"/>; its seed depends only on the base seed and the index (and the attempt, when a condition rejects an object).</summary>
        public T Create(long index, FakeScope? parent = null) => CreateFrom(index, parent, SeededRandom.DeriveSeed(seed, index));

        /// <summary>Creates an object from <paramref name="itemSeed"/> (and attempts derived from it), as item <paramref name="index"/>.</summary>
        public T CreateFrom(long index, FakeScope? parent, long itemSeed)
        {
            return MayRetry ? CreateAccepted(index, parent, itemSeed) : CreateOnce(index, parent, itemSeed).Item;
        }

        // Apart from CreateFrom, so the closure is only allocated when an object may be made again (captured parameters are allocated on entry).
        private T CreateAccepted(long index, FakeScope? parent, long itemSeed) =>
            Accepted(default!, itemSeed, PopulateMode.All, (_, attemptSeed) => CreateOnce(index, parent, attemptSeed));

        /// <summary>Whether an object can be rejected and made again: by a condition, a unique identity member or a unique combination.</summary>
        private bool MayRetry => !generator._config.Filters.IsEmpty || compiled.ObjectRetries != 0;

        /// <summary>
        /// The first attempt whose object the conditions accept and whose unique values are new; attempt <c>k</c> makes the object from
        /// <see cref="Filter.AttemptSeed"/>, starting from the object the attempt before it returned. <paramref name="mode"/> is how much an attempt makes again
        /// (creating makes everything); with less than <see cref="PopulateMode.All"/> a rejected object can't be made again, so it throws.
        /// </summary>
        private T Accepted(T item, long itemSeed, PopulateMode mode, Func<T, long, (T Item, FakeScope Scope)> attempt)
        {
            var filters = generator._config.Filters;
            var limit = AttemptLimit(filters);
            for (var i = 0; i < limit; i++)
            {
                (item, var scope) = attempt(item, Filter.AttemptSeed(itemSeed, i));
                if (Filter.Accepts(filters, item) && scope.TryClaimPending(CombinationsOf(item, scope)))
                    return item;
                if (mode != PopulateMode.All)
                {
                    // The values filled in are now set, so a second pass would keep them: there is nothing to draw again.
                    throw new InvalidOperationException(
                        $"Could not populate a {typeof(T).Name} with a unique {UniqueNames()}: the value was produced before, and PopulateMode.{mode} can't generate the object again. "
                        + "Use PopulateMode.All, or give the member values with .With(…).");
                }
            }

            throw Exhausted(filters, limit);
        }

        /// <summary>The attempts an object gets: the smallest limit of the conditions and of the unique identity members.</summary>
        private int AttemptLimit(ImmutableList<Filter> filters)
        {
            if (filters.IsEmpty)
                return compiled.ObjectRetries;
            return compiled.ObjectRetries == 0 ? Filter.AttemptLimit(filters) : Math.Min(compiled.ObjectRetries, Filter.AttemptLimit(filters));
        }

        /// <summary>The error when no attempt gave an acceptable object.</summary>
        private InvalidOperationException Exhausted(ImmutableList<Filter> filters, int attempts)
        {
            if (!filters.IsEmpty && (compiled.ObjectRetries == 0 || attempts < compiled.ObjectRetries))
                return Filter.Exhausted(typeof(T), filters, attempts);
            return new InvalidOperationException(
                $"Could not generate a {typeof(T).Name} with a unique {UniqueNames()} after {attempts} attempts: almost every value was produced before. "
                + "Give the member a source with more distinct values with .With(…), generate fewer objects, or raise maxAttempts.");
        }

        /// <summary>What has to be unique per object, for messages: "FirstName, combination of UserId and RoleId".</summary>
        private string UniqueNames() => string.Join(", ", compiled.Slots.Where(s => s is { RetriesObject: true }).Select(s => s!.MemberName)
            .Concat(compiled.Combinations.Select(c => c.Description)));

        /// <summary>The values of the unique combinations on a finished object, or <see langword="null"/> when there are none.</summary>
        private (UniqueCombination, object?[])[]? CombinationsOf(T item, FakeScope scope)
        {
            if (compiled.Combinations.Length == 0)
                return null;
            var model = generator._model;
            var values = new (UniqueCombination, object?[])[compiled.Combinations.Length];
            for (var i = 0; i < values.Length; i++)
                values[i] = (compiled.Combinations[i], compiled.Combinations[i].Read(scope, member => model.Get(item, member)));
            return values;
        }

        /// <summary>Populates <paramref name="instance"/> as item <paramref name="index"/> of the sequence.</summary>
        public T Populate(T instance, long index, PopulateMode mode) => PopulateFrom(instance, index, mode, SeededRandom.DeriveSeed(seed, index));

        /// <summary>Populates <paramref name="instance"/> from <paramref name="itemSeed"/> (and attempts derived from it), as item <paramref name="index"/>.</summary>
        public T PopulateFrom(T instance, long index, PopulateMode mode, long itemSeed)
        {
            if (!MayRetry)
                return PopulateOnce(instance, index, mode, itemSeed).Item;

            if (mode != PopulateMode.All && !generator._config.Filters.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"A Where condition can't be combined with PopulateMode.{mode}: the values {typeof(T).Name} keeps can't be generated again when the condition rejects the object. Use PopulateMode.All, or Generate().");
            }

            return PopulateAccepted(instance, index, mode, itemSeed);
        }

        // Apart from PopulateFrom, like CreateAccepted.
        private T PopulateAccepted(T instance, long index, PopulateMode mode, long itemSeed) =>
            Accepted(instance, itemSeed, mode, (item, attemptSeed) => PopulateOnce(item, index, mode, attemptSeed));

        private (T Item, FakeScope Scope) CreateOnce(long index, FakeScope? parent, long itemSeed)
        {
            faker.Random.Reseed(itemSeed);
            var factory = generator._config.Factory;
            var scope = new FakeScope(faker, compiled.Settings, compiled.Slots, index, typeof(T), parent, compiled.RootChildren);
            var item = factory is null ? generator._model.Create(scope) : ApplyRules(factory(faker), scope);
            return (Complete(item, scope, PopulateMode.All, compare: factory is null), scope); // a factory decides the members no rule is for
        }

        private (T Item, FakeScope Scope) PopulateOnce(T instance, long index, PopulateMode mode, long itemSeed)
        {
            faker.Random.Reseed(itemSeed);
            var scope = new FakeScope(faker, compiled.Settings, compiled.Slots, index, typeof(T), null, compiled.RootChildren);
            return (Complete(generator._model.Populate(instance, scope, mode), scope, mode, compare: true), scope);
        }

        /// <summary>Applies the explicit rules to an object a factory created; the factory decides everything else.</summary>
        private T ApplyRules(T item, FakeScope scope)
        {
            var model = generator._model;
            for (var i = 0; i < compiled.Slots.Length; i++)
            {
                if (compiled.Slots[i] is not { Kind: RuleKind.Constant or RuleKind.Factory or RuleKind.Generator or RuleKind.Convention or RuleKind.Constraint } || model.Members[i].IsConstructorParameter)
                    continue;
                var value = scope.GetObject(i, model.Fallback(i)); // only a constraint reads the member's own value
                if (model.Members[i].FillsExisting)
                    model.Fill(item, i, value, replace: false);
                else
                    item = model.Set(item, i, value);
            }

            return item;
        }

        /// <summary>
        /// The steps that run on every object once its members are produced, created or populated, in this order: the dependent rules (only for members
        /// <paramref name="mode"/> fills), <c>[Compare]</c>, back-references, parent keys, then the AfterCreate actions. Dependent rules and AfterCreate draw, so the order is part of the seed.
        /// </summary>
        private T Complete(T item, FakeScope scope, PopulateMode mode, bool compare)
        {
            var config = generator._config;
            var model = generator._model;
            foreach (var (member, rule) in compiled.Dependent)
            {
                if (mode == PopulateMode.All || model.IsUnset(item, member))
                    item = model.Set(item, member, rule(faker, item));
            }

            if (compare)
                item = Comparisons.Apply(model, item, scope);
            if (config.BackReferences && item is not null)
                Phoney.Generation.BackReferences.Apply(item);
            if (!config.ParentKeys.IsEmpty && item is not null)
                Phoney.Generation.ParentKeys.Apply(item, config.ParentKeys);
            foreach (var action in config.AfterCreate)
                action(faker, item);
            return item;
        }
    }

    /// <summary>A condition from <see cref="Where(Func{T, bool}, int)"/> and how often an object may be generated again for it.</summary>
    private sealed record Filter(Func<T, bool> Predicate, int MaxAttempts)
    {
        /// <summary>The smallest limit of the conditions: the object has to satisfy all of them, so the strictest one gives up first.</summary>
        public static int AttemptLimit(ImmutableList<Filter> filters) => filters.Min(f => f.MaxAttempts);

        /// <summary>The first attempt is the unfiltered object; later ones draw from a seed derived from it, so they depend on the seed, position and attempt only.</summary>
        public static long AttemptSeed(long itemSeed, int attempt) => attempt == 0 ? itemSeed : SeededRandom.DeriveSeed(itemSeed, attempt);

        public static bool Accepts(ImmutableList<Filter> filters, T item)
        {
#pragma warning disable S3267 // runs for every generated object: All(...) would allocate a closure over the item each time
            foreach (var filter in filters)
            {
                if (!filter.Predicate(item))
                    return false;
            }
#pragma warning restore S3267

            return true;
        }

        public static InvalidOperationException Exhausted(Type type, ImmutableList<Filter> filters, int attempts) =>
            new($"Could not generate a {type.Name} that satisfies {(filters.Count == 1 ? "the Where condition" : "all Where conditions")} after {attempts} attempts. "
                + "Almost every generated object is rejected: set the members the condition looks at with .With(…) so that it can hold, loosen the condition, or raise maxAttempts.");
    }

    /// <summary>
    /// Rules bound to member indices, plus rules that run after creation; <paramref name="ObjectRetries"/> is the attempt limit of unique identity members and
    /// <paramref name="Combinations"/> (0 when there are none).
    /// </summary>
    private sealed record Compiled(RuleSlot?[] Slots, (int Member, Func<Faker, T, object?> Rule)[] Dependent, GenerationSettings Settings, PathNode?[]? RootChildren, RuleUsage? Usage, int ObjectRetries, UniqueCombination[] Combinations, Registrations Registrations);

    /// <summary>A configured rule; <paramref name="Boxed"/> is the object-returning twin of a typed factory, <paramref name="Added"/> its position among the rules added, <paramref name="Positional"/> whether its value depends on the object's position (<c>Sequence</c>, <c>Cycle</c>).</summary>
    private sealed record Rule(RuleKind Kind, object? Value, Func<Faker, object?>? Boxed = null, int Added = 0, bool Positional = false);

    /// <summary>Mutable per-instance state; derived generators start fresh.</summary>
    private sealed class State(State? shared)
    {
        private long _next;

        /// <summary>Base seed used when no seed is configured.</summary>
#pragma warning disable RS0030 // Base seed of an unseeded generator; a configured seed replaces it.
        public long RandomSeed { get; } = System.Random.Shared.NextInt64();
#pragma warning restore RS0030

        private Compiled? _compiled;

        /// <summary>Rules compiled against the model on first use (by this generator or the one it shares compiled rules with).</summary>
        public Compiled? Compiled
        {
            get
            {
                if (Volatile.Read(ref _compiled) is { } own)
                    return own;
                // Rules compiled by the generator this one was copied from, while the app-wide registrations are still the ones they were compiled with.
                return shared?.Compiled is { } inherited && inherited.Registrations.IsSameAs(Registrations.Current)
                    ? Interlocked.CompareExchange(ref _compiled, inherited, null) ?? inherited
                    : null;
            }
        }

        /// <summary>Stores <paramref name="compiled"/> unless another thread compiled first, and returns the one every run shares (so uniqueness and rule usage are tracked in one place).</summary>
        public Compiled Publish(Compiled compiled)
        {
            shared?.Publish(compiled); // so later copies of the same generator reuse it
            return Interlocked.CompareExchange(ref _compiled, compiled, null) ?? compiled;
        }

        /// <summary>Reserves <paramref name="count"/> consecutive item indices.</summary>
        public long NextIndex(int count) => Interlocked.Add(ref _next, count) - count;

        private long _rangedCalls;

        /// <summary>The number of this call among the ranged <c>Generate(min, max)</c> calls (0 for the first); it advances even when the call produces no objects.</summary>
        public long NextRangedCall() => Interlocked.Increment(ref _rangedCalls) - 1;
    }

    /// <summary>Immutable configuration; every builder method returns a copy.</summary>
    private sealed record Config(
        string? Locale,
        long? Seed,
        RunClock Clock,
        ImmutableDictionary<string, Rule> Rules,
        int RuleCount,
        ImmutableDictionary<string, PathSpec> PathRules,
        ImmutableDictionary<string, int> Unique,
        ImmutableDictionary<string, int> UniqueKeys,
        ImmutableList<CustomConvention> Conventions,
        ImmutableDictionary<Type, ImmutableArray<Type>> Subtypes,
        ImmutableDictionary<Type, INestedGenerator> TypeGenerators,
        ImmutableList<Action<Faker, T>> AfterCreate,
        ImmutableList<Filter> Filters,
        ImmutableDictionary<string, MemberCondition> MemberFilters,
        Func<Faker, T>? Factory,
        bool Strict,
        bool Verify,
        bool BackReferences,
        ImmutableList<ParentKey> ParentKeys,
        FeedSettings? Feed,
        GenerationSettings Settings)
    {
#pragma warning disable S2743 // one default configuration per T is intended
        public static readonly Config Default = new(
            null, null, default,
            ImmutableDictionary.Create<string, Rule>(StringComparer.Ordinal),
            0,
            ImmutableDictionary.Create<string, PathSpec>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, int>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, int>(StringComparer.Ordinal),
            [],
            ImmutableDictionary<Type, ImmutableArray<Type>>.Empty,
            ImmutableDictionary<Type, INestedGenerator>.Empty,
            [],
            [],
            ImmutableDictionary.Create<string, MemberCondition>(StringComparer.Ordinal),
            null,
            false,
            false,
            false,
            [],
            null,
            GenerationSettings.Default);
#pragma warning restore S2743
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
    /// <summary>
    /// Refuses a member whose own type (before the conversion to the interface a subset overload takes) cannot hold <paramref name="produced"/>:
    /// a <c>List&lt;T&gt;</c> cannot be assigned to an <c>ImmutableArray&lt;T&gt;</c>, and failing here names the member and says what to use, where failing at the first <c>Generate</c> would not.
    /// </summary>
    private static void RequireMemberAccepts<TValue>(Expression<Func<T, TValue>> member, Type produced)
    {
        ArgumentNullException.ThrowIfNull(member);
        var body = member.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            body = convert.Operand;
        if (body.Type.IsAssignableFrom(produced))
            return;

        static string Name(Type type) => type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">" : type.Name;
        throw new ArgumentException(
            $"'{member}' is a {Name(body.Type)}, which a subset cannot be assigned to: a {Name(produced)} is made. Use a list, array or set member, or set the member with With(x => …, f => …).",
            nameof(member));
    }

    private static string[] MemberPath<TValue>(Expression<Func<T, TValue>> expression) => [.. PathSteps(expression).Select(step => step.Member.Name)];

    /// <summary>The members a path selects, from the root on, each with the type of the object it is read from (the element type after <c>Each()</c>).</summary>
    private static List<(MemberInfo Member, Type Owner)> PathSteps<TValue>(Expression<Func<T, TValue>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var steps = new List<(MemberInfo, Type)>();
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
                        steps.Add((access.Member, owner.Type));
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

        if (steps.Count == 0)
            throw new ArgumentException($"'{expression}' must select a member of {typeof(T).Name}, like x => x.Name.", nameof(expression));
        steps.Reverse();
        return steps;

        // The values of a dictionary are what Each() walks, and a nullable's value is the object itself.
        static bool IsTransparent(string member, Type owner) =>
            (member == "Values" && owner.IsGenericType && owner.GetGenericTypeDefinition() is var d && (d == typeof(Dictionary<,>) || d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)))
            || (member == "Value" && Nullable.GetUnderlyingType(owner) is not null);
    }
}
