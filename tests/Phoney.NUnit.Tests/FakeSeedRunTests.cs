namespace Phoney.NUnit.Tests;

/// <summary>Replayable random data in NUnit with <c>FakeSeed.Run</c> (the snippet of the README).</summary>
public sealed class FakeSeedRunTests
{
    [Test]
    public void A_test_can_run_on_random_data_and_report_its_seed() => FakeSeed.Run(() =>
    {
        Fake.IsSeeded.ShouldBeTrue();
        Fake.Person.FirstName().ShouldNotBeNullOrWhiteSpace();
    }, TestContext.Out.WriteLine);

    [Test]
    public async Task An_async_test_keeps_its_seed_across_awaits() => await FakeSeed.RunAsync(async () =>
    {
        await Task.Yield();
        Fake.IsSeeded.ShouldBeTrue();
    }, TestContext.Out.WriteLine, seed: 5);
}
