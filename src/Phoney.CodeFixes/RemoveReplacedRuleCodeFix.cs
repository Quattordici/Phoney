using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Phoney.CodeFixes;

/// <summary>
/// PHONEY008: removes the earlier rule (or <c>WithGeneratorFor</c>/<c>Subtype</c> registration) that a later call in the
/// same chain replaces. The earlier call has no effect, so removing it doesn't change what is generated.
/// </summary>
/// <remarks>The analyzer passes the replaced call as the diagnostic's additional location.</remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveReplacedRuleCodeFix)), Shared]
public sealed class RemoveReplacedRuleCodeFix : CodeFixProvider
{
    private const string Title = "Remove the replaced rule";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("PHONEY008");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var replacedSpan = diagnostic.AdditionalLocations.FirstOrDefault(l => l.IsInSource)?.SourceSpan;
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || replacedSpan is not { } span)
            return;

        var replaced = root.FindNode(span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault(i => i.Span == span);
        if (replaced?.Expression is not MemberAccessExpressionSyntax access)
            return;

        // receiver.With(a)  →  receiver; the call's line break stays, so a chain written one call per line keeps its layout.
        var receiver = access.Expression.WithTrailingTrivia(replaced.GetTrailingTrivia());
        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(replaced, receiver))), Title),
            diagnostic);
    }
}
