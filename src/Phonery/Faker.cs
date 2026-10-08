using System.Globalization;
using Phonery.Data;
using Phonery.Templates;

namespace Phonery;

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
public sealed partial class Faker
{
    private DateTimeOffset? _referenceDate;

    /// <summary>Creates a faker for <paramref name="locale"/> (e.g. <c>en</c>, <c>sv</c>, <c>de-AT</c>).</summary>
    /// <param name="locale">Locale code or culture name; see <see cref="Locales.All"/>.</param>
    /// <param name="seed">Seed for reproducible values; random when <see langword="null"/>.</param>
    public Faker(string locale = Locales.Default, long? seed = null)
        : this(LocaleStore.Get(locale), seed is { } s ? new Randomizer(s) : new Randomizer())
    {
    }

    /// <summary>Creates a faker for the locale matching <paramref name="culture"/> (e.g. <c>sv-SE</c> → <c>sv</c>).</summary>
    public Faker(CultureInfo culture, long? seed = null)
        : this(culture?.Name is { Length: > 0 } name ? name : Locales.Default, seed)
    {
    }

    /// <summary>Creates a faker over existing locale data and randomizer.</summary>
    public Faker(LocaleData data, Randomizer random)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>The locale code, e.g. <c>de_AT</c>.</summary>
    public string Locale => Data.Info.Code;

    /// <summary>The merged locale data used by this faker.</summary>
    public LocaleData Data { get; }

    /// <summary>The source of randomness. Use it for custom values so seeding stays reproducible.</summary>
    public Randomizer Random { get; }

    /// <summary>
    /// The point in time that relative dates (past, future, recent, birthdates…) are computed from.
    /// Defaults to the current time; set it together with a seed for fully reproducible dates.
    /// </summary>
    public DateTimeOffset ReferenceDate
    {
        get => _referenceDate ?? DateTimeOffset.UtcNow;
        set => _referenceDate = value;
    }

    /// <summary>Restarts the random sequence from <paramref name="seed"/>.</summary>
    public Faker Seed(long seed)
    {
        Random.Reseed(seed);
        return this;
    }

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
    /// <exception cref="PhoneryDataException">The locale has no such string data.</exception>
    public string Pick(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!KeyRegistry.TryGetId(path, out var key))
            throw new PhoneryDataException($"Unknown data path '{path}'.");
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

    /// <summary>Picks a random record from a records table, e.g. airports.</summary>
    internal (RecordsEntry Table, int Row) PickRecord(int key)
    {
        var table = Data.Records(key);
        return (table, Random.Index(table.Count));
    }
}
