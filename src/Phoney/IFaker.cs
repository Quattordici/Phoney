using Phoney.Data;
using Phoney.Modules;

namespace Phoney;

/// <summary>
/// The members of <see cref="Faker"/> as an interface, for dependency injection and test doubles
/// (<c>services.AddPhoney()</c> in the <c>Phoney.DependencyInjection</c> package registers it).
/// </summary>
public interface IFaker
{
    /// <inheritdoc cref="Faker.Locale"/>
    string Locale { get; }

    /// <inheritdoc cref="Faker.Data"/>
    LocaleData Data { get; }

    /// <inheritdoc cref="Faker.Random"/>
    SeededRandom Random { get; }

    /// <inheritdoc cref="Faker.ReferenceDate"/>
    DateTimeOffset ReferenceDate { get; set; }

    /// <inheritdoc cref="Faker.TimeProvider"/>
    TimeProvider? TimeProvider { get; set; }

    /// <inheritdoc cref="Faker.Helpers"/>
    HelpersModule Helpers { get; }

    /// <inheritdoc cref="Faker.Number"/>
    NumberModule Number { get; }

    /// <inheritdoc cref="Faker.String"/>
    StringModule String { get; }

    /// <inheritdoc cref="Faker.Person"/>
    PersonModule Person { get; }

    /// <inheritdoc cref="Faker.Location"/>
    LocationModule Location { get; }

    /// <inheritdoc cref="Faker.Internet"/>
    InternetModule Internet { get; }

    /// <inheritdoc cref="Faker.Phone"/>
    PhoneModule Phone { get; }

    /// <inheritdoc cref="Faker.Company"/>
    CompanyModule Company { get; }

    /// <inheritdoc cref="Faker.Commerce"/>
    CommerceModule Commerce { get; }

    /// <inheritdoc cref="Faker.Finance"/>
    FinanceModule Finance { get; }

    /// <inheritdoc cref="Faker.Date"/>
    DateModule Date { get; }

    /// <inheritdoc cref="Faker.Lorem"/>
    LoremModule Lorem { get; }

    /// <inheritdoc cref="Faker.Word"/>
    WordModule Word { get; }

    /// <inheritdoc cref="Faker.Color"/>
    ColorModule Color { get; }

    /// <inheritdoc cref="Faker.Animal"/>
    AnimalModule Animal { get; }

    /// <inheritdoc cref="Faker.Book"/>
    BookModule Book { get; }

    /// <inheritdoc cref="Faker.Music"/>
    MusicModule Music { get; }

    /// <inheritdoc cref="Faker.Food"/>
    FoodModule Food { get; }

    /// <inheritdoc cref="Faker.Hacker"/>
    HackerModule Hacker { get; }

    /// <inheritdoc cref="Faker.Vehicle"/>
    VehicleModule Vehicle { get; }

    /// <inheritdoc cref="Faker.Airline"/>
    AirlineModule Airline { get; }

    /// <inheritdoc cref="Faker.Science"/>
    ScienceModule Science { get; }

    /// <inheritdoc cref="Faker.Database"/>
    DatabaseModule Database { get; }

    /// <inheritdoc cref="Faker.System"/>
    SystemModule System { get; }

    /// <inheritdoc cref="Faker.Git"/>
    GitModule Git { get; }

    /// <inheritdoc cref="Faker.Image"/>
    ImageModule Image { get; }

    /// <inheritdoc cref="Faker.NationalId"/>
    NationalIdModule NationalId { get; }

    /// <inheritdoc cref="Faker.Seed"/>
    IFaker Seed(long seed);

    /// <inheritdoc cref="Faker.Parse"/>
    string Parse(string template);

    /// <inheritdoc cref="Faker.Pick(string)"/>
    string Pick(string path);

    /// <inheritdoc cref="Faker.TryPick(string)"/>
    string? TryPick(string path);
}
