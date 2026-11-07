using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Phoney.Generation;

namespace Phoney.SourceGenerator;

/// <summary>What a rule's member selector (<c>x =&gt; x.Name</c>) turned out to be.</summary>
internal enum SelectorKind
{
    /// <summary>Not a lambda in the source (a variable or a call), so nothing can be said about it.</summary>
    Unknown,

    /// <summary>A lambda that doesn't select a member path (<c>x =&gt; x.Name.ToUpper()</c>, or a member of a value like <c>x =&gt; x.Name.Length</c>).</summary>
    NotDirect,

    /// <summary>
    /// A lambda selecting a member of its parameter, also through a cast (<c>x =&gt; (object)x.Age</c>), or a path through nested objects
    /// (<c>x =&gt; x.Address.City</c>, <c>x =&gt; x.Lines.Each().Sku</c>).
    /// </summary>
    Member,

    /// <summary>An anonymous type of members of the parameter (<c>x =&gt; new { x.UserId, x.RoleId }</c>), which only <c>Unique</c> accepts.</summary>
    Combination,
}

/// <summary>The member selector of a rule call, like <c>MemberName</c> in <c>ObjectGenerator&lt;T&gt;</c> reads it at runtime.</summary>
/// <param name="Kind">What the selector is.</param>
/// <param name="Name">For <see cref="SelectorKind.Member"/>: the name of the member of the generated type the path starts with.</param>
/// <param name="Location">Where the selector argument is.</param>
/// <param name="Path">For <see cref="SelectorKind.Member"/>: all member names joined with dots (<c>Address.City</c>); the same as <paramref name="Name"/> for a direct member.</param>
internal readonly record struct RuleSelector(SelectorKind Kind, string? Name, Location Location, string? Path = null)
{
    /// <summary>Whether the selector names a member of a nested object rather than of the generated type.</summary>
    public bool IsNested => Path is not null && Path != Name;

    /// <summary>Reads the first argument of <paramref name="call"/> (<c>With</c>, <c>Ignore</c>, <c>Unique</c>, <c>WithOneOf</c>).</summary>
    public static RuleSelector Read(IInvocationOperation call)
    {
        var argument = call.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0);
        if (argument is null)
            return new RuleSelector(SelectorKind.Unknown, null, call.Syntax.GetLocation());

        var location = argument.Syntax.GetLocation();
        if (Lambda(argument.Value) is not { } lambda)
            return new RuleSelector(SelectorKind.Unknown, null, location);

        // Expression-bodied lambdas are a block with one implicit return.
        if (lambda.Body.Operations.Length != 1 || lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } returned })
            return new RuleSelector(SelectorKind.NotDirect, null, location);
        while (returned is IConversionOperation cast)
            returned = cast.Operand;

        var parameter = lambda.Symbol.Parameters.FirstOrDefault();
        if (returned is IAnonymousObjectCreationOperation combination)
            return ReadCombination(combination, parameter, location);
        return ReadPath(returned, parameter, location);
    }

    /// <summary>The lambda an argument passes, looking through conversions and delegate creations, or <see langword="null"/> when it is not one.</summary>
    private static IAnonymousFunctionOperation? Lambda(IOperation value)
    {
        while (true)
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    value = conversion.Operand;
                    break;
                case IDelegateCreationOperation creation:
                    value = creation.Target;
                    break;
                default:
                    return value as IAnonymousFunctionOperation;
            }
        }
    }

    /// <summary><c>x =&gt; x.Billing.City</c> (and <c>x.Lines.Each().Sku</c>): a <see cref="SelectorKind.Member"/> when it is a chain of members of the parameter.</summary>
    private static RuleSelector ReadPath(IOperation returned, IParameterSymbol? parameter, Location location)
    {
        var names = new List<string>();
        var current = returned;
        while (!(current is IParameterReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter)))
        {
            IOperation? owner;
            switch (current)
            {
                case IConversionOperation conversion:
                    current = conversion.Operand;
                    continue;
                case IPropertyReferenceOperation { Property.IsIndexer: true }:
                    return new RuleSelector(SelectorKind.NotDirect, null, location); // x.Lines[0]: which element? Each() means all of them
                case IPropertyReferenceOperation property:
                    owner = property.Instance;
                    if (!IsTransparent(property.Property, owner?.Type))
                        names.Add(property.Property.Name);
                    break;
                case IFieldReferenceOperation field:
                    owner = field.Instance;
                    names.Add(field.Field.Name);
                    break;
                case IInvocationOperation { TargetMethod: { Name: "Each", ContainingType: { Name: "FakePath" } } } each when each.Arguments.Length == 1:
                    current = each.Arguments[0].Value;
                    continue;
                default:
                    return new RuleSelector(SelectorKind.NotDirect, null, location);
            }

            // A member of a string, number or date (x.Name.Length) is not something Phoney generates; only objects have members to give rules to.
            if (owner is null || (!(owner is IParameterReferenceOperation) && owner.Type is { } ownerType && ModelBuilder.Category(ownerType) != TypeCategory.Other))
                return new RuleSelector(SelectorKind.NotDirect, null, location);
            current = owner;
        }

        if (names.Count == 0)
            return new RuleSelector(SelectorKind.NotDirect, null, location);
        names.Reverse();
        return new RuleSelector(SelectorKind.Member, names[0], location, string.Join(".", names));
    }

    /// <summary><c>x =&gt; new { x.UserId, x.RoleId }</c>: a <see cref="SelectorKind.Combination"/> when every part is a member of the parameter, like <c>CombinationMembers</c> reads it at runtime.</summary>
    private static RuleSelector ReadCombination(IAnonymousObjectCreationOperation creation, IParameterSymbol? parameter, Location location)
    {
        var names = new List<string>();
        foreach (var initializer in creation.Initializers)
        {
            var value = initializer is ISimpleAssignmentOperation assignment ? assignment.Value : initializer;
            while (value is IConversionOperation conversion)
                value = conversion.Operand;
            var (name, owner) = value switch
            {
                IPropertyReferenceOperation { Property.IsIndexer: false } property => (property.Property.Name, property.Instance),
                IFieldReferenceOperation field => (field.Field.Name, field.Instance),
                _ => (null, null),
            };
            if (name is null || !(owner is IParameterReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter)))
                return new RuleSelector(SelectorKind.NotDirect, null, location);
            names.Add(name);
        }

        return names.Count == 0
            ? new RuleSelector(SelectorKind.NotDirect, null, location)
            : new RuleSelector(SelectorKind.Combination, null, location, string.Join(",", names));
    }

    /// <summary>The values of a dictionary and the value of a nullable are what <c>Each()</c> and the path continue through.</summary>
    private static bool IsTransparent(IPropertySymbol property, ITypeSymbol? owner) =>
        (property.Name == "Values" && owner is INamedTypeSymbol { Name: "Dictionary" or "IDictionary" or "IReadOnlyDictionary" })
        || (property.Name == "Value" && owner is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });
}

