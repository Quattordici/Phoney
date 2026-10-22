using System.Collections.Generic;
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

namespace Phoney.CodeFixes;

/// <summary>
/// PHONEY006: turns a <c>Fake.Seeded(...)</c> scope that is never disposed into a <c>using</c> declaration, so the seed
/// ends with the enclosing block: <c>Fake.Seeded(1);</c> and <c>_ = Fake.Seeded(1);</c> become
/// <c>using var _ = Fake.Seeded(1);</c>, and <c>var scope = Fake.Seeded(1);</c> becomes <c>using var scope = …</c>.
/// </summary>
/// <remarks>
/// Only offered where a using declaration is allowed: directly in a block or in top-level statements. No fix-all, since
/// several scopes in one block would each be given the same variable name.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DisposeSeededScopeCodeFix)), Shared]
public sealed class DisposeSeededScopeCodeFix : CodeFixProvider
{
    private const string Title = "Dispose the seeded scope with 'using'";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("PHONEY006");

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var invocation = root is null ? null : CodeFixSupport.InvocationAt(root, context.Span);
        var statement = invocation?.FirstAncestorOrSelf<StatementSyntax>();
        if (root is null || invocation is null || statement?.Parent is not (BlockSyntax or GlobalStatementSyntax))
            return;

        StatementSyntax? replacement = statement switch
        {
            ExpressionStatementSyntax { Expression: var expression } when expression == invocation || IsDiscard(expression, invocation) =>
                UsingDeclaration(statement, invocation, FreeName(statement)),
            LocalDeclarationStatementSyntax { UsingKeyword.RawKind: 0, Declaration.Variables.Count: 1 } declaration
                when declaration.Declaration.Variables[0].Initializer?.Value == invocation => AddUsing(declaration),
            _ => null,
        };
        if (replacement is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(statement, replacement))), Title),
            context.Diagnostics);
    }

    private static bool IsDiscard(ExpressionSyntax expression, InvocationExpressionSyntax invocation) =>
        expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "_" } } assignment && assignment.Right == invocation;

    /// <summary><c>using var name = Fake.Seeded(...);</c> in place of <paramref name="statement"/>, with its trivia.</summary>
    private static LocalDeclarationStatementSyntax UsingDeclaration(StatementSyntax statement, InvocationExpressionSyntax invocation, string name)
    {
        var declaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("var").WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name).WithTrailingTrivia(SyntaxFactory.Space))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(
                        SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
                        invocation.WithoutTrivia()))));
        return SyntaxFactory.LocalDeclarationStatement(declaration)
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithTrailingTrivia(statement.GetTrailingTrivia());
    }

    /// <summary><c>var scope = …;</c> → <c>using var scope = …;</c>, moving the statement's leading trivia to <c>using</c>.</summary>
    private static LocalDeclarationStatementSyntax AddUsing(LocalDeclarationStatementSyntax declaration)
    {
        var leading = declaration.GetLeadingTrivia();
        return declaration.WithoutLeadingTrivia()
            .WithUsingKeyword(SyntaxFactory.Token(leading, SyntaxKind.UsingKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space)));
    }

    /// <summary><c>_</c>, or <c>seeded</c>, <c>seeded2</c>… when the enclosing member already declares that name.</summary>
    private static string FreeName(StatementSyntax statement)
    {
        var body = statement.Ancestors().FirstOrDefault(n => n is MemberDeclarationSyntax or AccessorDeclarationSyntax or CompilationUnitSyntax) ?? statement;
        var taken = new HashSet<string>(body.DescendantNodes().Select(DeclaredName).Where(n => n is not null)!);
        if (!taken.Contains("_"))
            return "_";
        var name = "seeded";
        for (var n = 2; taken.Contains(name); n++)
            name = "seeded" + n;
        return name;
    }

    private static string? DeclaredName(SyntaxNode node) => node switch
    {
        VariableDeclaratorSyntax v => v.Identifier.ValueText,
        SingleVariableDesignationSyntax d => d.Identifier.ValueText,
        ParameterSyntax p => p.Identifier.ValueText,
        ForEachStatementSyntax f => f.Identifier.ValueText,
        CatchDeclarationSyntax c => c.Identifier.ValueText,
        LocalFunctionStatementSyntax l => l.Identifier.ValueText,
        _ => null,
    };
}
