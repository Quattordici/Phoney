using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>Values for parameter lists (the building block of test framework integrations such as Phoney.TUnit).</summary>
public sealed class ArgumentGeneratorTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    // Parameter lists to generate for; the methods are never called.
    private static void Person(string firstName, string lastName, string email, DateOnly birthDate, int age, string city, string zipCode, DateTime createdAt, DateTime updatedAt) { }

    private static void Annotated([Range(1, 5)] int quantity, [EmailAddress] string contact, [StringLength(6, MinimumLength = 6)] string code, [AllowedValues("a", "b")] string letter) { }

    private static void Objects(Customer customer, Member member, List<string> tags, Order[] orders) { }

    private static void Nullables(string? nickname, int? score, [Required] string? required) { }

    private static void Self(ArgumentGeneratorTests tests) { }

    private static ArgumentGenerator For(string method) =>
        Fake.Arguments(typeof(ArgumentGeneratorTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!);

    [Test]
    public void Arguments_of_a_row_describe_the_same_person()
    {
        foreach (var row in For(nameof(Person)).Seed(1).ReferenceDate(Reference).Locale("de_AT").Generate(50))
        {
            var (firstName, lastName, email) = ((string)row[0]!, (string)row[1]!, (string)row[2]!);
            var local = email[..email.IndexOf('@')];
            (local.Contains(Ascii(firstName), StringComparison.OrdinalIgnoreCase) ||
             local.Contains(Ascii(lastName), StringComparison.OrdinalIgnoreCase)).ShouldBeTrue($"{firstName} {lastName} <{email}>");

            var (birthDate, age) = ((DateOnly)row[3]!, (int)row[4]!);
            var today = DateOnly.FromDateTime(Reference.UtcDateTime);
            age.ShouldBe(today.Year - birthDate.Year - (today < birthDate.AddYears(today.Year - birthDate.Year) ? 1 : 0));

            ((string)row[5]!).ShouldNotBeNullOrWhiteSpace();
            ((string)row[6]!).ShouldMatch(@"^\d{4}$"); // an Austrian zip code
            ((DateTime)row[7]!).ShouldBeLessThanOrEqualTo((DateTime)row[8]!);
        }

        static string Ascii(string s) => new(s.Normalize(System.Text.NormalizationForm.FormKD).Where(char.IsAsciiLetter).ToArray());
    }

    [Test]
    public void Data_annotations_on_parameters_are_honoured()
    {
        foreach (var row in For(nameof(Annotated)).Seed(2).Generate(100))
        {
            ((int)row[0]!).ShouldBeInRange(1, 5);
            new EmailAddressAttribute().IsValid(row[1]).ShouldBeTrue();
            ((string)row[2]!).Length.ShouldBe(6);
            ((string)row[3]!).ShouldBeOneOf("a", "b");
        }
    }

    [Test]
    public void Seeded_rows_are_reproducible_and_distinct()
    {
        var first = For(nameof(Person)).Seed(3).Generate(5);
        var second = For(nameof(Person)).Seed(3).Generate(5);
        second.ShouldBe(first);
        first.Select(r => r[2]).Distinct().Count().ShouldBe(5);

        // Row(n) recreates row n on demand without advancing the sequence.
        For(nameof(Person)).Seed(3).Row(4).ShouldBe(first[4]);
    }

    [Test]
    public void Complex_types_use_generated_and_reflection_models()
    {
        var row = For(nameof(Objects)).Seed(4).Generate();

        var customer = row[0].ShouldBeOfType<Customer>(); // reflection model
        customer.Email.ShouldContain("@");
        row[1].ShouldBeOfType<Member>().FirstName.ShouldNotBeNullOrWhiteSpace(); // [FakeFor<Member>] model
        row[2].ShouldBeOfType<List<string>>().ShouldNotBeEmpty();
        row[3].ShouldBeOfType<Order[]>().ShouldNotBeEmpty();
    }

    [Test]
    public void Nullable_arguments_follow_the_null_probability()
    {
        var never = For(nameof(Nullables)).Seed(5).Generate(50);
        never.ShouldAllBe(r => r[0] != null && r[1] != null);

        var always = For(nameof(Nullables)).Seed(5).NullProbability(1).Generate(50);
        always.ShouldAllBe(r => r[0] == null && r[1] == null && r[2] != null); // [Required] is never null
    }

    [Test]
    public void An_argument_of_the_declaring_type_is_not_a_cycle() =>
        For(nameof(Self)).Seed(6).Generate()[0].ShouldBeOfType<ArgumentGeneratorTests>();

    [Test]
    public void Arguments_can_be_described_without_reflection()
    {
        var row = Fake.Arguments([new FakeArgument("email", typeof(string)), new FakeArgument("Quantity", typeof(int))]).Seed(7).Generate();
        ((string)row[0]!).ShouldContain("@");
        row[1].ShouldBeOfType<int>();
    }
}
