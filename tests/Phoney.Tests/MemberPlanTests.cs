using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>
/// <see cref="MemberPlan"/> through its interface: which source gives each member its value, and the root-only policies (uniqueness, conditions, object retries,
/// key parts) on the finished slots, with the errors in the order <c>Compile</c> has always reported them.
/// </summary>
public sealed class MemberPlanTests
{
    private static readonly FakeMember Label = new("Label", typeof(string), ConventionKind.None);
    private static readonly FakeMember Email = new("Email", typeof(string), ConventionKind.Email);
    private static readonly FakeMember Optional = new("Code", typeof(string), ConventionKind.None) { IsConstructorParameter = true, HasDefaultValue = true, DefaultValue = "declared" };

    private static SlotDecision Constant(object? value) => new(RuleKind.Constant, value);

    private static CustomConvention Convention(string value) => new(typeof(string), static _ => true, _ => value);

    private static MemberPlan Build(FakeMember[] members, MemberPlanInputs inputs) => MemberPlan.Build(members, inputs);

    [Test]
    public void A_rule_beats_the_feed_which_beats_a_declared_default_which_beats_a_convention()
    {
        FakeMember[] members = [Optional, Optional, Optional, Optional];
        var plan = Build(members, new MemberPlanInputs
        {
            Rules = new Dictionary<int, SlotDecision> { [0] = Constant("rule") },
            Feed = new Dictionary<int, SlotDecision> { [0] = Constant("feed"), [1] = Constant("feed") },
            ParameterDefaults = true,
            Conventions = [Convention("conv")],
        });

        plan.Slots.Select(s => s!.Kind == RuleKind.Convention ? "conv" : s.Value).ShouldBe(["rule", "feed", "declared", "declared"]);
    }

    [Test]
    public void A_member_nothing_decides_has_no_slot_unless_it_is_unique()
    {
        var plan = Build([Label, Label], new MemberPlanInputs { Unique = new Dictionary<int, int> { [1] = 7 } });

        plan.Slots[0].ShouldBeNull();
        plan.Slots[1].ShouldNotBeNull();
        plan.Slots[1]!.Kind.ShouldBe(RuleKind.Default);
        plan.Slots[1]!.Unique.ShouldBeTrue();
        plan.Slots[1]!.MaxAttempts.ShouldBe(7);
    }

    [Test]
    public void A_unique_or_conditioned_member_gets_no_declared_default_but_takes_a_convention()
    {
        var withoutConvention = Build([Optional, Optional], new MemberPlanInputs
        {
            Unique = new Dictionary<int, int> { [0] = 5 },
            Conditions = new Dictionary<string, MemberCondition> { ["Code"] = new(_ => true, 3) },
            ParameterDefaults = true,
        });
        withoutConvention.Slots[0]!.Kind.ShouldBe(RuleKind.Default);

        var withConvention = Build([Optional], new MemberPlanInputs { Unique = new Dictionary<int, int> { [0] = 5 }, ParameterDefaults = true, Conventions = [Convention("conv")] });
        withConvention.Slots[0]!.Kind.ShouldBe(RuleKind.Convention);
    }

    [Test]
    public void A_condition_is_put_on_the_slot_of_its_member()
    {
        Func<object?, bool> accepts = v => v is "x";
        var plan = Build([Label], new MemberPlanInputs { Conditions = new Dictionary<string, MemberCondition> { ["Label"] = new(accepts, 4) } });

        plan.Slots[0]!.Kind.ShouldBe(RuleKind.Default);
        plan.Slots[0]!.Filter.ShouldBeSameAs(accepts);
        plan.Slots[0]!.FilterAttempts.ShouldBe(4);
    }

    [Test]
    public void A_strict_plan_reports_uncovered_scalars_and_a_condition_alone_is_not_coverage()
    {
        var error = Should.Throw<InvalidOperationException>(() => Build([Label], new MemberPlanInputs
        {
            TypeName = "Thing",
            Strict = true,
            Conditions = new Dictionary<string, MemberCondition> { ["Label"] = new(_ => true, 3) },
        }));

        error.Message.ShouldBe("Strict generator for Thing: no rule or convention for Label. Add .With(...) or .Ignore(...) for them.");
        Should.NotThrow(() => Build([Label], new MemberPlanInputs { Strict = true, Unique = new Dictionary<int, int> { [0] = 3 } }));
    }

