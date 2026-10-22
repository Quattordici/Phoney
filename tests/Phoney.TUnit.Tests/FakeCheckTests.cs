using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Phoney.Generation;
using TUnit.Core.Executors;

namespace Phoney.TUnit.Tests;

/// <summary>[FakeCheck] runs a test once per generated row of its parameters and shrinks a failing row.</summary>
public sealed class FakeCheckTests
{
    private static readonly ConcurrentDictionary<string, int> Runs = new();

    [Test, FakeCheck(Count = 25)]
    public void Runs_the_test_on_every_row(string email, [Range(1, 10)] int quantity)
    {
        email.ShouldContain('@');
        quantity.ShouldBeInRange(1, 10);
        Runs.AddOrUpdate(nameof(Runs_the_test_on_every_row), 1, (_, n) => n + 1);
    }

    [Test, DependsOn(nameof(Runs_the_test_on_every_row))]
    public void The_test_ran_once_per_row() => Runs[nameof(Runs_the_test_on_every_row)].ShouldBe(25);

    [Test, FakeCheck(Count = 10)]
    public async Task Asynchronous_tests_are_checked_too(Customer customer)
    {
        await Task.Yield();
        customer.Email.ShouldNotBeNullOrEmpty();
    }

    [Test, FakeCheck(Count = 3), TestExecutor<MarkingExecutor>]
    public void The_executor_a_test_already_has_runs_each_row() => MarkingExecutor.Active.Value.ShouldBeTrue();

    [Test, FakeCheck(Count = 3), FakeSeed(11)]
    public void Works_inside_FakeSeed(string email)
    {
        email.ShouldContain('@');
        Fake.IsSeeded.ShouldBeTrue(); // the seeded scope wraps every row
    }

    [Test]
    public async Task A_run_fills_the_test_arguments_with_each_row()
    {
        var values = new object?[2];
        var seen = new List<(int, string)>();
        var generator = Generator(seed: 3);

        await CheckedRun.Execute(generator, new FakeCheckOptions { Count = 5 }, values, () =>
        {
            seen.Add(((int)values[0]!, (string)values[1]!));
            return ValueTask.CompletedTask;
        });

        seen.ShouldBe(Enumerable.Range(0, 5).Select(i => generator.Row(i)).Select(r => ((int)r[0]!, (string)r[1]!)));
    }

    [Test]
    public async Task A_failing_row_is_shrunk_and_reported_with_the_attribute_to_replay_it()
    {
        var values = new object?[2];
        var generator = new FakeCheckOptions { Seed = 8 }.CreateGenerator(Generator(seed: 0).Arguments.ToList());

        var failure = await Should.ThrowAsync<FakeCheckException<object?[]>>(async () => await CheckedRun.Execute(generator, new FakeCheckOptions(), values, () =>
        {
            ((int)values[0]!).ShouldBeLessThan(500);
            return ValueTask.CompletedTask;
        }));

        failure.Counterexample[0].ShouldBe(500);
        failure.Message.ShouldContain("quantity = 500, email = \"");
        failure.Message.ShouldContain("[FakeCheck(Seed = 8)]");
        failure.InnerException.ShouldBeOfType<ShouldAssertException>();
    }

    [Test]
    public void The_options_are_readable_and_validated()
    {
        var attribute = new FakeCheckAttribute { Count = 7, MaxShrinks = 0, Seed = 5, Locale = "sv", NullProbability = 0.5, ReferenceDate = "2030-01-01" };

        (attribute.Count, attribute.MaxShrinks, attribute.Seed, attribute.Locale, attribute.NullProbability, attribute.ReferenceDate)
            .ShouldBe((7, 0, 5L, "sv", 0.5, "2030-01-01"));
        Should.Throw<ArgumentOutOfRangeException>(() => new FakeCheckAttribute { Count = 0 });
    }

    private static ArgumentGenerator Generator(long seed) =>
        new ArgumentGenerator([FakeArgument.From(typeof(FakeCheckTests).GetMethod(nameof(Shape), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetParameters()[0]), new FakeArgument("email", typeof(string))]).Seed(seed);

    private static void Shape([Range(0, 1000)] int quantity) => _ = quantity;
}
