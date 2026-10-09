using System.Runtime.CompilerServices;
using Phoney.Modules;

namespace Phoney;

/// <summary>
/// The quickest way to get fake data: thread-safe static access to a per-thread <see cref="Faker"/>.
/// </summary>
/// <example>
/// <code>
/// string name = Fake.Person.FullName();
/// string city = Fake.Location.City();
/// Fake.Locale = "sv";                 // switch the default locale for all threads
///
/// using var _ = Fake.Seeded(42);      // reproducible Fake.* and Fake.One/Many/For in this test
/// </code>
/// </example>
public static partial class Fake
{
    private static readonly AsyncLocal<Faker?> s_scoped = new();
    private static string s_locale = Locales.Default;
    private static StrongBox<long>? s_seed; // a reference, so threads read it atomically
    private static int s_version;

    [ThreadStatic]
    private static Faker? t_faker;

    [ThreadStatic]
    private static int t_version;

    /// <summary>
    /// Locale used by the static facade and by generators without an explicit locale. Changing it affects all threads.
    /// </summary>
    public static string Locale
    {
        get => s_locale;
        set
        {
            s_locale = Locales.Get(value).Code; // validates and normalizes
            Interlocked.Increment(ref s_version);
        }
    }

    /// <summary>
    /// The faker behind the facade: the one from an enclosing <see cref="Seeded"/> scope, otherwise the current
    /// thread's (seeded when <see cref="Seed"/> was called).
    /// </summary>
    public static Faker Faker => s_scoped.Value ?? ThreadFaker;

    /// <summary>Whether the facade currently produces reproducible values (a <see cref="Seeded"/> scope or <see cref="Seed"/>).</summary>
    public static bool IsSeeded => s_scoped.Value is not null || Volatile.Read(ref s_seed) is not null;

    private static Faker ThreadFaker
    {
        get
        {
            var faker = t_faker;
            if (faker is null || t_version != Volatile.Read(ref s_version))
            {
                t_version = Volatile.Read(ref s_version);
                faker = t_faker = new Faker(s_locale, Volatile.Read(ref s_seed)?.Value);
            }

            return faker;
        }
    }

    /// <summary>
    /// Seeds the facade for the whole app, or makes it random again with <see langword="null"/>. Each thread restarts
    /// the same sequence, so single-threaded code becomes reproducible. For parallel tests prefer <see cref="Seeded"/>.
    /// </summary>
    public static void Seed(long? seed)
    {
        Volatile.Write(ref s_seed, seed is { } value ? new StrongBox<long>(value) : null);
        Interlocked.Increment(ref s_version);
    }

    /// <summary>
    /// Makes <c>Fake.*</c>, <see cref="One{T}"/>, <see cref="Many{T}"/> and <see cref="For{T}"/> reproducible until the
    /// returned scope is disposed. The scope follows the current async flow, so parallel tests don't affect each other.
    /// </summary>
    /// <example>
    /// <code>
    /// [Fact]
    /// public void Test()
    /// {
    ///     using var _ = Fake.Seeded(42);
    ///     var customer = Fake.One&lt;Customer&gt;();   // the same customer on every run
    /// }
    /// </code>
    /// </example>
    /// <param name="seed">The seed.</param>
    /// <param name="locale">Locale for the scope; defaults to <see cref="Locale"/>.</param>
    public static IDisposable Seeded(long seed, string? locale = null)
    {
        var previous = s_scoped.Value;
        s_scoped.Value = new Faker(locale ?? s_locale, seed);
        return new Scope(previous);
    }

    /// <summary>Restores the facade's previous faker when a <see cref="Seeded"/> scope ends.</summary>
    private sealed class Scope(Faker? previous) : IDisposable
    {
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            s_scoped.Value = previous;
        }
    }

    /// <summary>Source of randomness of the current thread's faker.</summary>
    public static Randomizer Random => Faker.Random;

    /// <inheritdoc cref="Faker.Helpers"/>
    public static HelpersModule Helpers => Faker.Helpers;

    /// <inheritdoc cref="Faker.Number"/>
    public static NumberModule Number => Faker.Number;

    /// <inheritdoc cref="Faker.String"/>
    public static StringModule String => Faker.String;

    /// <inheritdoc cref="Faker.Person"/>
    public static PersonModule Person => Faker.Person;

    /// <inheritdoc cref="Faker.Location"/>
    public static LocationModule Location => Faker.Location;

    /// <inheritdoc cref="Faker.Internet"/>
    public static InternetModule Internet => Faker.Internet;

    /// <inheritdoc cref="Faker.Phone"/>
    public static PhoneModule Phone => Faker.Phone;

    /// <inheritdoc cref="Faker.Company"/>
    public static CompanyModule Company => Faker.Company;

    /// <inheritdoc cref="Faker.Commerce"/>
    public static CommerceModule Commerce => Faker.Commerce;

    /// <inheritdoc cref="Faker.Finance"/>
    public static FinanceModule Finance => Faker.Finance;

    /// <inheritdoc cref="Faker.Date"/>
    public static DateModule Date => Faker.Date;

    /// <inheritdoc cref="Faker.Lorem"/>
    public static LoremModule Lorem => Faker.Lorem;

    /// <inheritdoc cref="Faker.Word"/>
    public static WordModule Word => Faker.Word;

    /// <inheritdoc cref="Faker.Color"/>
    public static ColorModule Color => Faker.Color;

    /// <inheritdoc cref="Faker.Animal"/>
    public static AnimalModule Animal => Faker.Animal;

    /// <inheritdoc cref="Faker.Book"/>
    public static BookModule Book => Faker.Book;

    /// <inheritdoc cref="Faker.Music"/>
    public static MusicModule Music => Faker.Music;

    /// <inheritdoc cref="Faker.Food"/>
    public static FoodModule Food => Faker.Food;

    /// <inheritdoc cref="Faker.Hacker"/>
    public static HackerModule Hacker => Faker.Hacker;

    /// <inheritdoc cref="Faker.Vehicle"/>
    public static VehicleModule Vehicle => Faker.Vehicle;

    /// <inheritdoc cref="Faker.Airline"/>
    public static AirlineModule Airline => Faker.Airline;

    /// <inheritdoc cref="Faker.Science"/>
    public static ScienceModule Science => Faker.Science;

    /// <inheritdoc cref="Faker.Database"/>
    public static DatabaseModule Database => Faker.Database;

    /// <inheritdoc cref="Faker.System"/>
    public static SystemModule System => Faker.System;

    /// <inheritdoc cref="Faker.Git"/>
    public static GitModule Git => Faker.Git;

    /// <inheritdoc cref="Faker.Image"/>
    public static ImageModule Image => Faker.Image;

    /// <inheritdoc cref="Faker.Parse"/>
    public static string Parse(string template) => Faker.Parse(template);
}
