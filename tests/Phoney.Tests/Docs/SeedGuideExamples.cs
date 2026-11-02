namespace Phoney.Tests.Docs;

// Inside the namespace, so the guides' model wins over the test models of Phoney.Tests.
using Phoney.Tests.Docs.Shop;

/// <summary>The examples of docs/seeds.md, run as tests: each one also checks the promise its paragraph makes.</summary>
public sealed class SeedGuideExamples
{
    [Test]
    public void A_seed_fixes_the_data_of_a_generator()
    {
        // snippet: seed-generator
        var first = Fake.For<Customer>().Seed(42).Generate(3);
        var again = Fake.For<Customer>().Seed(42).Generate(3);   // the same three customers, dates included
        // end snippet

        again.Select(c => (c.Email, c.CreatedAt)).ShouldBe(first.Select(c => (c.Email, c.CreatedAt)));
    }

    [Test]
    public void Item_n_does_not_depend_on_how_it_is_generated()
    {
        // snippet: seed-item-n
        var listed = Fake.For<Customer>().Seed(42).Generate(1_000);
        var parallel = Fake.For<Customer>().Seed(42).GenerateParallel(1_000);
        var streamed = Fake.For<Customer>().Seed(42).Stream().Take(1_000).ToList();
        // listed[500], parallel[500] and streamed[500] are the same customer
        // end snippet

        parallel[500].Email.ShouldBe(listed[500].Email);
        streamed[500].Email.ShouldBe(listed[500].Email);
    }

    [Test]
    public void Seeded_dates_do_not_follow_the_clock()
    {
        // snippet: seed-dates
        var customer = Fake.For<Customer>().Seed(1).Generate();
        // customer.CreatedAt is before 2025-01-01 (Faker.DefaultSeededReferenceDate) on every day you run this

        var current = Fake.For<Customer>().Seed(1).ReferenceDate(DateTimeOffset.UtcNow).Generate();   // opt back in to "now"
        // end snippet

        customer.CreatedAt.ShouldBeLessThan(Faker.DefaultSeededReferenceDate);
        current.CreatedAt.ShouldBeGreaterThan(customer.CreatedAt);
    }

    [Test]
    public void A_seeded_scope_fixes_the_facade()
    {
        (string Name, string Email) Run()
        {
            // snippet: seed-scope
            using var seeded = Fake.Seeded(7);          // until the end of the block: tests in parallel keep their own
            var name = Fake.Person.FullName();          // the same name on every run
            var customer = Fake.One<Customer>();        // Fake.One, Many, For and Populate too
            // end snippet
            return (name, customer.Email);
        }

        Run().ShouldBe(Run());
    }

    [Test]
    public void A_fork_gives_parallel_work_the_same_data_whatever_the_timing()
    {
        string[] Run()
        {
            // snippet: seed-fork
            var names = new string[100];
            using (Fake.Seeded(7))
            {
                Parallel.For(0, names.Length, i =>
                {
                    using var fork = Fake.Fork(i);      // item i's own faker, from the seed and i
                    names[i] = Fake.Person.FullName();
                });
            }
            // end snippet
            return names;
        }

        Run().ShouldBe(Run());
    }

    [Test]
    public void A_key_gives_the_same_stand_in_everywhere()
    {
        // snippet: seed-key
        var byGenerator = Fake.For<Customer>().GenerateFor("customer:42");
        var name = Fake.Keyed("customer:42").Person.FullName();

        Customer byFacade;
        using (Fake.ForKey("customer:42"))
            byFacade = Fake.One<Customer>();            // the same customer as byGenerator, in any process, on any machine
        // end snippet

        byFacade.Email.ShouldBe(byGenerator.Email);
        Fake.Keyed("customer:42").Person.FullName().ShouldBe(name);
    }

    [Test]
    public void A_random_run_reports_its_seed_when_it_fails()
    {
        var output = new List<string>();

        Should.Throw<InvalidOperationException>(() =>
        {
            // snippet: seed-run
            FakeSeed.Run(() =>
            {
                var orders = Fake.Many<Order>(20);      // other orders on every run
                Ship(orders);                           // when this throws, the seed goes to the report
            }, report: output.Add);
            // end snippet
        });

        output.ShouldHaveSingleItem().ShouldContain("PHONEY_SEED=");

        static void Ship(List<Order> orders) => throw new InvalidOperationException($"Could not ship {orders.Count} orders.");
    }
}
