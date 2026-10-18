using System.Globalization;
using Phoney.Data;
using Phoney.Templates;

namespace Phoney;

/// <summary>
/// Generates fake values for one locale. Cheap to create; not thread-safe (use one per thread, or the
/// thread-safe static <see cref="Fake"/> facade).
/// </summary>
/// <example>
/// <code>
/// var faker = new Faker("sv", seed: 42);
/// string name = faker.Person.FullName();
/// string email = faker.Internet.Email();
/// string text = faker.Parse("{{person.firstName}} lives in {{location.city}}");
/// </code>
/// </example>
public sealed partial class Faker : IFaker
{
    private DateTimeOffset? _referenceDate;
    private bool _seeded;

    /// <summary>
    /// The <see cref="ReferenceDate"/> of seeded fakers and generators that don't set one: a fixed point in time, so
    /// relative dates (past, recent, birthdates, ages) are as reproducible as everything else. 2025-01-01T00:00:00Z.
    /// </summary>
    public static readonly DateTimeOffset DefaultSeededReferenceDate = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a faker for <paramref name="locale"/> (e.g. <c>en</c>, <c>sv</c>, <c>de-AT</c>).</summary>
    /// <param name="locale">Locale code or culture name; see <see cref="Locales.All"/>.</param>
    /// <param name="seed">
    /// Seed for reproducible values; random when <see langword="null"/>. A seeded faker also fixes
    /// <see cref="ReferenceDate"/> to <see cref="DefaultSeededReferenceDate"/> unless you set it or a <see cref="TimeProvider"/>.
    /// </param>
    public Faker(string locale = Locales.Default, long? seed = null)
        : this(LocaleStore.Get(locale), seed is { } s ? new SeededRandom(s) : new SeededRandom())
    {
        _seeded = seed is not null;
    }

    /// <summary>Creates a faker for the locale matching <paramref name="culture"/> (e.g. <c>sv-SE</c> → <c>sv</c>).</summary>
    public Faker(CultureInfo culture, long? seed = null)
        : this(culture?.Name is { Length: > 0 } name ? name : Locales.Default, seed)
    {
    }

    /// <summary>Creates a faker over existing locale data and randomizer.</summary>
    public Faker(LocaleData data, SeededRandom random)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>Whether the faker was seeded, so its values and dates are reproducible.</summary>
    internal bool IsSeeded => _seeded;

    /// <summary>The locale code, e.g. <c>de_AT</c>.</summary>
    public string Locale => Data.Info.Code;

    /// <summary>The merged locale data used by this faker.</summary>
    public LocaleData Data { get; }

    /// <summary>The source of randomness. Use it for custom values so seeding stays reproducible.</summary>
    public SeededRandom Random { get; }

    /// <summary>
    /// The point in time that relative dates (past, future, recent, birthdates…) are computed from: the value you set,
    /// else the current time of <see cref="TimeProvider"/>, else <see cref="DefaultSeededReferenceDate"/> once the faker
    /// is seeded, else the system clock.
    /// </summary>
    public DateTimeOffset ReferenceDate
    {
#pragma warning disable RS0030 // The system clock is the documented fallback of an unseeded faker without a TimeProvider.
        get => _referenceDate ?? TimeProvider?.GetUtcNow() ?? (_seeded ? DefaultSeededReferenceDate : DateTimeOffset.UtcNow);
#pragma warning restore RS0030
        set => _referenceDate = value;
    }

    /// <summary>
    /// The clock that "now" is read from when no <see cref="ReferenceDate"/> is set, e.g. a <c>FakeTimeProvider</c> in tests.
    /// It is read on every use, so advancing it moves the dates. It takes precedence over <see cref="DefaultSeededReferenceDate"/>.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// Restarts the random sequence from <paramref name="seed"/>. Unless a <see cref="ReferenceDate"/> or
    /// <see cref="TimeProvider"/> was set, dates become relative to <see cref="DefaultSeededReferenceDate"/> so they are reproducible too.
    /// </summary>
    public Faker Seed(long seed)
    {
        Random.Reseed(seed);
        _seeded = true;
        return this;
    }

    /// <inheritdoc />
    IFaker IFaker.Seed(long seed) => Seed(seed);

