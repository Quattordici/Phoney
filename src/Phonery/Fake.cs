using Phonery.Modules;

namespace Phonery;

/// <summary>
/// The quickest way to get fake data: thread-safe static access to a per-thread <see cref="Faker"/>.
/// </summary>
/// <example>
/// <code>
/// string name = Fake.Person.FullName();
/// string city = Fake.Location.City();
/// Fake.Locale = "sv";          // switch the default locale for all threads
/// </code>
/// </example>
public static partial class Fake
{
    private static string s_locale = Locales.Default;
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

    /// <summary>The current thread's faker. Not seeded; use <c>new Faker(seed: …)</c> for reproducible values.</summary>
    public static Faker Faker
    {
        get
        {
            var faker = t_faker;
            if (faker is null || t_version != Volatile.Read(ref s_version))
            {
                t_version = Volatile.Read(ref s_version);
                faker = t_faker = new Faker(s_locale);
            }

            return faker;
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
