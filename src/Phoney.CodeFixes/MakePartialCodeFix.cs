using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace Phoney.CodeFixes;

/// <summary>PHONEY001: makes the class with <c>[FakeFor]</c>, and the types containing it, <see langword="partial"/>.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakePartialCodeFix)), Shared]
public sealed class MakePartialCodeFix : CodeFixProvider
{
    /// <summary>One key for every class, so "Fix all" applies the fix to all of them rather than only to classes with the same name.</summary>
    private const string EquivalenceKey = "Phoney.MakePartial";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("PHONEY001");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var declaration = root?.FindNode(context.Span).AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (declaration is null)
            return;

        var title = declaration.Ancestors().OfType<TypeDeclarationSyntax>().Any(NeedsPartial)
            ? $"Make '{declaration.Identifier.ValueText}' and its containing types partial"
            : $"Make '{declaration.Identifier.ValueText}' partial";
        context.RegisterCodeFix(
            CodeAction.Create(title, ct => MakePartialAsync(context.Document, declaration, ct), equivalenceKey: EquivalenceKey),
            context.Diagnostics);
    }

    private static bool NeedsPartial(TypeDeclarationSyntax declaration) => !declaration.Modifiers.Any(m => m.Text == "partial");

    private static async Task<Document> MakePartialAsync(Document document, TypeDeclarationSyntax declaration, CancellationToken cancellationToken)
    {
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var generator = SyntaxGenerator.GetGenerator(document);
        var targets = declaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Where(NeedsPartial).ToList();

        // Nested replacements are applied innermost first by ReplaceNodes, so the updated node is the one to change.
        var updated = root.ReplaceNodes(
            targets,
            (_, rewritten) => generator.WithModifiers(rewritten, generator.GetModifiers(rewritten).WithPartial(true)).WithAdditionalAnnotations(Formatter.Annotation));
        return document.WithSyntaxRoot(updated);
    }
}
