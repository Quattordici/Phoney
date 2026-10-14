using System.ComponentModel.DataAnnotations;

namespace Phoney.TUnit.Tests;

public sealed class Signup
{
    [Range(1, 10)] public int Guests { get; set; }
    [StringLength(20, MinimumLength = 3)] public string Name { get; set; } = "";
    [EmailAddress] public string Email { get; set; } = "";
}

/// <summary>Every violation and every edge as its own test case, named after what it is.</summary>
public sealed class InvalidCasesAsTestDataTests
{
    public static IEnumerable<Func<ConstraintCase<Signup>>> Violations() =>
        Fake.For<Signup>().Seed(1).InvalidCases().Select(c => (Func<ConstraintCase<Signup>>)(() => c));

    public static IEnumerable<Func<ConstraintCase<Signup>>> Edges() =>
        Fake.For<Signup>().Seed(1).BoundaryCases().Select(c => (Func<ConstraintCase<Signup>>)(() => c));

    private static bool IsValid(Signup signup) =>
        Validator.TryValidateObject(signup, new ValidationContext(signup), null, validateAllProperties: true);

    [Test, MethodDataSource(nameof(Violations))]
    public void Validation_rejects(ConstraintCase<Signup> violation) =>
        IsValid(violation.Value).ShouldBeFalse(violation.ToString());

    [Test, MethodDataSource(nameof(Edges))]
    public void Validation_accepts(ConstraintCase<Signup> edge) =>
        IsValid(edge.Value).ShouldBeTrue(edge.ToString());

    [Test]
    public void There_is_a_test_case_per_violation_and_per_edge()
    {
        Violations().Count().ShouldBe(5); // Guests twice, Name twice, Email once
        Edges().Count().ShouldBe(4);      // Guests twice, Name twice
    }
}
