using System;
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
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Phoney.CodeFixes;

/// <summary>
/// PHONEY012: adds <c>[FakeFor&lt;T&gt;]</c> for a type that is generated with reflection, to the partial class that already
/// lists types for the source generator, or to a new class when the project has none.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddFakeForCodeFix)), Shared]
public sealed class AddFakeForCodeFix : CodeFixProvider
{
    private const string NewClassName = "FakeModels";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("PHONEY012");

    /// <inheritdoc />
    /// <remarks>No fix-all: several diagnostics for one type would each add the same attribute.</remarks>
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var invocation = root?.FindNode(context.Span).AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (root is null || model is null || invocation is null)
            return;
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { TypeArguments.Length: 1 } method || method.TypeArguments[0] is not INamedTypeSymbol type)
            return;

        var target = await FindTargetAsync(document.Project, cancellationToken).ConfigureAwait(false);
        if (target is { } found)
        {
            var title = $"Add [FakeFor<{type.Name}>] to '{found.Declaration.Identifier.ValueText}'";
            context.RegisterCodeFix(
                CodeAction.Create(title, ct => AddToExistingAsync(document.Project, found.Document, type, ct), equivalenceKey: title),
                context.Diagnostics);
        }
        else
        {
            var name = await FreeNameAsync(document.Project, cancellationToken).ConfigureAwait(false);
            var title = $"Create '{name}' with [FakeFor<{type.Name}>]";
            context.RegisterCodeFix(
                CodeAction.Create(title, _ => Task.FromResult(CreateClass(document.Project, name, type)), equivalenceKey: title),
                context.Diagnostics);
        }
    }

    /// <summary>The first partial class (by file path) in the project that already carries <c>[FakeFor]</c>.</summary>
    private static async Task<(Document Document, TypeDeclarationSyntax Declaration)?> FindTargetAsync(Project project, CancellationToken cancellationToken)
    {
        foreach (var document in project.Documents.OrderBy(d => d.FilePath ?? d.Name, StringComparer.Ordinal))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var declaration = root?.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax)
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(d => d.Modifiers.Any(SyntaxKind.PartialKeyword) && FakeForAttribute(d) is not null);
            if (declaration is not null)
                return (document, declaration);
        }

        return null;
    }

    /// <summary>The first <c>[FakeFor&lt;T&gt;]</c> attribute on <paramref name="declaration"/>.</summary>
    private static AttributeSyntax? FakeForAttribute(TypeDeclarationSyntax declaration) =>
        declaration.AttributeLists.SelectMany(l => l.Attributes).FirstOrDefault(a => SimpleName(a.Name) is GenericNameSyntax { Identifier.ValueText: "FakeFor" or "FakeForAttribute" });

    private static SimpleNameSyntax? SimpleName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right,
        AliasQualifiedNameSyntax alias => alias.Name,
        SimpleNameSyntax simple => simple,
        _ => null,
    };

    private static async Task<Solution> AddToExistingAsync(Project project, Document document, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        // Resolve again in the current snapshot of the target document.
        var current = project.GetDocument(document.Id)!;
        var root = (await current.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var model = (await current.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false))!;
        var declaration = root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax)
            .OfType<TypeDeclarationSyntax>()
            .First(d => d.Modifiers.Any(SyntaxKind.PartialKeyword) && FakeForAttribute(d) is not null);

        var existing = FakeForAttribute(declaration)!;
        var list = (AttributeListSyntax)existing.Parent!;
        var typeName = SyntaxFactory.ParseTypeName(type.ToMinimalDisplayString(model, declaration.SpanStart));
        var attribute = SyntaxFactory.Attribute(WithArgument(existing.Name, typeName));
        var updated = list.WithAttributes(list.Attributes.Add(attribute)).WithAdditionalAnnotations(Formatter.Annotation);

        return project.Solution.WithDocumentSyntaxRoot(current.Id, root.ReplaceNode(list, updated));
    }

    /// <summary>The attribute name of <paramref name="name"/> (<c>FakeFor&lt;A&gt;</c>, also qualified) with <paramref name="argument"/> as its type argument.</summary>
    private static NameSyntax WithArgument(NameSyntax name, TypeSyntax argument) => name switch
    {
        GenericNameSyntax generic => generic.WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(argument))),
        QualifiedNameSyntax qualified => qualified.WithRight((SimpleNameSyntax)WithArgument(qualified.Right, argument)),
        AliasQualifiedNameSyntax alias => alias.WithName((SimpleNameSyntax)WithArgument(alias.Name, argument)),
        _ => name,
    };

    private static Solution CreateClass(Project project, string name, INamedTypeSymbol type)
    {
        var typeName = type.ToDisplayString(new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes));
        var text = "using Phoney.Generation;\n" +
            (type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"using {ns.ToDisplayString()};\n" : "") +
            $"\n[FakeFor<{typeName}>]\ninternal static partial class {name};\n";
        return project.AddDocument(name + ".cs", SourceText.From(text)).Project.Solution;
    }

    /// <summary>A class name nothing in the project uses: <c>FakeModels</c>, else <c>FakeModels2</c>…</summary>
    private static async Task<string> FreeNameAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var name = NewClassName;
        var n = 1;
        while (IsTaken(name))
            name = NewClassName + ++n;
        return name;

        bool IsTaken(string candidate) =>
            compilation?.GetTypeByMetadataName(candidate) is not null || project.Documents.Any(d => d.Name == candidate + ".cs");
    }
}
