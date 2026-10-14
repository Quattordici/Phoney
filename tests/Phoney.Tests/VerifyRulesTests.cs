namespace Phoney.Tests;

/// <summary>
/// <c>VerifyRules</c> fails when a rule whose reach depends on the object graph (a path rule, <c>Subtype</c>, <c>Use</c>,
/// <c>WithConvention</c>) did not apply to anything that was generated.
/// </summary>
public sealed class VerifyRulesTests
{
    private static Generator<Statement> Make() => Fake.For<Statement>().Seed(1);

    [Test]
    public void Rules_that_applied_pass()
    {
        var statements = Make()
            .With(s => s.Billing.City, "Oslo")
            .Ignore(s => s.Billing.Contact)
            .With(s => s.Lines.Each().Sku, "SKU")
            .Subtype<IShape, Circle>()
            .Use(Fake.For<Contact>().With(c => c.Phone, "1"))
            .WithConvention<string>(n => n == "Number", _ => "N-1")
            .VerifyRules()
            .Generate(5);

        statements.ShouldAllBe(s => s.Billing.City == "Oslo" && s.Number == "N-1");
    }

    [Test]
    public void Without_VerifyRules_unused_rules_stay_silent_as_before()
    {
        Make().CollectionSize(0, 0).With(s => s.Lines.Each().Sku, "SKU").Generate(3).ShouldNotBeEmpty();
    }

    [Test]
    public void A_path_rule_hidden_by_a_rule_for_the_enclosing_member_is_reported()
    {
        var generator = Make()
            .With(s => s.Billing, Fake.For<Counterparty>())
            .With(s => s.Billing.City, "Oslo")
            .VerifyRules();

        var error = Should.Throw<InvalidOperationException>(() => generator.Generate(10));

        error.Message.ShouldContain("Statement.Billing.City");
        error.Message.ShouldContain("With");
    }

    [Test]
    public void A_path_rule_for_elements_that_never_exist_is_reported()
    {
        var generator = Make().CollectionSize(0, 0).With(s => s.Lines.Each().Sku, "SKU").VerifyRules();

        Should.Throw<InvalidOperationException>(() => generator.Generate(10)).Message.ShouldContain("Statement.Lines.Sku");
    }

    [Test]
    public void A_rule_counts_as_used_when_it_applied_to_any_of_the_generated_objects()
    {
        // Half of the suppliers are null, so the rule only reaches some objects.
        var statements = Make().NullProbability(0.5).With(s => s.Lines.Each().Supplier!.City, "Bergen").VerifyRules().Generate(60);

        statements.SelectMany(s => s.Lines).Select(l => l.Supplier).OfType<Counterparty>().ShouldAllBe(c => c.City == "Bergen");
    }

    [Test]
    public void Ignore_and_constraint_rules_on_paths_are_checked_too()
    {
        var hidden = Make().With(s => s.Billing, Fake.For<Counterparty>());

        Should.Throw<InvalidOperationException>(() => hidden.Ignore(s => s.Billing.Contact).VerifyRules().Generate(3)).Message.ShouldContain("Ignore");
        Should.Throw<InvalidOperationException>(() => hidden.Invalid(s => s.Lines.Each().Quantity).CollectionSize(0, 0).VerifyRules().Generate(3)).Message.ShouldContain("Invalid");
    }

    [Test]
    public void A_subtype_that_nothing_in_the_graph_needs_is_reported()
    {
        var generator = Make().Subtype<Pet, Cat>().VerifyRules();

        Should.Throw<InvalidOperationException>(() => generator.Generate(5)).Message.ShouldContain("Subtype<Pet>");
    }

    [Test]
    public void A_type_generator_that_nothing_in_the_graph_needs_is_reported()
    {
        var generator = Fake.For<Counterparty>().Seed(1).Use(Fake.For<Line>()).VerifyRules();

        Should.Throw<InvalidOperationException>(() => generator.Generate(5)).Message.ShouldContain("Use<Line>");
    }

    [Test]
    public void A_convention_that_matches_no_member_is_reported_and_one_that_matches_nested_members_passes()
    {
        var unused = Make().WithConvention<string>(n => n == "Nope", _ => "x").VerifyRules();
        var nested = Make().WithConvention<string>(n => n == "Phone", _ => "+1").VerifyRules();

        Should.Throw<InvalidOperationException>(() => unused.Generate(5)).Message.ShouldContain("WithConvention<String>");
        nested.Generate(5).ShouldAllBe(s => s.Billing.Contact.Phone == "+1");
    }

    [Test]
    public void A_convention_hidden_by_an_explicit_rule_is_reported()
    {
        var generator = Fake.For<Counterparty>().Seed(1)
            .With(c => c.City, "Oslo")
            .WithConvention<string>(n => n == "City", _ => "Bergen")
            .VerifyRules();

        Should.Throw<InvalidOperationException>(() => generator.Generate(3)).Message.ShouldContain("WithConvention<String>");
    }

    [Test]
    public void Every_unused_rule_is_listed_once_in_order()
    {
        var generator = Make()
            .CollectionSize(0, 0)
            .With(s => s.Lines.Each().Sku, "SKU")
            .With(s => s.Billing.City, "Oslo")
            .Subtype<Pet, Cat>()
            .Use(Fake.For<Line>())
            .WithConvention<int>(n => n == "Nope", _ => 1)
            .VerifyRules();

        var message = Should.Throw<InvalidOperationException>(() => generator.Generate(5)).Message;

        message.ShouldContain("Statement.Lines.Sku");
        message.ShouldContain("Subtype<Pet>");
        message.ShouldContain("Use<Line>");
        message.ShouldContain("WithConvention<Int32>");
        message.ShouldNotContain("Statement.Billing.City"); // this one did apply
        message.IndexOf("Statement.Lines.Sku", StringComparison.Ordinal).ShouldBeLessThan(message.IndexOf("Subtype<Pet>", StringComparison.Ordinal));
        message.IndexOf("Subtype<Pet>", StringComparison.Ordinal).ShouldBeLessThan(message.IndexOf("Use<Line>", StringComparison.Ordinal));
    }

