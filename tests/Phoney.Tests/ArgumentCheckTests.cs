using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary><c>Fake.Check</c> and <c>ArgumentGenerator.Check</c> run a property over rows of arguments and shrink a failing row like <c>Check</c> on a generator.</summary>
public sealed class ArgumentCheckTests
{
    private static FakeCheckException<object?[]> Falsify(Action check) => Should.Throw<FakeCheckException<object?[]>>(check);

    [Test]
    public void A_property_that_holds_for_every_row_passes()
    {
        var seen = 0;
        Fake.Check((string email, [Range(1, 10)] int quantity) =>
        {
            seen++;
            return email.Contains('@', StringComparison.Ordinal) && quantity is >= 1 and <= 10;
        });

        seen.ShouldBe(100);
    }

    [Test]
    public void A_failing_row_is_shrunk_to_the_smallest_failing_values()
    {
        var failure = Falsify(() => Fake.Check(([Range(0, 1000)] int discount) => discount < 500));

        failure.Counterexample.ShouldBe([500]);
        ((int)failure.Original[0]!).ShouldBeGreaterThanOrEqualTo(500);
    }

    [Test]
    public void Values_of_several_parameters_shrink_together_and_the_report_names_them()
    {
        var failure = Falsify(() => Fake.Check((CartLine line, [Range(0, 1000)] int discount) => line.Quantity * 10 - discount >= 0));

        var line = (CartLine)failure.Counterexample[0]!;
        var discount = (int)failure.Counterexample[1]!;
        (line.Quantity * 10 - discount).ShouldBe(-1);
        line.Quantity.ShouldBe(1);
        failure.Message.ShouldContain("The property failed for row number ");
        failure.Message.ShouldContain("line = CartLine { Sku = \"");
        failure.Message.ShouldContain("discount = 11");
    }

    [Test]
    public void Up_to_four_parameters_are_checked()
    {
        var rows = 0;
        Fake.Check((string firstName, string lastName, string email, [Range(18, 99)] int age) =>
        {
            rows++;
            email.ShouldNotBeNullOrEmpty();
            age.ShouldBeInRange(18, 99);
        }, count: 10);

        rows.ShouldBe(10);
    }

    [Test]
    public void An_assertion_that_throws_fails_the_row_and_is_the_inner_exception()
    {
        var failure = Falsify(() => Fake.Check(([Range(0, 1000)] int discount) => discount.ShouldBeLessThan(500)));

        failure.Counterexample.ShouldBe([500]);
        failure.InnerException.ShouldBeOfType<ShouldAssertException>();
    }

    [Test]
    public async Task An_asynchronous_property_is_shrunk_the_same_way()
    {
        var failure = await Should.ThrowAsync<FakeCheckException<object?[]>>(() => Fake.CheckAsync(async ([Range(0, 1000)] int discount) =>
        {
            await Task.Yield();
            discount.ShouldBeLessThan(500);
        }));

        failure.Counterexample.ShouldBe([500]);
    }

    [Test]
    public void A_seeded_generator_finds_the_same_counterexample_and_says_how_to_replay_it()
    {
        var generator = Fake.Arguments([new FakeArgument("quantity", typeof(int)), new FakeArgument("email", typeof(string))]).Seed(7);

        var first = Falsify(() => generator.Check(row => (int)row[0]! % 7 != 3));
        var second = Falsify(() => generator.Check(row => (int)row[0]! % 7 != 3));

        first.Seed.ShouldBe(7);
        second.Counterexample.ShouldBe(first.Counterexample);
        first.Message.ShouldContain("seed 7");
        first.Message.ShouldContain("Replay: Seed(7) on the generator");
    }

    [Test]
    public void A_failed_Fake_Check_says_to_replay_it_with_the_environment_seed_only()
    {
        var failure = Falsify(() => Fake.Check((int quantity) => false));

        failure.Message.ShouldContain("Replay: PHONEY_SEED=");
        failure.Message.ShouldNotContain("on the generator");
    }

    [Test]
    public void The_replay_hint_can_name_how_a_test_framework_pins_the_seed()
    {
        var generator = Fake.Arguments([new FakeArgument("quantity", typeof(int))]).ReplayHint("[FakeCheck(Seed = {0})]");

        var failure = Falsify(() => generator.Check(_ => false));

        failure.Message.ShouldContain($"[FakeCheck(Seed = {failure.Seed})]");
    }

    [Test]
    public void The_rows_of_a_check_are_the_rows_the_generator_makes_for_its_seed()
    {
        var generator = Fake.Arguments([new FakeArgument("firstName", typeof(string)), new FakeArgument("email", typeof(string))]).Seed(3);
        var expected = generator.Row(4);

        var failure = Falsify(() => generator.Check(row => row[0] as string != expected[0] as string, maxShrinks: 0));

        failure.Index.ShouldBe(4);
        failure.Counterexample.ShouldBe(expected);
    }

    [Test]
    public void FakeCheck_options_pin_the_seed_and_name_the_attribute_in_the_report()
    {
        var options = new FakeCheckOptions { Seed = 5, Locale = "sv" };

        var failure = Falsify(() => options.CreateGenerator([new FakeArgument("quantity", typeof(int))]).Check(_ => false, options.Count, options.MaxShrinks));

        failure.Seed.ShouldBe(5);
        failure.Message.ShouldContain("Replay: [FakeCheck(Seed = 5)] finds and shrinks it again.");
    }

    [Test]
    public void FakeCheck_options_have_defaults_and_reject_values_that_would_fail_later()
    {
        var options = new FakeCheckOptions();

        options.Count.ShouldBe(100);
        options.MaxShrinks.ShouldBe(1000);
        options.Seed.ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(() => options.Count = 0);
        Should.Throw<ArgumentOutOfRangeException>(() => options.MaxShrinks = -1);
        Should.Throw<ArgumentOutOfRangeException>(() => options.NullProbability = 2);
        Should.Throw<ArgumentException>(() => options.ReferenceDate = "soon");
    }

    [Test]
    public void Arguments_are_validated()
    {
        Should.Throw<ArgumentNullException>(() => Fake.Check((Func<int, bool>)null!));
#pragma warning disable PHONEY011 // the runtime checks are what this test is about
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Check((int x) => true, count: -1));
        Should.Throw<ArgumentOutOfRangeException>(() => Fake.Check((int x) => true, maxShrinks: -1));
#pragma warning restore PHONEY011
        Should.Throw<ArgumentException>(() => Fake.Arguments([new FakeArgument("x", typeof(int))]).ReplayHint("no placeholder"));
    }
}
