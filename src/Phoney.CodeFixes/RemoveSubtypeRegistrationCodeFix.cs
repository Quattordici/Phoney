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
/// PHONEY009: removes a <c>[FakeSubtype&lt;TBase, TSubtype&gt;]</c> whose subtype is abstract or an interface. The generator
/// already leaves that registration out, so removing it doesn't change what is generated.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveSubtypeRegistrationCodeFix)), Shared]
public sealed class RemoveSubtypeRegistrationCodeFix : CodeFixProvider
{
    private const string EquivalenceKey = "Phoney.RemoveSubtypeRegistration";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("PHONEY009");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var attribute = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (root is null || attribute?.Parent is not AttributeListSyntax list)
            return;

        context.RegisterCodeFix(
            CodeAction.Create($"Remove '[{attribute}]'", _ => Task.FromResult(context.Document.WithSyntaxRoot(Remove(root, list, attribute))), EquivalenceKey),
            context.Diagnostics);
    }

    /// <summary>
    /// Removes <paramref name="attribute"/>; the only attribute in its list takes the brackets with it, keeping the leading trivia
    /// (indentation, doc comments). <c>RemoveNode</c> only returns <see langword="null"/> for the root, which an attribute never is.
    /// </summary>
    private static SyntaxNode Remove(SyntaxNode root, AttributeListSyntax list, AttributeSyntax attribute) =>
        list.Attributes.Count == 1
            ? root.RemoveNode(list, SyntaxRemoveOptions.KeepLeadingTrivia)!
            : root.RemoveNode(attribute, SyntaxRemoveOptions.KeepNoTrivia)!;
}
