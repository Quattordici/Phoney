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
    private static readonly AsyncLocal<Scope?> s_scoped = new();
    private static string s_locale = Locales.Default;
    private static StrongBox<long>? s_seed; // a reference, so threads read it atomically
    private static TimeProvider? s_timeProvider;
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
    /// The clock relative dates of the facade and of <see cref="For{T}"/> generators are computed from, e.g. a
    /// <c>FakeTimeProvider</c>; <see langword="null"/> (the default) uses the system clock. Changing it affects all threads.
    /// </summary>
    public static TimeProvider? TimeProvider
    {
        get => Volatile.Read(ref s_timeProvider);
        set
        {
            Volatile.Write(ref s_timeProvider, value);
            Interlocked.Increment(ref s_version);
        }
    }

    /// <summary>
    /// The faker behind the facade: the one from an enclosing <see cref="Seeded"/> scope, otherwise the current
    /// thread's (seeded when <see cref="Seed"/> was called).
    /// </summary>
    public static Faker Faker => Scope.Current?.ScopedFaker ?? ThreadFaker;

    /// <summary>Whether the facade currently produces reproducible values (a <see cref="Seeded"/> scope or <see cref="Seed"/>).</summary>
    public static bool IsSeeded => Scope.Current is { } scoped ? scoped.ScopedFaker.IsSeeded : Volatile.Read(ref s_seed) is not null;

    private static Faker ThreadFaker
    {
        get
        {
            var faker = t_faker;
            if (faker is null || t_version != Volatile.Read(ref s_version))
            {
                t_version = Volatile.Read(ref s_version);
                faker = t_faker = new Faker(s_locale, Volatile.Read(ref s_seed)?.Value) { TimeProvider = TimeProvider };
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
    /// Makes <c>Fake.*</c>, <see cref="One{T}"/>, <see cref="Many{T}(int)"/> and <see cref="For{T}"/> reproducible until the
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
    /// <remarks>
    /// The scope's faker flows into tasks and <c>Parallel.For</c> workers started inside it, and a <see cref="Phoney.Faker"/> is not thread-safe:
    /// give each parallel item its own with <see cref="Fork"/>, or generate objects with <see cref="ObjectGenerator{T}.GenerateParallel"/>.
    /// </remarks>
    /// <param name="seed">The seed.</param>
    /// <param name="locale">Locale for the scope; defaults to <see cref="Locale"/>.</param>
    public static IDisposable Seeded(long seed, string? locale = null)
    {
        return Scope.Open(new Faker(locale ?? s_locale, seed) { TimeProvider = TimeProvider });
    }

    /// <summary>
    /// Gives the current async flow its own faker for item <paramref name="index"/> of parallel work, until the returned scope is disposed: a fork of
    /// <see cref="Faker"/> (see <see cref="Phoney.Faker.Fork"/>). Inside a <see cref="Seeded"/> scope every task and <c>Parallel.For</c> worker would
    /// otherwise share the scope's one faker, which is not thread-safe; with a fork per item, item <em>n</em> gets the same data whatever the thread timing.
    /// </summary>
    /// <example>
    /// <code>
    /// using var _ = Fake.Seeded(42);
    /// Parallel.For(0, 1000, i =&gt;
    /// {
    ///     using var fork = Fake.Fork(i);
    ///     Save(Fake.Person.FullName(), Fake.One&lt;Order&gt;());   // the same for item i on every run
    /// });
    /// </code>
    /// </example>
    public static IDisposable Fork(long index)
    {
        return Scope.Open(Faker.Fork(index));
    }

    /// <summary>
    /// A faker whose values depend on <paramref name="key"/> only (see <see cref="Phoney.Faker.Keyed"/>), in the facade's <see cref="Locale"/>:
    /// <c>Fake.Keyed("customer:42").Person.FullName()</c> is the same name in every run, process and machine.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static Faker Keyed(string key) => Faker.Keyed(key);

    /// <summary>
    /// A <see cref="Seeded"/> or <see cref="Fork"/> scope: its faker is the facade's while it is the innermost open scope of the async flow. Disposing it goes back to the
    /// nearest enclosing scope that is still open, so scopes disposed out of order never bring back one that has ended, and an outer scope disposed too early leaves
    /// the inner one in charge until that one ends too.
    /// </summary>
    private sealed class Scope : IDisposable
    {
        private readonly Scope? _outer;
        private volatile bool _disposed;

        private Scope(Faker faker, Scope? outer) => (ScopedFaker, _outer) = (faker, outer);

        /// <summary>The scope's faker.</summary>
        public Faker ScopedFaker { get; }

        /// <summary>
        /// The innermost scope of this async flow that is still open. A task started inside a scope keeps a copy of it, so the copy can be one its creator has
        /// disposed since; that one is skipped like any other ended scope.
        /// </summary>
        public static Scope? Current => OpenFrom(s_scoped.Value);

        /// <summary>Makes <paramref name="faker"/> the facade's faker of this async flow until the returned scope is disposed.</summary>
        public static Scope Open(Faker faker)
        {
            var scope = new Scope(faker, Current);
            s_scoped.Value = scope;
            return scope;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!ReferenceEquals(s_scoped.Value, this))
                return; // disposed before a scope opened inside it: that one stays in charge and skips this one when it ends
            s_scoped.Value = OpenFrom(_outer);
        }

        /// <summary><paramref name="scope"/>, or the nearest scope around it, that is not disposed.</summary>
        private static Scope? OpenFrom(Scope? scope)
        {
            while (scope is { _disposed: true })
                scope = scope._outer;
            return scope;
        }
    }

    /// <summary>Source of randomness of the current thread's faker.</summary>
    public static SeededRandom Random => Faker.Random;

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

    /// <inheritdoc cref="Faker.NationalId"/>
    public static NationalIdModule NationalId => Faker.NationalId;

    /// <inheritdoc cref="Faker.Parse"/>
    public static string Parse(string template) => Faker.Parse(template);
}
