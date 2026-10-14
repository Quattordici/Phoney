using TUnit.Core.Executors;
using TUnit.Core.Interfaces;

namespace Phoney.TUnit.Tests;

/// <summary>An executor that marks the async flow of the test body, to prove it ran.</summary>
public sealed class MarkingExecutor : ITestExecutor
{
    public static readonly AsyncLocal<bool> Active = new();

    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        Active.Value = true;
        try
        {
            await action();
        }
        finally
        {
            Active.Value = false;
        }
    }
}

/// <summary>[FakeSeed] wraps the executor a test already has instead of replacing it, whichever attribute comes first.</summary>
public sealed class FakeSeedWithOtherExecutorsTests
{
    [Test, FakeSeed(11), TestExecutor<MarkingExecutor>]
    public void FakeSeed_listed_first_keeps_the_other_executor() => ShouldRunBoth();

    [Test, TestExecutor<MarkingExecutor>, FakeSeed(11)]
    public void FakeSeed_listed_last_keeps_the_other_executor() => ShouldRunBoth();

    private static void ShouldRunBoth()
    {
        MarkingExecutor.Active.Value.ShouldBeTrue("the other executor must still run");
        Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(11));
    }

    [Test]
    public async Task The_wrapped_executor_runs_inside_the_seeded_scope_and_the_body_runs_once()
    {
        var inner = new RecordingExecutor();
        var executor = new SeededTestExecutor(21, inner);
        var runs = 0;

        await executor.ExecuteTest(TestContext.Current!, () =>
        {
            runs++;
            return ValueTask.CompletedTask;
        });

        runs.ShouldBe(1);
        inner.Calls.ShouldBe(1);
        inner.SeededWhenCalled.ShouldBeTrue();
    }

    private sealed class RecordingExecutor : ITestExecutor
    {
        public int Calls { get; private set; }

        public bool SeededWhenCalled { get; private set; }

        public ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
        {
            Calls++;
            SeededWhenCalled = Fake.IsSeeded;
            return action();
        }
    }
}
