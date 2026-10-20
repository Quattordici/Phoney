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
    /// <summary>The generator calls that produce objects, which end a fluent chain.</summary>
    public static readonly HashSet<string> Terminals = new(StringComparer.Ordinal) { "Generate", "Stream", "GenerateParallel", "Populate", "GenerateFor", "Pseudonymize" };

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
