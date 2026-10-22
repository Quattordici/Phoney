using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Phoney.Generation;

namespace Phoney.MSTest.Tests;

/// <summary>[FakeCheck] runs a test once per generated row of its parameters and shrinks a failing row.</summary>
[TestClass]
public sealed class FakeCheckTests
{
    [FakeCheck(Count = 25)]
    public void Runs_the_test_on_rows_of_its_parameters(string email, [Range(1, 10)] int quantity)
    {
        email.ShouldContain('@');
        quantity.ShouldBeInRange(1, 10);
    }

    [FakeCheck(Count = 10)]
    public async Task Asynchronous_tests_are_checked_too(Customer customer)
    {
        await Task.Yield();
        customer.Email.ShouldNotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task The_test_runs_once_per_row_with_the_rows_of_the_seed()
    {
        var method = new RecordingMethod(_ => true);

        var results = await new FakeCheckAttribute { Count = 5, Seed = 3 }.ExecuteAsync(method);

        results.ShouldHaveSingleItem().Outcome.ShouldBe(UnitTestOutcome.Passed);
        var generator = new FakeCheckOptions { Seed = 3 }.CreateGenerator([.. Shape.GetParameters().Select(FakeArgument.From)]);
        method.Rows.ShouldBe(Enumerable.Range(0, 5).Select(i => generator.Row(i)));
    }

    [TestMethod]
    public async Task A_failing_row_is_shrunk_and_reported_with_the_attribute_to_replay_it()
    {
        var method = new RecordingMethod(row => (int)row[0]! < 500);

        var result = (await new FakeCheckAttribute { Seed = 8 }.ExecuteAsync(method)).ShouldHaveSingleItem();

        result.Outcome.ShouldBe(UnitTestOutcome.Failed);
        var failure = result.TestFailureException.ShouldBeOfType<FakeCheckException<object?[]>>();
        failure.Counterexample[0].ShouldBe(500);
        failure.Message.ShouldContain("quantity = 500, email = \"");
        failure.Message.ShouldContain("[FakeCheck(Seed = 8)]");
        failure.InnerException.ShouldBeOfType<AssertFailedException>(); // what the failing row reported
    }

    [TestMethod]
    public void The_options_are_readable_and_validated()
    {
        var attribute = new FakeCheckAttribute { Count = 7, MaxShrinks = 0, Seed = 5, Locale = "sv", NullProbability = 0.5, ReferenceDate = "2030-01-01" };

        (attribute.Count, attribute.MaxShrinks, attribute.Seed, attribute.Locale, attribute.NullProbability, attribute.ReferenceDate)
            .ShouldBe((7, 0, 5L, "sv", 0.5, "2030-01-01"));
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeCheckAttribute { Count = 0 });
    }

    private static MethodInfo Shape => typeof(FakeCheckTests).GetMethod(nameof(ShapeOfTheRows), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void ShapeOfTheRows([Range(0, 1000)] int quantity, string email) => _ = (quantity, email);

    /// <summary>A test method that records its rows and fails the rows <c>passes</c> rejects, as MSTest reports a failed assertion.</summary>
    private sealed class RecordingMethod(Func<object?[], bool> passes) : ITestMethod
    {
        public List<object?[]> Rows { get; } = [];

        public string TestMethodName => Shape.Name;
        public string TestClassName => nameof(FakeCheckTests);
        public Type ReturnType => typeof(void);
        public object?[]? Arguments => null;
        public ParameterInfo[] ParameterTypes => Shape.GetParameters();
        public MethodInfo MethodInfo => Shape;

        public Task<TestResult> InvokeAsync(object[]? arguments)
        {
            var row = arguments.ShouldNotBeNull();
            Rows.Add((object?[])row.Clone());
            return Task.FromResult(passes(row)
                ? new TestResult { Outcome = UnitTestOutcome.Passed }
                : new TestResult { Outcome = UnitTestOutcome.Failed, TestFailureException = new AssertFailedException("too many") });
        }

        public Attribute[]? GetAllAttributes() => [];
        public TAttributeType[] GetAttributes<TAttributeType>() where TAttributeType : Attribute => [];
    }
}
