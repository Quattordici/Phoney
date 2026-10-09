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

    /// <summary>Generates a nested member with its own generator, e.g. <c>.With(x =&gt; x.Address, Fake.For&lt;Address&gt;().Locale("sv"))</c>.</summary>
    public Generator<T> With<TValue>(Expression<Func<T, TValue>> member, Generator<TValue> generator) =>
        AddRule(member, new Rule(RuleKind.Generator, generator ?? throw new ArgumentNullException(nameof(generator))));

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
    /// <paramref name="nameMatches"/> and has no explicit rule, e.g. <c>.WithConvention&lt;string&gt;(n =&gt; n.EndsWith("Code"), f =&gt; f.Random.Replace("??-###"))</c>.
    /// </summary>
    public Generator<T> WithConvention<TValue>(Func<string, bool> nameMatches, Func<Faker, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(nameMatches);
        ArgumentNullException.ThrowIfNull(factory);
        var convention = new CustomConvention(typeof(TValue), nameMatches, s => factory(s.Faker));
        return Derive(_config with { Conventions = _config.Conventions.Add(convention) });
    }

    /// <summary>
    /// Fails when a scalar member (string, number, date…) has neither a rule nor a name convention, so new members
    /// can't silently get meaningless data. Objects and collections are populated recursively and don't count.
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

    /// <inheritdoc />
    object? INestedGenerator.CreateNested(FakeScope parent) =>
        Start(parent.Faker.Random.Long(), parent.Faker).Create(0, parent);

    /// <summary>The base seed: the configured one, or one random seed per generator instance.</summary>
    private long BaseSeed => _config.Seed ?? _state.RandomSeed;

    /// <summary>A new generator with <paramref name="config"/> and fresh state.</summary>
    private Generator<T> Derive(Config config) => new(_model, config);

    private Generator<T> AddRule<TValue>(Expression<Func<T, TValue>> member, Rule rule) =>
        Derive(_config with { Rules = _config.Rules.SetItem(MemberName(member), rule) });

    /// <summary>Prepares a run: compiled rules, a faker for the locale and the base seed. Nested generators inherit the parent's locale and reference date.</summary>
    private Run Start(long? seed = null, Faker? parentFaker = null)
    {
        var compiled = _state.Compiled ??= Compile();
        var data = parentFaker is not null && _config.Locale is null ? parentFaker.Data : LocaleStore.Get(_config.Locale ?? Fake.Locale);
        var faker = new Faker(data, new Randomizer(0));
        // Explicit date, else the parent's (nested generators), else a fixed date for seeded generators so their
        // relative dates don't drift with the clock; unseeded generators use the current time.
        var referenceDate = _config.ReferenceDate ?? parentFaker?.ReferenceDate
            ?? (_config.Seed is not null ? Faker.DefaultSeededReferenceDate : null);
        if (referenceDate is { } date)
            faker.ReferenceDate = date;
        return new Run(this, compiled, faker, seed ?? BaseSeed);
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

        foreach (var (name, rule) in _config.Rules)
        {
            if (!known.TryGetValue(name, out var index))
                throw new InvalidOperationException($"{typeof(T).Name}.{name} can't be generated: it is not a public settable member or constructor parameter.");
            if (rule.Kind == RuleKind.Dependent)
            {
                if (members[index].IsConstructorParameter)
                    throw new InvalidOperationException($"{typeof(T).Name}.{name} is a constructor parameter, so it can't depend on the object. Use a rule without the object parameter.");
                dependent.Add((index, (Func<Faker, T, object?>)rule.Value!));
            }

            slots[index] = new RuleSlot(rule.Kind, rule.Value, _config.Unique.TryGetValue(name, out var attempts), attempts, rule.Boxed);
        }

        // Unique members without an explicit rule keep their convention value but still need a slot.
        foreach (var (name, attempts) in _config.Unique)
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
                .Where((m, i) => slots[i] is null && m.IsScalar && (m.Convention == ConventionKind.None || !_config.Settings.UseConventions))
                .Select(m => m.Name)
                .ToList();
            if (uncovered.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Strict generator for {typeof(T).Name}: no rule or convention for {string.Join(", ", uncovered)}. Add .With(...) or .Ignore(...) for them.");
            }
        }

        return new Compiled(slots, [.. dependent]);
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
            var scope = new FakeScope(faker, config.Settings, compiled.Slots, index, typeof(T), parent);

            T item;
            if (config.Factory is { } factory)
            {
                item = factory(faker);
                for (var i = 0; i < compiled.Slots.Length; i++)
                {
                    // Only explicit rules apply to factory-created objects; the factory decides everything else.
                    if (compiled.Slots[i] is { Kind: RuleKind.Constant or RuleKind.Factory or RuleKind.Generator or RuleKind.Convention } && !model.Members[i].IsConstructorParameter)
                        item = model.Set(item, i, scope.GetObject(i, static _ => null));
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
    }

    /// <summary>Rules bound to member indices, plus rules that run after creation.</summary>
    private sealed record Compiled(RuleSlot?[] Slots, (int Member, Func<Faker, T, object?> Rule)[] Dependent);

    /// <summary>A configured rule; <paramref name="Boxed"/> is the object-returning twin of a typed factory.</summary>
    private sealed record Rule(RuleKind Kind, object? Value, Func<Faker, object?>? Boxed = null);

    /// <summary>Mutable per-instance state; derived generators start fresh.</summary>
    private sealed class State
    {
        private long _next;

        /// <summary>Base seed used when no seed is configured.</summary>
        public long RandomSeed { get; } = System.Random.Shared.NextInt64();

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
        ImmutableDictionary<string, Rule> Rules,
        ImmutableDictionary<string, int> Unique,
        ImmutableList<CustomConvention> Conventions,
        ImmutableList<Action<Faker, T>> AfterCreate,
        Func<Faker, T>? Factory,
        bool Strict,
        GenerationSettings Settings)
    {
        public static readonly Config Default = new(
            null, null, null,
            ImmutableDictionary.Create<string, Rule>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, int>(StringComparer.Ordinal),
            [],
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
