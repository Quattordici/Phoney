namespace Phoney.MSTest.Tests;

/// <summary>Replayable random data in MSTest with <c>FakeSeed.Run</c> (the snippet of the README).</summary>
[TestClass]
public sealed class FakeSeedRunTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void A_test_can_run_on_random_data_and_report_its_seed() => FakeSeed.Run(() =>
    {
        Fake.IsSeeded.ShouldBeTrue();
        Fake.Person.FirstName().ShouldNotBeNullOrWhiteSpace();
    }, TestContext.WriteLine);

    [TestMethod]
    public async Task An_async_test_keeps_its_seed_across_awaits() => await FakeSeed.RunAsync(async () =>
    {
        await Task.Yield();
        Fake.IsSeeded.ShouldBeTrue();
        Fake.Person.FirstName().ShouldBe("Eden"); // the first name of seed 5, after the await too
    }, TestContext.WriteLine, seed: 5);
}
