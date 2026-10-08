using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>Calls every locale-dependent generator in every locale to catch missing data and broken templates.</summary>
public sealed class SmokeTests
{
    /// <summary>Generators that depend on locale data, keyed by name for readable failures.</summary>
    private static readonly (string Name, Func<Faker, object> Generate)[] Generators =
    [
        ("person.firstName", f => f.Person.FirstName()),
        ("person.firstName(female)", f => f.Person.FirstName(Sex.Female)),
        ("person.lastName(male)", f => f.Person.LastName(Sex.Male)),
        ("person.middleName", f => f.Person.MiddleName()),
        ("person.fullName", f => f.Person.FullName()),
        ("person.gender", f => f.Person.Gender()),
        ("person.sex", f => f.Person.Sex()),
        ("person.bio", f => f.Person.Bio()),
        ("person.prefix", f => f.Person.Prefix()),
        ("person.suffix", f => f.Person.Suffix()),
        ("person.jobTitle", f => f.Person.JobTitle()),
        ("person.zodiacSign", f => f.Person.ZodiacSign()),
        ("location.zipCode", f => f.Location.ZipCode()),
        ("location.city", f => f.Location.City()),
        ("location.buildingNumber", f => f.Location.BuildingNumber()),
        ("location.street", f => f.Location.Street()),
        ("location.streetAddress", f => f.Location.StreetAddress(useFullAddress: true)),
        ("location.postalAddress", f => f.Location.PostalAddress()),
        ("location.secondaryAddress", f => f.Location.SecondaryAddress()),
        ("location.county", f => f.Location.County()),
        ("location.country", f => f.Location.Country()),
        ("location.countryCode", f => f.Location.CountryCode(CountryCodeFormat.Alpha3)),
        ("location.state", f => f.Location.State()),
        ("location.direction", f => f.Location.Direction(abbreviated: true)),
        ("location.timeZone", f => f.Location.TimeZone()),
        ("location.language", f => f.Location.Language()),
        ("internet.email", f => f.Internet.Email()),
        ("internet.exampleEmail", f => f.Internet.ExampleEmail()),
        ("internet.username", f => f.Internet.Username()),
        ("internet.url", f => f.Internet.Url()),
        ("internet.userAgent", f => f.Internet.UserAgent()),
        ("internet.emoji", f => f.Internet.Emoji()),
        ("internet.httpStatusCode", f => f.Internet.HttpStatusCode()),
        ("internet.jwt", f => f.Internet.Jwt()),
        ("phone.number", f => f.Phone.Number()),
        ("phone.mobile", f => f.Phone.Number(PhoneStyle.Mobile)),
        ("company.name", f => f.Company.Name()),
        ("company.catchPhrase", f => f.Company.CatchPhrase()),
        ("company.buzzPhrase", f => f.Company.BuzzPhrase()),
        ("commerce.productName", f => f.Commerce.ProductName()),
        ("commerce.productDescription", f => f.Commerce.ProductDescription()),
        ("commerce.department", f => f.Commerce.Department()),
        ("finance.accountName", f => f.Finance.AccountName()),
        ("finance.transactionDescription", f => f.Finance.TransactionDescription()),
        ("finance.currency", f => f.Finance.Currency()),
        ("finance.creditCardNumber", f => f.Finance.CreditCardNumber()),
        ("finance.routingNumber", f => f.Finance.RoutingNumber()),
        ("date.month", f => f.Date.Month(abbreviated: true, context: true)),
        ("date.weekday", f => f.Date.Weekday()),
        ("date.timeZone", f => f.Date.TimeZone()),
        ("lorem.paragraph", f => f.Lorem.Paragraph()),
        ("word.words", f => f.Word.Words(5)),
        ("color.human", f => f.Color.Human()),
        ("animal.type", f => f.Animal.Type()),
        ("animal.petName", f => f.Animal.PetName()),
        ("book.title", f => f.Book.Title()),
        ("music.songName", f => f.Music.SongName()),
        ("food.dish", f => f.Food.Dish()),
        ("food.description", f => f.Food.Description()),
        ("hacker.phrase", f => f.Hacker.Phrase()),
        ("vehicle.vehicle", f => f.Vehicle.Vehicle()),
        ("airline.airport", f => f.Airline.Airport()),
        ("science.chemicalElement", f => f.Science.ChemicalElement()),
        ("database.column", f => f.Database.Column()),
        ("system.filePath", f => f.System.FilePath()),
        ("system.commonFileName", f => f.System.CommonFileName()),
        ("git.commitEntry", f => f.Git.CommitEntry()),
    ];

    [Theory]
    [ClassData(typeof(AllLocales))]
    public void Every_generator_works_in_every_locale(string locale)
    {
        var faker = new Faker(locale, seed: 1);
        var failures = new List<string>();
        foreach (var (name, generate) in Generators)
        {
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    var value = generate(faker)?.ToString();
                    if (string.IsNullOrWhiteSpace(value) || value.Contains("{{", StringComparison.Ordinal))
                    {
                        failures.Add($"{name}: '{value}'");
                        break;
                    }
                }
                catch (PhoneyDataException ex) when (ex.Message.Contains("isn't applicable", StringComparison.Ordinal))
                {
                    break; // faker.js marks this data as not applicable to the locale
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                    break;
                }
            }
        }

        failures.ShouldBeEmpty($"{locale} failures:\n{string.Join("\n", failures)}");
    }
}
