#pragma warning disable TUnit0031 // the async void lambdas are what the tests give Check, to see them refused

namespace Phoney.Tests;

/// <summary>
/// An <c>async</c> lambda given to a synchronous <c>Check</c> binds to its <see cref="Action{T}"/> overload and would run as <c>async void</c>: the check would pass
/// before the property finished and a later failure would crash the process, so every synchronous entry point refuses it and names <c>CheckAsync</c>.
/// </summary>
public sealed class AsyncPropertyGuardTests
{
    // The lambdas never throw, so a missing guard fails the test instead of crashing the test host.
    [Test]
    public void A_generator_refuses_an_async_lambda() =>
        Should.Throw<ArgumentException>(() => Fake.For<Cart>().Seed(1).Check(async _ => await Task.Yield()))
            .Message.ShouldContain("Use CheckAsync instead");

    [Test]
    public void An_argument_generator_refuses_an_async_lambda() =>
        Should.Throw<ArgumentException>(() => Fake.Arguments(typeof(AsyncPropertyGuardTests).GetMethod(nameof(Takes))!).Check(async _ => await Task.Yield()))
            .Message.ShouldContain("Use CheckAsync instead");

    [Test]
    public void Fake_Check_refuses_an_async_lambda() =>
        Should.Throw<ArgumentException>(() => Fake.Check(async (int value) => await Task.Yield()))
            .Message.ShouldContain("Use Fake.CheckAsync instead");

    [Test]
    public void A_plain_lambda_still_runs()
    {
        var seen = 0;
        Fake.For<Cart>().Seed(1).Check(_ => seen++, count: 3);
        Fake.Check((int _) => seen++, count: 3);
        seen.ShouldBe(6);
    }

    public static void Takes(int value) => _ = value;
}
