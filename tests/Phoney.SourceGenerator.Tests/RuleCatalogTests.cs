using System.Linq.Expressions;
using System.Reflection;
using Phoney.Generation;

namespace Phoney.SourceGenerator.Tests;

/// <summary>The analyzers read one list of the generator's fluent rule methods; it must stay the list <see cref="ObjectGenerator{T}"/> really has.</summary>
public sealed class RuleCatalogTests
{
    private static readonly MethodInfo[] GeneratorMethods =
        [.. typeof(ObjectGenerator<>).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)];

    /// <summary>Methods that take a member selector but are not rules for the member's value: they have their own handling in the analyzers.</summary>
    private static readonly string[] OtherSelectorMethods = ["Where", "WithParentKey"];

    private static bool TakesMemberSelector(MethodInfo method) =>
        method.GetParameters() is [{ ParameterType: { IsGenericType: true } first }, ..] && first.GetGenericTypeDefinition() == typeof(Expression<>);

    [Test]
    public void Every_cataloged_rule_is_a_method_of_the_generator()
    {
        var names = GeneratorMethods.Select(m => m.Name).ToHashSet();

        foreach (var name in RuleCatalog.MemberRules.Concat(RuleCatalog.TypeRules))
            names.ShouldContain(name);
    }

    [Test]
    public void Every_generator_method_that_selects_a_member_is_cataloged_or_known()
    {
        var selecting = GeneratorMethods.Where(TakesMemberSelector).Select(m => m.Name).Distinct().Order(StringComparer.Ordinal).ToList();

        selecting.Except(RuleCatalog.MemberRules).Except(OtherSelectorMethods).ShouldBeEmpty(
            "a new rule method that takes a member selector must be added to RuleCatalog.MemberRules (or to OtherSelectorMethods here, with its own analyzer handling)");
        RuleCatalog.MemberRules.Except(selecting).ShouldBeEmpty("a cataloged member rule that takes no member selector");
    }

    [Test]
    public void A_slot_rule_is_a_member_rule_except_Unique_which_is_kept_apart()
    {
        RuleCatalog.SlotRules.ShouldBe(RuleCatalog.MemberRules.Where(n => n != "Unique"), ignoreOrder: true);
        RuleCatalog.IsMemberRule("Unique").ShouldBeTrue();
        RuleCatalog.OccupiesSlot("Unique").ShouldBeFalse();
        RuleCatalog.OccupiesSlot("With").ShouldBeTrue();
        RuleCatalog.IsMemberRule("Generate").ShouldBeFalse();
    }
}