/// <summary>Helpers shared by the analyzers.</summary>
internal static class AnalyzerSupport
{
    /// <summary>The generator calls that produce objects, which end a fluent chain (<c>Check</c> generates the objects it tests).</summary>
    public static readonly HashSet<string> Terminals = new(StringComparer.Ordinal) { "Generate", "Stream", "GenerateParallel", "Populate", "GenerateFor", "Pseudonymize", "Check", "CheckAsync" };

    /// <summary>Whether the chain starts a new generator (<c>Fake.For&lt;T&gt;()</c>, <c>new ObjectGenerator&lt;T&gt;(…)</c> or a <c>[FakeFor]</c> property), so no rules can have been added elsewhere.</summary>
    public static bool IsStart(IOperation? receiver, INamedTypeSymbol? fake, INamedTypeSymbol? fakeFor) => receiver switch
    {
        IInvocationOperation { TargetMethod.Name: "For" } call => SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, fake),
        IObjectCreationOperation => true,
        IPropertyReferenceOperation { Property.IsStatic: true } property =>
            fakeFor is not null && property.Property.ContainingType.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass?.OriginalDefinition, fakeFor)),
        _ => false,
    };

    /// <summary>Whether <paramref name="type"/> is <paramref name="definition"/> or a constructed form of it.</summary>
    public static bool IsDefinition(INamedTypeSymbol? type, INamedTypeSymbol? definition) =>
        definition is not null && SymbolEqualityComparer.Default.Equals(type?.OriginalDefinition, definition);

    /// <summary>The method name of a call (<c>Strict</c> in <c>x.Strict()</c>), so a diagnostic doesn't underline the whole chain.</summary>
    public static Location NameLocation(IInvocationOperation call) =>
        call.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
            ? access.Name.GetLocation()
            : call.Syntax.GetLocation();
}

/// <summary>
/// Walks the fluent chain a generator call is in (<c>Fake.For&lt;T&gt;().Seed(1).With(…).Generate(5)</c>): the generator calls before and after it, and what
/// the chain was started from. Every analyzer that reads a chain walks it here, so they agree on where a chain begins and ends.
/// </summary>
internal static class FluentChain
{
    /// <summary>The generator calls earlier in the chain (the receivers of <paramref name="call"/>), nearest first.</summary>
    public static IEnumerable<IInvocationOperation> Earlier(IInvocationOperation call, INamedTypeSymbol generator)
    {
        var receiver = call.Instance;
        while (receiver is IInvocationOperation earlier && AnalyzerSupport.IsDefinition(earlier.TargetMethod.ContainingType, generator))
        {
            yield return earlier;
            receiver = earlier.Instance;
        }
    }

    /// <summary>The generator calls later in the chain (made on the result of <paramref name="call"/>), nearest first.</summary>
    public static IEnumerable<IInvocationOperation> Later(IInvocationOperation call, INamedTypeSymbol generator)
    {
        IOperation current = call;
        while (current.Parent is IInvocationOperation later && later.Instance == current && AnalyzerSupport.IsDefinition(later.TargetMethod.ContainingType, generator))
        {
            yield return later;
            current = later;
        }
    }