    [Test]
    public void Rules_for_the_generated_objects_own_members_are_not_checked()
    {
        Fake.For<Counterparty>().With(c => c.Name, "x").Ignore(c => c.City).VerifyRules().Generate(2).ShouldAllBe(c => c.Name == "x");
    }

    [Test]
    public void The_check_runs_after_Generate_GenerateParallel_and_Populate()
    {
        var unused = Make().Subtype<Pet, Cat>().VerifyRules();

        Should.Throw<InvalidOperationException>(() => unused.Generate());
        Should.Throw<InvalidOperationException>(() => unused.Generate(4));
        Should.Throw<InvalidOperationException>(() => unused.GenerateParallel(4));
        Should.Throw<InvalidOperationException>(() => unused.Populate(new Statement()));
        Should.Throw<InvalidOperationException>(() => unused.Populate([new Statement(), new Statement()]));
    }

    [Test]
    public void Path_rules_do_not_apply_to_populated_objects_and_that_is_reported()
    {
        var generator = Make().With(s => s.Billing.City, "Oslo").VerifyRules();
        var statement = new Statement { Billing = new Counterparty { Name = "kept", Contact = new Contact() } };

        Should.Throw<InvalidOperationException>(() => generator.Populate(statement)).Message.ShouldContain("Statement.Billing.City");
    }

    [Test]
    public void Generating_nothing_proves_nothing_so_it_is_not_an_error()
    {
        Make().Subtype<Pet, Cat>().VerifyRules().Generate(0).ShouldBeEmpty();
    }

    [Test]
    public void An_endless_stream_has_no_end_to_check_at_so_it_is_rejected_up_front()
    {
        var generator = Make().VerifyRules();

        Should.Throw<NotSupportedException>(() => generator.Stream()).Message.ShouldContain("Generate");
    }

    [Test]
    public void Usage_accumulates_over_calls_of_one_generator()
    {
        // Half of the suppliers are null: the rule applies in the first call, and later single objects without a supplier don't undo that.
        var generator = Make().NullProbability(0.5).CollectionSize(1, 1).With(s => s.Lines.Each().Supplier!.City, "Bergen").VerifyRules();

        generator.Generate(60).Count.ShouldBe(60);
        for (var i = 0; i < 10; i++)
            generator.Generate();
    }

    [Test]
    public void The_message_explains_the_usual_causes()
    {
        var generator = Make().Subtype<Pet, Cat>().VerifyRules();

        var message = Should.Throw<InvalidOperationException>(() => generator.Generate()).Message;

        message.ShouldContain("Statement");
        message.ShouldContain("VerifyRules(false)");
    }

    [Test]
    public void Verification_can_be_switched_off_again_and_generators_stay_immutable()
    {
        var verified = Make().Subtype<Pet, Cat>().VerifyRules();
        var relaxed = verified.VerifyRules(false);

        relaxed.Generate(2).Count.ShouldBe(2);
        Should.Throw<InvalidOperationException>(() => verified.Generate(2));
    }

    [Test]
    public void A_generator_that_verifies_cannot_be_used_as_a_nested_generator_because_nobody_would_check_it()
    {
        var verified = Fake.For<Contact>().VerifyRules();

        Should.Throw<ArgumentException>(() => Make().With(s => s.Billing.Contact, verified)).Message.ShouldContain("VerifyRules(false)");
        Should.Throw<ArgumentException>(() => Make().Use(verified)).Message.ShouldContain("VerifyRules(false)");
        Should.Throw<ArgumentException>(() => Fake.Generators.Add(verified)).Message.ShouldContain("VerifyRules(false)");
        Make().Use(verified.VerifyRules(false)).Generate(2).Count.ShouldBe(2);
    }

    [Test]
    public void Invalid_and_boundary_cases_are_not_verified_because_each_case_replaces_a_rule()
    {
        var generator = Fake.For<Line>().Seed(1).Subtype<Pet, Cat>().VerifyRules(); // the subtype is never needed, which would fail a verified Generate

        generator.InvalidCases().ShouldNotBeEmpty();
        generator.BoundaryCases().ShouldNotBeEmpty();
    }

    [Test]
    public void Generated_models_are_verified_the_same_way()
    {
        var used = TestFakes.Member.Seed(2)
            .With(m => m.Address.City, "Oslo")
            .With(m => m.Memberships.Each().CompanyName, "Acme")
            .VerifyRules()
            .Generate(5);
        used.ShouldAllBe(m => m.Address.City == "Oslo");

        var unused = TestFakes.Member.Seed(2).CollectionSize(0, 0).With(m => m.Memberships.Each().CompanyName, "Acme").VerifyRules();
        Should.Throw<InvalidOperationException>(() => unused.Generate(5)).Message.ShouldContain("Member.Memberships.CompanyName");
    }

    [Test]
    public void Parallel_generation_counts_usage_from_every_thread()
    {
        Make().With(s => s.Lines.Each().Sku, "SKU").Subtype<IShape, Circle>().VerifyRules().GenerateParallel(200).Length.ShouldBe(200);
    }
}
