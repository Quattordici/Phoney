namespace Phoney.Tests;

/// <summary>Reproducibility: seeds fix dates as well as values, and the static facade can be seeded.</summary>
public sealed class SeedingTests
{
    [Fact]
    public void Seeded_fakers_use_a_fixed_reference_date()
    {
        var a = new Faker("en", seed: 1);
        var b = new Faker("en", seed: 1);

        a.ReferenceDate.ShouldBe(Faker.DefaultSeededReferenceDate);
        a.Date.Past().ShouldBe(b.Date.Past());
        a.Person.Profile().ShouldBe(b.Person.Profile());
        a.Date.Recent(3).ShouldBeLessThanOrEqualTo(Faker.DefaultSeededReferenceDate);
    }

    [Fact]
    public void Unseeded_fakers_use_the_current_time_and_explicit_dates_win()
    {
        new Faker().ReferenceDate.ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var explicitDate = new DateTimeOffset(2030, 5, 1, 0, 0, 0, TimeSpan.Zero);
        new Faker(seed: 1) { ReferenceDate = explicitDate }.ReferenceDate.ShouldBe(explicitDate);

        var reseeded = new Faker() { ReferenceDate = explicitDate }.Seed(3);
        reseeded.ReferenceDate.ShouldBe(explicitDate);
        new Faker().Seed(3).ReferenceDate.ShouldBe(Faker.DefaultSeededReferenceDate);
    }

    [Fact]
    public void Seeded_generators_produce_the_same_dates_regardless_of_the_clock()
    {
        var first = Fake.For<Customer>().Seed(9).Generate(10);
        var second = Fake.For<Customer>().Seed(9).Generate(10);

        second.Select(c => (c.CreatedAt, c.DateOfBirth, c.Age)).ShouldBe(first.Select(c => (c.CreatedAt, c.DateOfBirth, c.Age)));
        first.ShouldAllBe(c => c.CreatedAt <= Faker.DefaultSeededReferenceDate.UtcDateTime);

        var explicitDate = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Fake.For<Customer>().Seed(9).ReferenceDate(explicitDate).Generate()
            .CreatedAt.ShouldBeGreaterThan(Faker.DefaultSeededReferenceDate.UtcDateTime);
    }

    [Fact]
    public void Seeded_scopes_make_the_facade_reproducible()
    {
        static string Sample() =>
            $"{Fake.Person.FullName()}|{Fake.Internet.Email()}|{Fake.Date.Past():O}|{Fake.One<Customer>().Email}|{Fake.Many<Order>(2)[1].Total}";

        string first, second;
        using (Fake.Seeded(42))
        {
            Fake.IsSeeded.ShouldBeTrue();
            first = Sample();
        }

        using (Fake.Seeded(42))
            second = Sample();

        second.ShouldBe(first);
        Fake.IsSeeded.ShouldBeFalse();
    }

    [Fact]
    public void Seeded_scopes_nest_and_take_a_locale()
    {
        using (Fake.Seeded(1, "sv"))
        {
            Fake.Faker.Locale.ShouldBe("sv");
            using (Fake.Seeded(2, "de"))
                Fake.Faker.Locale.ShouldBe("de");
            Fake.Faker.Locale.ShouldBe("sv");
        }

        Fake.IsSeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Seeded_scopes_are_isolated_between_parallel_flows()
    {
        static List<string> Names(long seed)
        {
            using var _ = Fake.Seeded(seed);
            return [.. Enumerable.Range(0, 50).Select(_ => Fake.Person.FullName())];
        }

        var expected1 = Names(1);
        var expected2 = Names(2);
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(i => Task.Run(() => (Seed: i % 2 + 1, Names: Names(i % 2 + 1)))));

        foreach (var (seed, names) in results)
            names.ShouldBe(seed == 1 ? expected1 : expected2);
    }
}

/// <summary>Tests that change process-wide facade state run alone.</summary>
[CollectionDefinition(nameof(GlobalFakeState), DisableParallelization = true)]
public sealed class GlobalFakeState;

[Collection(nameof(GlobalFakeState))]
public sealed class GlobalSeedTests
{
    [Fact]
    public async Task Global_seed_makes_every_thread_reproducible()
    {
        try
        {
            Fake.Seed(5);
            Fake.IsSeeded.ShouldBeTrue();
            var first = (Fake.Person.FullName(), Fake.One<Customer>().Email);

            Fake.Seed(5); // re-seeding restarts the sequence
            (Fake.Person.FullName(), Fake.One<Customer>().Email).ShouldBe(first);

            var onOtherThread = await Task.Run(() => { Fake.Seed(5); return Fake.Person.FullName(); }, TestContext.Current.CancellationToken);
            onOtherThread.ShouldBe(first.Item1);
        }
        finally
        {
            Fake.Seed(null);
        }

        Fake.IsSeeded.ShouldBeFalse();
    }
}