    /// <summary>What the chain was started from: the receiver of its first generator call (see <see cref="AnalyzerSupport.IsStart"/>), or <see langword="null"/> for a static call.</summary>
    public static IOperation? Start(IInvocationOperation call, INamedTypeSymbol generator) =>
        Earlier(call, generator).LastOrDefault() is { } first ? first.Instance : call.Instance;
}

/// <summary>
/// One member rule as the analyzers read it: a flat call (<c>.WithOneOf(x =&gt; x.Email, emails)</c>) or one call of a member view
/// (<c>.Member(x =&gt; x.Email, m =&gt; m.OneOf(emails))</c>), named after the generator method it stands for.
/// </summary>
/// <param name="Name">The generator method the rule is (<c>WithOneOf</c> for <c>m.OneOf</c>).</param>
/// <param name="Call">The call with the rule's own arguments: the flat call, or the view call inside the lambda.</param>
/// <param name="InView">Whether the rule is written in a member view.</param>
internal readonly record struct RuleCall(string Name, IInvocationOperation Call, bool InView)
{
    /// <summary>The method name as written (<c>OneOf</c> in a view), for messages.</summary>
    public string Written => Call.TargetMethod.Name;

    /// <summary>Whether the rule reads the object after it is created: <c>With(member, (faker, obj) =&gt; …)</c> or <c>m.From((faker, obj) =&gt; …)</c>.</summary>
    public bool IsDependent =>
        Name == "With" && Call.TargetMethod.Parameters.Length == (InView ? 1 : 2)
        && Call.TargetMethod.Parameters[InView ? 0 : 1].Type is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 3 };
}

/// <summary>
/// The member rules of one generator call: the call itself for a flat rule, the calls in the lambda for <c>Member(selector, m =&gt; …)</c>.
/// </summary>
/// <param name="Selector">The member the rules are for, read from the generator call.</param>
/// <param name="Rules">The rules in the order they apply, or <see langword="null"/> when a view's lambda is not a plain chain (<c>m =&gt; Helper(m)</c>), so its rules can't be known.</param>
internal sealed record RuleGroup(RuleSelector Selector, IReadOnlyList<RuleCall>? Rules)
{
    /// <summary>
    /// The rules of <paramref name="call"/>, or <see langword="null"/> when it adds no member rules: a member rule of <see cref="RuleCatalog.MemberRules"/>,
    /// the member <c>Where(x =&gt; x.Member, condition)</c>, or a <c>Member</c> view.
    /// </summary>
    public static RuleGroup? Of(IInvocationOperation call)
    {
        var method = call.TargetMethod;
        if (method.Name == RuleCatalog.Member && method.Parameters.Length == 2)
            return new RuleGroup(RuleSelector.Read(call), ViewRules(call));
        return RuleCatalog.IsMemberRule(method.Name) || IsMemberCondition(method)
            ? new RuleGroup(RuleSelector.Read(call), [new RuleCall(method.Name, call, InView: false)])
            : null;
    }

    /// <summary>Whether <paramref name="method"/> belongs to the member view (<c>MemberRules&lt;T, TValue&gt;</c> or its extension class).</summary>
    public static bool IsView(IMethodSymbol method) =>
        method.ContainingType is { Name: "MemberRules", ContainingNamespace: { Name: "Phoney", ContainingNamespace.IsGlobalNamespace: true } };

    /// <summary><c>Where(x =&gt; x.Member, condition)</c>: a condition on one member, unlike <c>Where(x =&gt; …)</c> on the whole object.</summary>
    private static bool IsMemberCondition(IMethodSymbol method) =>
        method.Name == "Where" && method.Parameters.Length >= 2 && method.Parameters[0].Type is INamedTypeSymbol { Name: "Expression" };

    /// <summary>The calls of <c>m =&gt; m.A().B()</c> first to last, or <see langword="null"/> when the lambda is anything else.</summary>
    private static IReadOnlyList<RuleCall>? ViewRules(IInvocationOperation member)
    {
        var argument = member.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 1)?.Value;
        while (argument is IConversionOperation or IDelegateCreationOperation)
            argument = argument is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)argument).Target;
        if (argument is not IAnonymousFunctionOperation lambda
            || lambda.Body.Operations.Length != 1 || lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } current })
        {
            return null;
        }

        var rules = new List<RuleCall>();
        while (current is IInvocationOperation call && IsView(call.TargetMethod) && RuleCatalog.ViewRules.TryGetValue(call.TargetMethod.Name, out var name))
        {
            rules.Add(new RuleCall(name, call, InView: true));
            current = call.TargetMethod.IsExtensionMethod ? call.Arguments[0].Value : call.Instance;
        }

        if (!(current is IParameterReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Parameter, lambda.Symbol.Parameters.FirstOrDefault())))
            return null;
        rules.Reverse();
        return rules;
    }
}
