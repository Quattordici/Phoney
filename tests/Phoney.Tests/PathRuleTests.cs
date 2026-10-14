using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

// Reflection-only types: no [FakeFor] reaches them, so these tests exercise the reflection model.
public sealed class Statement
{
    public string Number { get; set; } = "";
    public Counterparty Billing { get; set; } = null!;
    public Counterparty Shipping { get; set; } = null!;
    public List<Line> Lines { get; set; } = [];
    public Dictionary<string, Line> ByCode { get; set; } = [];
    public Line[] Spare { get; set; } = [];
    public IShape? Mark { get; set; }
}

public sealed class Counterparty
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public Contact Contact { get; set; } = null!;
}

public sealed class Contact
{
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Label => "read only";
}

public sealed class Line
{
    public string Sku { get; set; } = "";
    [Range(1, 10)] public int Quantity { get; set; }
    public Counterparty? Supplier { get; set; }
}

/// <summary>Rules for members of nested objects: <c>With(x =&gt; x.Billing.City, …)</c> and <c>With(x =&gt; x.Lines.Each().Sku, …)</c>.</summary>
public sealed class PathRuleTests
{
    private static ObjectGenerator<Statement> Make() => Fake.For<Statement>().Seed(1);

    [Test]
    public void A_nested_member_gets_its_own_rule_and_siblings_of_the_same_type_do_not()
    {
        var invoices = Make().With(i => i.Billing.City, "Oslo").Generate(20);

        invoices.ShouldAllBe(i => i.Billing.City == "Oslo");
        invoices.ShouldAllBe(i => i.Shipping.City != "Oslo"); // the same type, another position
        invoices.ShouldAllBe(i => i.Billing.Name.Length > 0); // the rest of the party is generated as usual
    }

    [Test]
    public void Paths_go_as_deep_as_the_graph()
    {
        var invoices = Make().With(i => i.Billing.Contact.Phone, "+47 000").Generate(10);

        invoices.ShouldAllBe(i => i.Billing.Contact.Phone == "+47 000");
        invoices.ShouldAllBe(i => i.Shipping.Contact.Phone != "+47 000");
        invoices.ShouldAllBe(i => i.Billing.Contact.Email.Contains('@'));
    }

    [Test]
    public void Each_reaches_every_element_of_a_list_an_array_and_dictionary_values()
    {
        var invoices = Make()
            .With(i => i.Lines.Each().Sku, "LINE")
            .With(i => i.Spare.Each().Sku, "SPARE")
            .With(i => i.ByCode.Values.Each().Sku, "CODE")
            .Generate(10);

        invoices.SelectMany(i => i.Lines).ShouldNotBeEmpty();
        invoices.SelectMany(i => i.Lines).ShouldAllBe(l => l.Sku == "LINE");
        invoices.SelectMany(i => i.Spare).ShouldNotBeEmpty();
        invoices.SelectMany(i => i.Spare).ShouldAllBe(l => l.Sku == "SPARE");
        invoices.SelectMany(i => i.ByCode.Values).ShouldNotBeEmpty();
        invoices.SelectMany(i => i.ByCode.Values).ShouldAllBe(l => l.Sku == "CODE");
    }

    [Test]
    public void Paths_continue_past_Each_and_tell_positions_apart()
    {
        var invoices = Make().With(i => i.Lines.Each().Supplier!.City, "Bergen").Generate(10);

        invoices.SelectMany(i => i.Lines).ShouldAllBe(l => l.Supplier!.City == "Bergen");
        invoices.ShouldAllBe(i => i.Billing.City != "Bergen"); // a Counterparty elsewhere is untouched
    }

    [Test]
    public void Ignore_factories_and_WithOneOf_work_on_paths()
    {
        var invoices = Make()
            .Ignore(i => i.Billing.Contact)
            .Ignore(i => i.Lines.Each().Quantity)
            .With(i => i.Shipping.City, f => "City " + f.Random.Int(100, 999))
            .WithOneOf(i => i.Spare.Each().Sku, ["A", "B"])
            .Generate(20);

        invoices.ShouldAllBe(i => i.Billing.Contact == null);
        invoices.SelectMany(i => i.Lines).ShouldAllBe(l => l.Quantity == 0);
        invoices.ShouldAllBe(i => i.Shipping.City.StartsWith("City "));
        invoices.SelectMany(i => i.Spare).Select(l => l.Sku).Distinct().Order().ShouldBe(["A", "B"]);
    }

    [Test]
    public void A_nested_generator_can_be_the_value_of_a_path()
    {
        var invoices = Make().With(i => i.Billing.Contact, Fake.For<Contact>().With(c => c.Phone, "9")).Generate(5);

        invoices.ShouldAllBe(i => i.Billing.Contact.Phone == "9");
        invoices.ShouldAllBe(i => i.Shipping.Contact.Phone != "9");
    }

