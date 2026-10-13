using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.SourceGenerator;

/// <summary>What a rule's member selector (<c>x =&gt; x.Name</c>) turned out to be.</summary>
internal enum SelectorKind
{
    /// <summary>Not a lambda in the source (a variable or a call), so nothing can be said about it.</summary>
    Unknown,

    /// <summary>A lambda that doesn't select a member of its parameter directly (<c>x =&gt; x.Address.Street</c>).</summary>
    NotDirect,

    /// <summary>A lambda selecting one member of its parameter, also through a cast (<c>x =&gt; (object)x.Age</c>).</summary>
    Member,
}

/// <summary>The member selector of a rule call, like <c>MemberName</c> in <c>Generator&lt;T&gt;</c> reads it at runtime.</summary>
/// <param name="Kind">What the selector is.</param>
/// <param name="Name">The selected member's name for <see cref="SelectorKind.Member"/>.</param>
/// <param name="Location">Where the selector argument is.</param>
internal readonly record struct RuleSelector(SelectorKind Kind, string? Name, Location Location)
{
    /// <summary>Reads the first argument of <paramref name="call"/> (<c>With</c>, <c>Ignore</c>, <c>Unique</c>, <c>WithOneOf</c>).</summary>
    public static RuleSelector Read(IInvocationOperation call)
    {
        var argument = call.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0);
        if (argument is null)
            return new RuleSelector(SelectorKind.Unknown, null, call.Syntax.GetLocation());

        var location = argument.Syntax.GetLocation();
        var value = argument.Value;
        while (true)
        {
            if (value is IConversionOperation conversion)
                value = conversion.Operand;
            else if (value is IDelegateCreationOperation creation)
                value = creation.Target;
            else
                break;
        }

        if (value is not IAnonymousFunctionOperation lambda)
            return new RuleSelector(SelectorKind.Unknown, null, location);

        // Expression-bodied lambdas are a block with one implicit return.
        if (lambda.Body.Operations.Length != 1 || lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } returned })
            return new RuleSelector(SelectorKind.NotDirect, null, location);
        while (returned is IConversionOperation cast)
            returned = cast.Operand;

        var parameter = lambda.Symbol.Parameters.FirstOrDefault();
        return returned switch
        {
            IPropertyReferenceOperation { Instance: IParameterReferenceOperation p } property when SymbolEqualityComparer.Default.Equals(p.Parameter, parameter) =>
                new RuleSelector(SelectorKind.Member, property.Property.Name, location),
            IFieldReferenceOperation { Instance: IParameterReferenceOperation p } field when SymbolEqualityComparer.Default.Equals(p.Parameter, parameter) =>
                new RuleSelector(SelectorKind.Member, field.Field.Name, location),
            _ => new RuleSelector(SelectorKind.NotDirect, null, location),
        };
    }
}

/// <summary>Helpers shared by the analyzers.</summary>
internal static class AnalyzerSupport
{
    /// <summary>The generator calls that produce objects, which end a fluent chain.</summary>
    public static readonly HashSet<string> Terminals = new(StringComparer.Ordinal) { "Generate", "Stream", "GenerateParallel", "Populate" };

    /// <summary>Whether the chain starts a new generator (<c>Fake.For&lt;T&gt;()</c>, <c>new Generator&lt;T&gt;(…)</c> or a <c>[FakeFor]</c> property), so no rules can have been added elsewhere.</summary>
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
