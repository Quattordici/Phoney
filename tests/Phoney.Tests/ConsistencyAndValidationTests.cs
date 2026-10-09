using System.ComponentModel.DataAnnotations;
using Phoney.Generation;
using Phoney.Modules;

namespace Phoney.Tests;

public sealed class Account
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public int Age { get; set; }
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public DateTime UpdatedAt { get; set; } // declared before CreatedAt on purpose
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public enum Plan
{
    Free,
    Pro,
    Enterprise,
}

/// <summary>Every supported annotation; generated instances must pass <see cref="Validator"/>.</summary>
public sealed class SignUp
{
    [Required, StringLength(12, MinimumLength = 3)]
    public string Username { get; set; } = "";

    [Required, MaxLength(8)]
    public string FirstName { get; set; } = "";

    [EmailAddress, StringLength(60)]
    public string Contact { get; set; } = "";

    [Phone]
    public string? Mobile { get; set; }

    [Url]
    public string? Homepage { get; set; }

    [CreditCard]
    public string Card { get; set; } = "";

    [RegularExpression("^[A-Z]{2}-\\d{4}$")]
    public string Code { get; set; } = "";

    [Range(18, 25)]
    public int Age { get; set; }

    [Range(0.5, 2.5)]
    public double Factor { get; set; }

    [Range(typeof(decimal), "10", "20")]
    public decimal Price { get; set; }

    [Range(typeof(DateTime), "2020-01-01", "2020-12-31")]
    public DateTime Joined { get; set; }

    [AllowedValues("red", "green")]
    public string Color { get; set; } = "";

    [AllowedValues(Plan.Pro, Plan.Enterprise)]
    public Plan Plan { get; set; }

    [DeniedValues(0)]
    [Range(0, 1)]
    public int Flag { get; set; }

    [Base64String, MaxLength(40)]
    public string Token { get; set; } = "";

    [Length(2, 2)]
    public List<string> Tags { get; set; } = [];

    [Required]
    public string? Nickname { get; set; }

    [MinLength(30)]
    public string Bio { get; set; } = "";
}

public sealed record Invoice(Guid Id, int CustomerId, [property: Range(1, 5)] int Priority);

[FakeFor<SignUp>]
[FakeFor<Account>]
public static partial class ValidationFakes;

public sealed class ConsistencyAndValidationTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Paths => new() { "reflection", "generated" };

    [Theory]
    [MemberData(nameof(Paths))]
    public void Values_within_an_object_agree(string path)
    {
        var generator = path == "generated"
            ? ValidationFakes.Account
            : new Generator<Account>(new ReflectionModel<Account>());

        foreach (var a in generator.Seed(1).ReferenceDate(Reference).Locale("de_AT").NullProbability(0).Generate(50))
        {
            // Age matches the birthdate.
            var today = DateOnly.FromDateTime(Reference.UtcDateTime);
            var expectedAge = today.Year - a.DateOfBirth.Year - (today < a.DateOfBirth.AddYears(today.Year - a.DateOfBirth.Year) ? 1 : 0);
            a.Age.ShouldBe(expectedAge);

            // One address in the locale's country.
            a.Country.ShouldBe("Österreich");
            a.CountryCode.ShouldBe("AT");
            a.City.ShouldNotBeNullOrWhiteSpace();
            a.ZipCode.ShouldMatch(@"^\d{4}$");

            // Timeline: created ≤ updated ≤ now < expires.
            a.CreatedAt.ShouldBeLessThanOrEqualTo(a.UpdatedAt);
            a.UpdatedAt.ShouldBeLessThanOrEqualTo(Reference.UtcDateTime);
            a.ExpiresAt.ShouldBeGreaterThan(Reference);
        }
    }

    [Fact]
    public void Generated_and_reflection_models_agree_with_annotations_and_consistency()
    {
        var generated = ValidationFakes.SignUp.Seed(3).Generate(20);
        var reflected = new Generator<SignUp>(new ReflectionModel<SignUp>()).Seed(3).Generate(20);
        reflected.Select(Describe).ShouldBe(generated.Select(Describe));

        var generatedAccounts = ValidationFakes.Account.Seed(3).Generate(20);
        var reflectedAccounts = new Generator<Account>(new ReflectionModel<Account>()).Seed(3).Generate(20);
        reflectedAccounts.Select(a => (a.Age, a.DateOfBirth, a.City, a.CreatedAt, a.UpdatedAt))
            .ShouldBe(generatedAccounts.Select(a => (a.Age, a.DateOfBirth, a.City, a.CreatedAt, a.UpdatedAt)));

        static string Describe(SignUp s) =>
            string.Join("|", s.Username, s.FirstName, s.Contact, s.Mobile, s.Homepage, s.Card, s.Code, s.Age, s.Factor, s.Price,
                s.Joined.ToString("O"), s.Color, s.Plan, s.Flag, s.Token, string.Join(",", s.Tags), s.Nickname, s.Bio);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Generated_objects_pass_their_data_annotations(string path)
    {
        var generator = path == "generated"
            ? ValidationFakes.SignUp
            : new Generator<SignUp>(new ReflectionModel<SignUp>());

        foreach (var signUp in generator.Seed(5).NullProbability(0.5).Generate(200))
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(signUp, new ValidationContext(signUp), results, validateAllProperties: true)
                .ShouldBeTrue(string.Join("; ", results.Select(r => $"{string.Join(",", r.MemberNames)}: {r.ErrorMessage}")));

            signUp.Color.ShouldBeOneOf("red", "green");
            signUp.Plan.ShouldNotBe(Plan.Free);
            signUp.Flag.ShouldBe(1);
            signUp.Tags.Count.ShouldBe(2);
            signUp.Nickname.ShouldNotBeNull();
            HelpersModule.LuhnCheck(signUp.Card).ShouldBeTrue();
        }
    }

    [Fact]
    public void Annotations_on_record_parameters_are_honoured() =>
        Fake.For<Invoice>().Generate(50).ShouldAllBe(i => i.Priority >= 1 && i.Priority <= 5);

    [Fact]
    public void Annotations_count_as_covered_in_strict_mode()
    {
        // Contact has no name convention but [EmailAddress]; Code has [RegularExpression]; neither needs a rule.
        Should.NotThrow(() => Fake.For<SignUp>().Strict().Generate());
    }

    [Fact]
    public void WithOneOf_links_object_graphs()
    {
        var customers = Fake.For<Customer>().Generate(5);
        var orders = Fake.For<Order>()
            .WithOneOf(o => o.CustomerId, customers, c => c.Id)
            .WithOneOf(o => o.Customer, customers)
            .Generate(50);

        orders.ShouldAllBe(o => customers.Any(c => c.Id == o.CustomerId));
        orders.ShouldAllBe(o => customers.Contains(o.Customer!));
        Should.Throw<ArgumentException>(() => Fake.For<Order>().WithOneOf(o => o.CustomerId, Array.Empty<int>()));
    }

    [Fact]
    public void Address_profile_names_the_locales_country()
    {
        var address = new Faker("de_AT", seed: 1).Location.Address();
        address.Country.ShouldBe("Österreich");
        address.CountryCode.ShouldBe("AT");
        address.ToString().ShouldEndWith(", Österreich");

        var swedish = new Faker("sv", seed: 1).Location.Address();
        swedish.Country.ShouldBeNull(); // language-wide locale: no country
    }
}