    [Test]
    public void Invalid_and_Boundary_work_on_paths()
    {
        var invalid = Make().Invalid(i => i.Lines.Each().Quantity).Generate(30).SelectMany(i => i.Lines).Select(l => l.Quantity).Distinct().Order();
        var edges = Make().Boundary(i => i.Spare.Each().Quantity).Generate(30).SelectMany(i => i.Spare).Select(l => l.Quantity).Distinct().Order();

        invalid.ShouldBe([0, 11]);
        edges.ShouldBe([1, 10]);
    }

    [Test]
    public void Path_rules_combine_with_rules_for_the_root_and_the_last_rule_for_a_path_wins()
    {
#pragma warning disable PHONEY008 // replacing the earlier rule is what this test is about
        var invoice = Make().With(i => i.Number, "INV-1").With(i => i.Billing.City, "Oslo").With(i => i.Billing.City, "Rome").Generate();
#pragma warning restore PHONEY008

        invoice.Number.ShouldBe("INV-1");
        invoice.Billing.City.ShouldBe("Rome");
    }

    [Test]
    public void Path_rules_are_reproducible_with_a_seed()
    {
        ObjectGenerator<Statement> Configure() => Make().With(i => i.Billing.City, f => f.Location.City()).With(i => i.Lines.Each().Sku, f => f.Commerce.ProductName());

        string Describe(Statement i) => $"{i.Number}|{i.Billing.City}|{i.Shipping.City}|{string.Join(",", i.Lines.Select(l => l.Sku + l.Quantity))}";

        Configure().Generate(10).Select(Describe).ShouldBe(Configure().Generate(10).Select(Describe));
        Configure().GenerateParallel(10).Select(Describe).ShouldBe(Configure().Generate(10).Select(Describe));
    }

    [Test]
    public void Generators_stay_immutable()
    {
        var plain = Make();
        var withRule = plain.With(i => i.Billing.City, "Oslo");

        withRule.ShouldNotBeSameAs(plain);
        plain.Generate(10).ShouldAllBe(i => i.Billing.City != "Oslo");
    }

    [Test]
    public void Populate_creates_missing_nested_objects_with_the_path_rules()
    {
        var invoice = Make().With(i => i.Billing.City, "Oslo").With(i => i.Lines.Each().Sku, "LINE").Populate(new Statement { Number = "Kept" });

        invoice.Number.ShouldBe("Kept");
        invoice.Billing.City.ShouldBe("Oslo");
        invoice.Lines.ShouldAllBe(l => l.Sku == "LINE");
    }

    [Test]
    public void A_type_generator_creates_the_whole_object_so_its_path_rules_do_not_apply()
    {
        var invoice = Make().WithGeneratorFor(Fake.For<Counterparty>().With(p => p.City, "Rome")).With(i => i.Billing.City, "Oslo").Generate();

        invoice.Billing.City.ShouldBe("Rome"); // Use is self-contained, like a generator given to With
    }

    [Test]
    public void A_rule_for_the_member_itself_replaces_the_object_so_rules_inside_it_do_not_apply()
    {
        var invoice = Make().With(i => i.Billing, Fake.For<Counterparty>().With(p => p.City, "Rome")).With(i => i.Billing.City, "Oslo").Generate();

        invoice.Billing.City.ShouldBe("Rome");
    }

#pragma warning disable PHONEY007 // the analyzer reports these at compile time; the tests check what happens without it
    [Test]
    public void Wrong_paths_are_reported_when_the_generator_is_first_used()
    {
        var readOnly = Should.Throw<InvalidOperationException>(() => Make().With(i => i.Billing.Contact.Label, "x").Generate());
        readOnly.Message.ShouldContain("Contact.Label");
        readOnly.Message.ShouldContain("settable");

        var scalar = Should.Throw<InvalidOperationException>(() => Make().With(i => i.Number.Length, 3).Generate());
        scalar.Message.ShouldContain("Statement.Number");
        scalar.Message.ShouldContain("not an object");

        var invalid = Should.Throw<InvalidOperationException>(() => Make().Invalid(i => i.Billing.City).Generate());
        invalid.Message.ShouldContain("Counterparty.City"); // no annotations to break
    }

    [Test]
    public void Selectors_that_are_not_paths_are_rejected_with_a_helpful_message()
    {
        Should.Throw<ArgumentException>(() => Make().With(i => i.Lines[0].Sku, "x")).Message.ShouldContain("Each()");
        Should.Throw<ArgumentException>(() => Make().With(i => i.Number.ToUpper(), "x")).Message.ShouldContain("Each()");
        Should.Throw<ArgumentException>(() => Make().Unique(i => i.Billing.City)).Message.ShouldContain("directly");
        Should.Throw<ArgumentException>(() => Make().With(i => i.Billing.City, (f, i) => i.Number)).Message.ShouldContain("depend on the object");
    }

#pragma warning restore PHONEY007

    [Test]
    public void Each_is_only_a_marker_inside_an_expression()
    {
        Should.Throw<InvalidOperationException>(() => new List<Line>().Each()).Message.ShouldContain("rule");
    }
}
