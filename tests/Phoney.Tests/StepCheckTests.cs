namespace Phoney.Tests;

/// <summary>An account with a bug: it lets you withdraw more than its balance.</summary>
public sealed class CheckedAccount
{
    public int Balance { get; private set; }

    public void Deposit(int amount) => Balance += amount;

    public void Withdraw(int amount) => Balance -= amount;
}

/// <summary>A state that counts how often it is disposed.</summary>
public sealed class DisposedState : IDisposable
{
    public static int Created;
    public static int Disposed;

    public DisposedState() => Interlocked.Increment(ref Created);

    public void Dispose() => Interlocked.Increment(ref Disposed);
}

/// <summary><c>Fake.Steps</c> runs random sequences of steps against a state, checks invariants after each step and shrinks a failing sequence.</summary>
public sealed class StepCheckTests
{
    private static StepCheck<CheckedAccount> Account() =>
        Fake.Steps(() => new CheckedAccount())
            .Seed(42)
            .Step("deposit", f => f.Random.Int(1, 100), (a, amount) => a.Deposit(amount))
            .Step("withdraw", f => f.Random.Int(1, 100), (a, amount) => a.Withdraw(amount), when: a => a.Balance > 0);

    private static FakeCheckException<StepRun<CheckedAccount>> Falsify(StepCheck<CheckedAccount> steps, int runs = 100, int maxSteps = 50) =>
        Should.Throw<FakeCheckException<StepRun<CheckedAccount>>>(() => steps.Check(runs, maxSteps));

    [Test]
    public void Steps_whose_invariants_hold_pass()
    {
        var runs = 0;
        Fake.Steps(() =>
            {
                runs++;
                return new CheckedAccount();
            })
            .Seed(1)
            .Step("deposit", f => f.Random.Int(1, 100), (a, amount) => a.Deposit(amount))
            .Invariant(a => a.Balance >= 0)
            .Check(runs: 30);

        runs.ShouldBe(30);
    }

    [Test]
    public void A_broken_invariant_shrinks_to_the_shortest_sequence_that_still_breaks_it()
    {
        var failure = Falsify(Account().Invariant(a => a.Balance >= 0));

        failure.Counterexample.Steps.Select(s => s.ToString()).ShouldBe(["deposit 1", "withdraw 2"]);
        failure.Counterexample.FailedAt.ShouldBe(2);
        failure.Counterexample.State.Balance.ShouldBe(-1);
        failure.Original.Steps.Count.ShouldBeGreaterThanOrEqualTo(2);
        failure.Seed.ShouldBe(42);
        failure.ShrinkSteps.ShouldBeGreaterThan(0);
    }

    [Test]
    public void The_report_lists_the_steps_and_names_the_invariant()
    {
        var failure = Falsify(Account().Invariant(a => a.Balance >= 0));

        failure.Message.ShouldContain("The property failed for run number");
        failure.Message.ShouldContain("1. deposit 1");
        failure.Message.ShouldContain("2. withdraw 2");
        failure.Message.ShouldContain("the invariant a => a.Balance >= 0 failed after step 2");
        failure.Message.ShouldContain("Seed(42) on the steps");
    }

    [Test]
    public void An_invariant_can_be_an_assertion()
    {
        var failure = Falsify(Account().Invariant(a => a.Balance.ShouldBeGreaterThanOrEqualTo(0)));

        failure.InnerException.ShouldBeOfType<ShouldAssertException>();
        failure.Counterexample.Steps.Count.ShouldBe(2);
    }

    [Test]
    public void A_step_that_throws_fails_the_run_at_that_step()
    {
        var failure = Falsify(Fake.Steps(() => new CheckedAccount())
            .Seed(3)
            .Step("deposit", f => f.Random.Int(1, 100), (a, amount) => a.Deposit(amount))
            .Step("audit", a =>
            {
                if (a.Balance > 150)
                    throw new InvalidOperationException("too rich");
            }));

        failure.InnerException.ShouldBeOfType<InvalidOperationException>();
        failure.Counterexample.FailedAt.ShouldBe(failure.Counterexample.Steps.Count);
        failure.Counterexample.Steps[^1].Name.ShouldBe("audit");
        failure.Counterexample.State.Balance.ShouldBe(151);
        failure.Message.ShouldContain("step 3 (audit) threw InvalidOperationException: too rich");
    }

    [Test]
    public void A_step_runs_only_while_its_precondition_holds()
    {
        Fake.Steps(() => new CheckedAccount())
            .Seed(5)
            .Step("deposit", f => f.Random.Int(1, 100), (a, amount) => a.Deposit(amount))
            .Step("withdraw all", a => a.Withdraw(a.Balance), when: a => a.Balance > 0)
            .Step("never", a => a.Deposit(-1_000_000), when: _ => false)
            .Invariant(a => a.Balance >= 0)
            .Check(runs: 50);
    }

    [Test]
    public void A_state_aware_argument_reads_the_state()
    {
        var failure = Falsify(Fake.Steps(() => new CheckedAccount())
            .Seed(7)
            .Step("deposit", f => f.Random.Int(1, 100), (a, amount) => a.Deposit(amount))
            .Step("withdraw some", (f, a) => f.Random.Int(0, a.Balance), (a, amount) => a.Withdraw(amount))
            .Invariant(a => a.Balance != 3));

        failure.Counterexample.State.Balance.ShouldBe(3);
    }

