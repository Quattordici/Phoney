using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.CodeFixes;

/// <summary>
/// PHONEY005: assigns a discarded result back to where it came from: <c>gen.With(...);</c> becomes
/// <c>gen = gen.With(...);</c>, and <c>gen.Populate(point);</c> on a struct becomes <c>point = gen.Populate(point);</c>.
/// Offered only when that variable, field or property can be assigned the result, and its receiver (if any) is read
/// without side effects, so writing it on both sides of <c>=</c> doesn't evaluate a call or getter twice.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseReturnedValueCodeFix)), Shared]
public sealed class UseReturnedValueCodeFix : CodeFixProvider
{
    private const string EquivalenceKey = "Phoney.UseReturnedValue";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("PHONEY005");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var cancellationToken = context.CancellationToken;
        var root = await context.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null ||
            CodeFixSupport.InvocationAt(root, context.Span) is not { Parent: ExpressionStatementSyntax statement } invocation ||
            model.GetOperation(invocation, cancellationToken) is not IInvocationOperation call ||
            Target(call) is not { } target ||
            !CanAssign(model, target, call, invocation.SpanStart))
        {
            return;
        }

        var targetSyntax = (ExpressionSyntax)target.Syntax;
        var name = targetSyntax.WithoutTrivia().ToString();
        context.RegisterCodeFix(
            CodeAction.Create($"Assign the result to '{name}'", _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(statement, Assign(statement, invocation, targetSyntax)))), EquivalenceKey),
            context.Diagnostics);
    }

    /// <summary>
    /// What receives the result: the struct passed to <c>Populate</c>, or the generator the fluent chain starts from
    /// (<see langword="null"/> when the chain starts from a call such as <c>Fake.For&lt;T&gt;()</c>).
    /// </summary>
    private static IOperation? Target(IInvocationOperation call)
    {
        if (call.TargetMethod.Name == "Populate")
            return Unwrap(call.Arguments.FirstOrDefault(a => a.Parameter?.Name == "instance")?.Value);

        var receiver = call.Instance;
        while (receiver is IInvocationOperation earlier)
            receiver = earlier.Instance;
        return Unwrap(receiver);
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }

    /// <summary>Whether <paramref name="target"/> is a variable, field or property that can take the call's result here.</summary>
    private static bool CanAssign(SemanticModel model, IOperation target, IInvocationOperation call, int position)
    {
        var assignable = target switch
        {
            ILocalReferenceOperation { Local: var local } => !local.IsConst && !local.IsFixed && !local.IsForEach && !local.IsUsing && local.RefKind != RefKind.RefReadOnly,
            IParameterReferenceOperation { Parameter: var parameter } => parameter.RefKind is RefKind.None or RefKind.Ref or RefKind.Out,
            IFieldReferenceOperation { Field: var field } => !field.IsReadOnly && !field.IsConst,
            IPropertyReferenceOperation { Property: { IsIndexer: false, SetMethod: { IsInitOnly: false } setter } } => model.IsAccessible(position, setter),
            _ => false,
        };
        return assignable && HasStableReceiver(target) && target.Type is { } targetType && call.Type is { } resultType &&
            model.Compilation.ClassifyCommonConversion(resultType, targetType).IsImplicit;
    }

    /// <summary>
    /// Whether the receiver of a field or property target can be written twice (<c>target = …(target)</c>) without changing
    /// behaviour: none, <c>this</c>/<c>base</c>, a local, a parameter, or a field read on one of those. Calls and property
    /// getters may have side effects or return another object each time, so <c>GetHolder().Value</c> gets no fix.
    /// </summary>
    private static bool HasStableReceiver(IOperation target)
    {
        var receiver = target switch
        {
            IFieldReferenceOperation field => field.Instance,
            IPropertyReferenceOperation property => property.Instance,
            _ => null,
        };
        while (receiver is IFieldReferenceOperation field)
            receiver = field.Instance;
        return receiver is null or ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation;
    }

    /// <summary><c>call;</c> → <c>target = call;</c>, keeping the statement's trivia and the layout of the call.</summary>
    private static ExpressionStatementSyntax Assign(ExpressionStatementSyntax statement, InvocationExpressionSyntax invocation, ExpressionSyntax target)
    {
        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            target.WithoutTrivia().WithLeadingTrivia(invocation.GetLeadingTrivia()),
            SyntaxFactory.Token(SyntaxKind.EqualsToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
            invocation.WithoutLeadingTrivia());
        return statement.WithExpression(assignment);
    }
}