    /// <summary>
    /// Returns a new faker for item <paramref name="index"/> of parallel work: same locale and dates, its own randomness derived from this faker's
    /// seed and the index only. A <see cref="Faker"/> is not thread-safe, so give each task or loop iteration a fork: the data of item <em>n</em> is then
    /// the same whatever the thread timing, and reproducible when this faker is seeded.
    /// </summary>
    /// <example>
    /// <code>
    /// var faker = new Faker(seed: 42);
    /// Parallel.For(0, 1000, i =&gt; Save(faker.Fork(i).Person.FullName()));   // item i is always the same name
    /// </code>
    /// </example>
    /// <remarks>It reads the seed this faker was created or last reseeded with, never its current position, so forking draws nothing and is safe from any thread.</remarks>
    public Faker Fork(long index) => Derived(SeededRandom.DeriveSeed(Random.Seed, index), _seeded);

    /// <inheritdoc />
    IFaker IFaker.Fork(long index) => Fork(index);

    /// <summary>
    /// Returns a faker whose values depend on <paramref name="key"/> only: the same key gives the same person, address and email in every run, process
    /// and machine, whatever this faker's seed. Use it to replace real data with stable stand-ins (<c>faker.Keyed(customer.Email).Person.FullName()</c>),
    /// so the same customer gets the same fake name in every table.
    /// </summary>
    /// <remarks>
    /// The faker keeps this faker's locale, and its dates are relative to <see cref="DefaultSeededReferenceDate"/> unless this faker has a
    /// <see cref="ReferenceDate"/> or <see cref="TimeProvider"/>. Anyone who knows a key can compute its stand-in; to make the mapping secret, put a secret
    /// in the key or use <see cref="ObjectGenerator{T}.Pseudonymize(T, Func{T, string})"/> with a seed you keep private.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public Faker Keyed(string key) => Derived(SeededRandom.KeySeed(key), seeded: true);

    /// <inheritdoc />
    IFaker IFaker.Keyed(string key) => Keyed(key);

    /// <summary>A faker over the same data and dates with its own sequence from <paramref name="seed"/>.</summary>
    private Faker Derived(long seed, bool seeded) =>
        new(Data, new SeededRandom(seed)) { _seeded = seeded, _referenceDate = _referenceDate, TimeProvider = TimeProvider };

    /// <summary>
    /// Evaluates <c>{{...}}</c> expressions in <paramref name="template"/>, faker.js style. An expression is either a
    /// faker.js method (<c>{{person.firstName}}</c>, <c>{{string.numeric(4)}}</c>) or a locale data path
    /// (<c>{{person.last_name.generic}}</c>, <c>{{airline.airport.name}}</c>).
    /// </summary>
    /// <remarks>Only evaluate trusted templates; they can call any registered template function.</remarks>
    public string Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Template.ContainsExpression(template) ? Template.Cached(template).Evaluate(this) : template;
    }

    /// <summary>Picks a random value from the locale data at <paramref name="path"/> and evaluates any templates in it.</summary>
    /// <exception cref="PhoneyDataException">The locale has no such string data.</exception>
    public string Pick(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!KeyRegistry.TryGetId(path, out var key))
            throw new PhoneyDataException($"Unknown data path '{path}'.");
        return Pick(key);
    }

    /// <summary>Like <see cref="Pick(string)"/> but returns <see langword="null"/> when the locale has no data.</summary>
    public string? TryPick(string path) =>
        KeyRegistry.TryGetId(path, out var key) ? TryPick(key) : null;

    /// <summary>Picks a value of a string entry and evaluates its templates.</summary>
    internal string Pick(int key) => Pick(Data.Strings(key));

    /// <summary>Like <see cref="Pick(int)"/>, or <see langword="null"/> when there is no data.</summary>
    internal string? TryPick(int key) => Data.TryStrings(key) is { } entry ? Pick(entry) : null;

    /// <summary>Picks a value (weighted if applicable) and evaluates its templates.</summary>
    internal string Pick(StringsEntry entry) => entry.Resolve(entry.PickIndex(Random), this);

    /// <summary>Like <see cref="Pick(int)"/>, writing the value into <paramref name="text"/> instead of returning it.</summary>
    internal void AppendPick(int key, ref TextBuilder text)
    {
        var entry = Data.Strings(key);
        entry.AppendTo(entry.PickIndex(Random), this, ref text);
    }

    /// <summary>Picks a random record from a records table, e.g. airports.</summary>
    internal (RecordsEntry Table, int Row) PickRecord(int key)
    {
        var table = Data.Records(key);
        return (table, Random.Index(table.Count));
    }
}
