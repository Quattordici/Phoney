namespace Phoney.Xunit.Tests;

/// <summary>Replayable random data in xUnit.net v3 with <c>FakeSeed.Run</c> (the snippet of the README).</summary>
public sealed class FakeSeedRunTests
{
    [Fact]
    public void A_test_can_run_on_random_data_and_report_its_seed() => FakeSeed.Run(() =>
    {
        Fake.IsSeeded.ShouldBeTrue();
        Fake.Person.FirstName().ShouldNotBeNullOrWhiteSpace();
    }, TestContext.Current.SendDiagnosticMessage);

    [Fact]
    public async Task An_async_test_keeps_its_seed_across_awaits() => await FakeSeed.RunAsync(async () =>
    {
        await Task.Yield();
        Fake.IsSeeded.ShouldBeTrue();
        Fake.Person.FirstName().ShouldBe("Eden"); // the first name of seed 5, after the await too
    }, TestContext.Current.SendDiagnosticMessage, seed: 5);
}
