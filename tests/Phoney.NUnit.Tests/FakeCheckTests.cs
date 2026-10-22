using System.ComponentModel.DataAnnotations;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;
using Phoney.Generation;
using Range = System.ComponentModel.DataAnnotations.RangeAttribute; // NUnit has a [Range] of its own

namespace Phoney.NUnit.Tests;

/// <summary>[FakeCheck] runs a test once per generated row of its parameters and shrinks a failing row.</summary>
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

    [Test]
    public void The_test_is_named_after_the_method_and_runs_once_per_row_with_the_rows_of_the_seed()
    {
        var fixture = new Fixture(_ => { });

        var (test, result) = Run(new FakeCheckAttribute { Count = 5, Seed = 3 }, fixture);

        test.Name.ShouldBe(nameof(Fixture.Total));
        result.ResultState.ShouldBe(ResultState.Success);
        var generator = new FakeCheckOptions { Seed = 3 }.CreateGenerator([.. typeof(Fixture).GetMethod(nameof(Fixture.Total))!.GetParameters().Select(FakeArgument.From)]);
        fixture.Rows.ShouldBe(Enumerable.Range(0, 5).Select(i => generator.Row(i)));
    }

    [Test]
    public void A_failing_row_is_shrunk_and_reported_with_the_attribute_to_replay_it()
    {
        var (_, result) = Run(new FakeCheckAttribute { Seed = 8 }, new Fixture(row => Assert.That((int)row[0]!, Is.LessThan(500))));

        result.ResultState.ShouldBe(ResultState.Failure);
        result.Message.ShouldStartWith("The property failed for row number");
        result.Message.ShouldContain("quantity = 500, email = \"");
        result.Message.ShouldContain("Because: AssertionException:"); // what the failing row threw, unwrapped from NUnit's invocation
        result.Message.ShouldContain("[FakeCheck(Seed = 8)]");
        result.AssertionResults.ShouldBeEmpty(); // the failed assertions of the rows tried are not the test's
    }

    [Test]
    public void Assert_Pass_passes_a_row()
    {
        var (_, result) = Run(new FakeCheckAttribute { Count = 3 }, new Fixture(_ => Assert.Pass()));

        result.ResultState.ShouldBe(ResultState.Success);
    }

    [Test]
    public void The_options_are_readable_and_validated()
    {
        var attribute = new FakeCheckAttribute { Count = 7, MaxShrinks = 0, Seed = 5, Locale = "sv", NullProbability = 0.5, ReferenceDate = "2030-01-01" };

        (attribute.Count, attribute.MaxShrinks, attribute.Seed, attribute.Locale, attribute.NullProbability, attribute.ReferenceDate)
            .ShouldBe((7, 0, 5L, "sv", 0.5, "2030-01-01"));
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeCheckAttribute { Count = 0 });
    }

    /// <summary>Builds and runs <see cref="Fixture.Total"/> as NUnit does, in a context of its own so its assertions don't reach this test.</summary>
    private static (TestMethod Test, TestResult Result) Run(FakeCheckAttribute attribute, Fixture fixture)
    {
        using var isolated = new TestExecutionContext.IsolatedContext();
        var test = attribute.BuildFrom(new MethodWrapper(typeof(Fixture), nameof(Fixture.Total)), null).ShouldHaveSingleItem();
        var context = TestExecutionContext.CurrentContext;
        context.CurrentTest = test;
        context.CurrentResult = test.MakeTestResult();
        context.TestObject = fixture;
        return (test, attribute.Wrap(new TestMethodCommand(test)).Execute(context));
    }

    /// <summary>Not a test fixture: <see cref="Run"/> runs its method.</summary>
    public sealed class Fixture(Action<object?[]> body)
    {
        public List<object?[]> Rows { get; } = [];

        public void Total([Range(0, 1000)] int quantity, string email)
        {
            object?[] row = [quantity, email];
            Rows.Add(row);
            body(row);
        }
    }
}