    [Test]
    public void Runs_end_after_max_steps()
    {
        var longest = 0;
        Fake.Steps(() => new List<int>())
            .Seed(9)
            .Step("add", list => list.Add(1))
            .Invariant(list =>
            {
                longest = Math.Max(longest, list.Count);
                return true;
            })
            .Check(runs: 200, maxSteps: 7);

        longest.ShouldBe(7);
    }

    [Test]
    public void The_same_seed_finds_the_same_failure_whatever_the_number_of_runs()
    {
        var first = Falsify(Account().Invariant(a => a.Balance >= 0), runs: 100);
        var again = Falsify(Account().Invariant(a => a.Balance >= 0), runs: 500);

        again.Index.ShouldBe(first.Index);
        again.Original.Steps.ShouldBe(first.Original.Steps);
        again.Counterexample.Steps.ShouldBe(first.Counterexample.Steps);
    }

    [Test]
    public void A_disposable_state_is_disposed_after_every_run_shrink_candidates_included()
    {
        var created = DisposedState.Created;
        var disposed = DisposedState.Disposed;
        Should.Throw<FakeCheckException<StepRun<DisposedState>>>(() =>
            Fake.Steps(() => new DisposedState())
                .Seed(11)
                .Step("noop", _ => { })
                .Invariant(_ => false)
                .Check(runs: 20));

        (DisposedState.Created - created).ShouldBeGreaterThan(1);
        (DisposedState.Disposed - disposed).ShouldBe(DisposedState.Created - created);
    }

    [Test]
    public void A_generator_can_make_a_step_argument()
    {
        var carts = Fake.For<Cart>().CollectionSize(0, 3);
        var failure = Should.Throw<FakeCheckException<StepRun<List<Cart>>>>(() =>
            Fake.Steps(() => new List<Cart>())
                .Seed(13)
                .Step("add", carts, (list, cart) => list.Add(cart))
                .Invariant(list => list.Sum(c => c.Lines.Count) < 4)
                .Check());

        failure.Counterexample.State.Sum(c => c.Lines.Count).ShouldBe(4);
        failure.Counterexample.Steps.Count.ShouldBeLessThanOrEqualTo(2);
        failure.Message.ShouldContain("1. add Cart {");
    }

    [Test]
    public async Task CheckAsync_runs_asynchronous_steps_and_shrinks_them()
    {
        var failure = await Should.ThrowAsync<FakeCheckException<StepRun<CheckedAccount>>>(() =>
            Fake.Steps(() => new CheckedAccount())
                .Seed(42)
                .Step("deposit", f => f.Random.Int(1, 100), async (a, amount) =>
                {
                    await Task.Yield();
                    a.Deposit(amount);
                })
                .Step("withdraw", f => f.Random.Int(1, 100), (a, amount) => a.Withdraw(amount), when: a => a.Balance > 0)
                .Invariant(a => a.Balance >= 0)
                .CheckAsync());

        failure.Counterexample.Steps.Select(s => s.ToString()).ShouldBe(["deposit 1", "withdraw 2"]);
    }

    [Test]
    public void Check_refuses_asynchronous_steps()
    {
        var steps = Fake.Steps(() => new CheckedAccount()).Step("later", _ => Task.CompletedTask);

        Should.Throw<InvalidOperationException>(() => steps.Check()).Message.ShouldContain("CheckAsync");
    }

    [Test]
    public void Check_needs_a_step()
    {
        Should.Throw<InvalidOperationException>(() => Fake.Steps(() => 1).Check()).Message.ShouldContain(".Step(");
    }

    [Test]
    public void Arguments_are_validated()
    {
        var steps = Account();

        Should.Throw<ArgumentNullException>(() => Fake.Steps<int>(null!));
        Should.Throw<ArgumentException>(() => steps.Step(" ", _ => { }));
        Should.Throw<ArgumentNullException>(() => steps.Step("x", (Action<CheckedAccount>)null!));
        Should.Throw<ArgumentNullException>(() => steps.Invariant((Func<CheckedAccount, bool>)null!));
#pragma warning disable PHONEY011 // the runtime checks these too
        Should.Throw<ArgumentOutOfRangeException>(() => steps.Check(runs: -1));
        Should.Throw<ArgumentOutOfRangeException>(() => steps.Check(maxSteps: 0));
        Should.Throw<ArgumentOutOfRangeException>(() => steps.Check(maxShrinks: -1));
#pragma warning restore PHONEY011
    }

    [Test]
    public void Steps_are_immutable()
    {
        var empty = Fake.Steps(() => new CheckedAccount());
        _ = empty.Step("deposit", a => a.Deposit(1));

        Should.Throw<InvalidOperationException>(() => empty.Check());
    }

    [Test]
    public void A_seeded_facade_seeds_the_steps()
    {
        long Seed()
        {
            using var _ = Fake.Seeded(99);
            return Falsify(Fake.Steps(() => new CheckedAccount())
                .Step("deposit", f => f.Random.Int(1, 100), (a, amount) => a.Deposit(amount))
                .Step("withdraw", f => f.Random.Int(1, 100), (a, amount) => a.Withdraw(amount))
                .Invariant(a => a.Balance >= 0)).Seed;
        }

        Seed().ShouldBe(Seed());
    }

    [Test]
    public void Zero_runs_test_nothing()
    {
        Account().Invariant(_ => false).Check(runs: 0);
    }
}
