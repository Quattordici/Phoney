using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.DevAnalyzers;

/// <summary>
/// Reports enumeration of collections whose order is undefined (<c>HashSet</c>, <c>Dictionary</c>, the concurrent, immutable
/// and frozen dictionaries and sets, and their <c>Keys</c>/<c>Values</c>) where nothing sorts the result first.
/// </summary>
/// <remarks>
/// Generated values must be reproducible for a seed, so the order of an enumeration must not decide which value is drawn,
/// which rule runs first or what a result list looks like (hash order of strings differs per process). A <c>foreach</c>, a
/// spread or an order-sensitive LINQ chain (<c>ToList</c>, <c>First</c>, <c>Select</c> into a result…) over such a
/// collection is reported; a chain that sorts (<c>Order</c>, <c>OrderBy</c>) or only asks order-independent questions
/// (<c>Any</c>, <c>Count</c>, <c>Contains</c>, <c>ToHashSet</c>, <c>ToDictionary</c>, <c>Sum</c>…) is not. Interfaces
/// (<c>IDictionary</c>, <c>ISet</c>) are not tracked. Where the order really can't matter, suppress with a comment that says why.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnorderedIterationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "PHONEYDEV001",
        "Enumerating an unordered collection",
        "Enumerating '{0}' gives no defined order. Sort it first (Order/OrderBy with an ordinal comparer), keep an ordered list next to it, or suppress with a comment that says why the order can't matter.",
        "Phoney.Development",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly HashSet<string> UnorderedTypes = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Concurrent.ConcurrentDictionary`2",
        "System.Collections.Immutable.ImmutableDictionary`2",
        "System.Collections.Immutable.ImmutableHashSet`1",
        "System.Collections.Frozen.FrozenDictionary`2",
        "System.Collections.Frozen.FrozenSet`1",
    };

    /// <summary>LINQ methods that sort, or whose result doesn't depend on the order of the source.</summary>
    private static readonly HashSet<string> OrderIndependent = new(StringComparer.Ordinal)
    {
        "Any", "All", "Count", "LongCount", "Contains", "Order", "OrderBy", "OrderByDescending", "OrderDescending",
        "ToHashSet", "ToDictionary", "ToLookup", "Sum", "Min", "Max", "Average",
    };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeForEach, OperationKind.Loop);
        context.RegisterOperationAction(AnalyzeLinq, OperationKind.Invocation);
        context.RegisterSyntaxNodeAction(AnalyzeSpread, SyntaxKind.SpreadElement);
    }

    private static void AnalyzeForEach(OperationAnalysisContext context)
    {
        if (context.Operation is IForEachLoopOperation loop && UnorderedSource(loop.Collection) is { } type)
            context.ReportDiagnostic(Diagnostic.Create(Rule, loop.Collection.Syntax.GetLocation(), type));
    }

    private static void AnalyzeSpread(SyntaxNodeAnalysisContext context)
    {
        var expression = ((SpreadElementSyntax)context.Node).Expression;
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        if (IsUnorderedType(type) || IsKeysOrValues(context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol))
            context.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation(), type?.Name ?? expression.ToString()));
    }

    /// <summary>A LINQ chain that starts at an unordered collection and never sorts or asks an order-independent question.</summary>
    private static void AnalyzeLinq(OperationAnalysisContext context)
    {
        var root = (IInvocationOperation)context.Operation;
        if (!IsLinq(root) || UnorderedSource(root.Arguments[0].Value) is not { } source)
            return;

        for (IInvocationOperation current = root; ;)
        {
            if (OrderIndependent.Contains(current.TargetMethod.Name))
                return;
            // The call is the receiver (first argument) of the next call of the chain: invocation -> argument -> invocation.
            var parent = current.Parent;
            while (parent is IConversionOperation)
                parent = parent.Parent;
            if (parent is IArgumentOperation { Parent: IInvocationOperation outer } argument && IsLinq(outer) && outer.Arguments[0] == argument)
                current = outer;
            else
                break;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, AnalyzerNameLocation(root), source));
    }

    private static bool IsLinq(IInvocationOperation call) =>
        call.TargetMethod is { IsExtensionMethod: true, ContainingType: { Name: "Enumerable", ContainingNamespace: { Name: "Linq", ContainingNamespace.Name: "System" } } } &&
        call.Arguments.Length > 0;

    /// <summary>The name of the unordered collection type <paramref name="operation"/> evaluates to, or <see langword="null"/>.</summary>
    private static string? UnorderedSource(IOperation operation)
    {
        operation = Unwrap(operation);
        if (operation is IPropertyReferenceOperation { Property: { Name: "Keys" or "Values" } property } && IsUnorderedType(property.ContainingType))
            return property.ContainingType.Name;
        return IsUnorderedType(operation.Type) ? operation.Type!.Name : null;
    }

    private static bool IsKeysOrValues(ISymbol? symbol) =>
        symbol is IPropertySymbol { Name: "Keys" or "Values" } property && IsUnorderedType(property.ContainingType);

    /// <summary>Whether the type, a base type or a type containing it (<c>Dictionary&lt;,&gt;.KeyCollection</c>) is an unordered collection.</summary>
    private static bool IsUnorderedType(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            for (ISymbol? definition = t.OriginalDefinition; definition is INamedTypeSymbol named; definition = named.ContainingType)
            {
                if (UnorderedTypes.Contains(named.ContainingNamespace.ToDisplayString() + "." + named.MetadataName))
                    return true;
            }
        }

        return false;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static Location AnalyzerNameLocation(IInvocationOperation call) =>
        call.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
            ? access.Name.GetLocation()
            : call.Syntax.GetLocation();
}