    [Test]
    public void A_strict_failure_is_reported_before_a_condition_on_an_unknown_member()
    {
        var error = Should.Throw<InvalidOperationException>(() => Build([Label], new MemberPlanInputs
        {
            TypeName = "Thing",
            Strict = true,
            Conditions = new Dictionary<string, MemberCondition> { ["Missing"] = new(_ => true, 3) },
        }));

        error.Message.ShouldStartWith("Strict generator for Thing");
    }

    [Test]
    public void A_condition_error_is_reported_before_a_combination_error()
    {
        var error = Should.Throw<InvalidOperationException>(() => Build([Label], new MemberPlanInputs
        {
            TypeName = "Thing",
            Conditions = new Dictionary<string, MemberCondition> { ["Missing"] = new(_ => true, 3) },
            UniqueKeys = new Dictionary<string, int> { ["Label,Other"] = 3 },
        }));

        error.Message.ShouldStartWith("Thing.Missing can't be generated");
    }

    [Test]
    public void A_condition_on_an_identity_member_without_a_rule_is_refused()
    {
        var error = Should.Throw<InvalidOperationException>(() => Build([Email], new MemberPlanInputs
        {
            TypeName = "Thing",
            Conditions = new Dictionary<string, MemberCondition> { ["Email"] = new(_ => true, 3) },
        }));

        error.Message.ShouldStartWith("Thing.Email comes from the object's identity");
        Should.NotThrow(() => Build([Email], new MemberPlanInputs
        {
            Rules = new Dictionary<int, SlotDecision> { [0] = Constant("a@b.c") },
            Conditions = new Dictionary<string, MemberCondition> { ["Email"] = new(_ => true, 3) },
        }));
    }

    [Test]
    public void A_unique_identity_member_retries_the_whole_object_and_the_smallest_limit_wins()
    {
        var plan = Build([Email, Label, Label], new MemberPlanInputs
        {
            Unique = new Dictionary<int, int> { [0] = 9 },
            UniqueKeys = new Dictionary<string, int> { ["Label,Label"] = 4 },
        });

        plan.Slots[0]!.RetriesObject.ShouldBeTrue();
        plan.Combinations.Length.ShouldBe(1);
        plan.ObjectRetries.ShouldBe(4);

        Build([Email], new MemberPlanInputs { Unique = new Dictionary<int, int> { [0] = 9 } }).ObjectRetries.ShouldBe(9);
        Build([Email], new MemberPlanInputs { Unique = new Dictionary<int, int> { [0] = 9 }, NameConventions = false }).Slots[0]!.RetriesObject.ShouldBeFalse();
    }

    [Test]
    public void A_constructor_parameter_in_a_combination_records_its_value()
    {
        var parameter = new FakeMember("UserId", typeof(int), ConventionKind.None) { IsConstructorParameter = true };
        var plan = Build([parameter, Label], new MemberPlanInputs { UniqueKeys = new Dictionary<string, int> { ["UserId,Label"] = 3 } });

        plan.Slots[0]!.RecordsKeyPart.ShouldBeTrue();
        plan.Slots[1].ShouldBeNull();

        var error = Should.Throw<InvalidOperationException>(() => Build([parameter, Label], new MemberPlanInputs
        {
            TypeName = "Thing",
            CreatesWithFactory = true,
            UniqueKeys = new Dictionary<string, int> { ["UserId,Label"] = 3 },
        }));
        error.Message.ShouldStartWith("Thing.UserId is a constructor parameter, and CreateWith creates the objects");
    }

    [Test]
    public void Policies_keep_what_the_source_decided()
    {
        var plan = Build([Label], new MemberPlanInputs
        {
            Rules = new Dictionary<int, SlotDecision> { [0] = Constant("rule") },
            Unique = new Dictionary<int, int> { [0] = 2 },
            Conditions = new Dictionary<string, MemberCondition> { ["Label"] = new(_ => true, 6) },
        });

        var slot = plan.Slots[0]!;
        slot.Kind.ShouldBe(RuleKind.Constant);
        slot.Value.ShouldBe("rule");
        slot.Unique.ShouldBeTrue();
        slot.Attempts.ShouldBe(2);
    }
}
