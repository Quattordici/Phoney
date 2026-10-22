using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Phoney.Generation;

namespace Phoney.Xunit.Tests;

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

    [Fact]
    public async Task A_run_fills_the_test_arguments_with_each_row()
    {
        var values = new object?[2];
        var seen = new List<(int, string)>();
        var generator = Generator(seed: 3);

        var failure = await CheckedRun.Execute(generator, new FakeCheckOptions { Count = 5 }, values, () =>
        {
            seen.Add(((int)values[0]!, (string)values[1]!));
            return new ValueTask<Exception?>((Exception?)null);
        });

        failure.ShouldBeNull();
        seen.ShouldBe(Enumerable.Range(0, 5).Select(i => generator.Row(i)).Select(r => ((int)r[0]!, (string)r[1]!)));
    }

    [Fact]
    public async Task A_failing_row_is_shrunk_and_reported_with_the_attribute_to_replay_it()
    {
        var values = new object?[2];
        var options = new FakeCheckOptions { Seed = 8 };
        var generator = options.CreateGenerator([.. Shape.GetParameters().Select(FakeArgument.From)]);

        var failure = await CheckedRun.Execute(generator, options, values, () =>
            new ValueTask<Exception?>((int)values[0]! < 500 ? null : new InvalidOperationException("too many")));

        var check = failure.ShouldBeOfType<FakeCheckException<object?[]>>();
        check.Counterexample[0].ShouldBe(500);
        check.Message.ShouldContain("quantity = 500, email = \"");
        check.Message.ShouldContain("[FakeCheck(Seed = 8)]");
        check.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void The_options_are_readable_and_validated()
    {
        var attribute = new FakeCheckAttribute { Count = 7, MaxShrinks = 0, Seed = 5, Locale = "sv", NullProbability = 0.5, ReferenceDate = "2030-01-01" };

        (attribute.Count, attribute.MaxShrinks, attribute.Seed, attribute.Locale, attribute.NullProbability, attribute.ReferenceDate)
            .ShouldBe((7, 0, 5L, "sv", 0.5, "2030-01-01"));
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeCheckAttribute { Count = 0 });
    }

    private static MethodInfo Shape => typeof(FakeCheckTests).GetMethod(nameof(ShapeOfTheRows), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void ShapeOfTheRows([Range(0, 1000)] int quantity, string email) => _ = (quantity, email);

    private static ArgumentGenerator Generator(long seed) => new ArgumentGenerator([.. Shape.GetParameters().Select(FakeArgument.From)]).Seed(seed);
}
