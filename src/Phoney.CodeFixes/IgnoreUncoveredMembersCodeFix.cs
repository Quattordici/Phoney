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

namespace Phoney.CodeFixes;

/// <summary>
/// PHONEY004: adds <c>.Ignore(x =&gt; x.Member)</c> before <c>.Strict()</c> for every member the strict generator has no
/// rule, convention or annotation for, so they keep their default value instead of making the generator throw.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IgnoreUncoveredMembersCodeFix)), Shared]
public sealed class IgnoreUncoveredMembersCodeFix : CodeFixProvider
{
    /// <summary>The diagnostic property with the member names (<c>StrictAnalyzer.MembersProperty</c>).</summary>
    private const string MembersProperty = "Members";

    private const string EquivalenceKey = "Phoney.IgnoreUncoveredMembers";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("PHONEY004");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue(MembersProperty, out var joined) || string.IsNullOrEmpty(joined))
            return;
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || CodeFixSupport.InvocationAt(root, context.Span) is not { Expression: MemberAccessExpressionSyntax } strict)
            return;

        var members = joined!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        var title = members.Length == 1 ? $"Ignore '{members[0]}'" : $"Ignore the {members.Length} members without a value";
        context.RegisterCodeFix(
            CodeAction.Create(title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(strict, AddIgnores(strict, members)))), EquivalenceKey),
            diagnostic);
    }

    /// <summary><c>receiver.Strict()</c> → <c>receiver.Ignore(x =&gt; x.A).Ignore(x =&gt; x.B).Strict()</c>, one call per line when the chain is written that way.</summary>
    private static InvocationExpressionSyntax AddIgnores(InvocationExpressionSyntax strict, string[] members)
    {
        var access = (MemberAccessExpressionSyntax)strict.Expression;
        var lineEnd = access.Expression.GetTrailingTrivia();
        var indentation = access.OperatorToken.LeadingTrivia;

        var chain = access.Expression;
        foreach (var member in members)
        {
            var selector = SyntaxFactory.ParseExpression($"x => x.{CodeFixSupport.Identifier(member)}");
            chain = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        chain,
                        SyntaxFactory.Token(SyntaxKind.DotToken).WithLeadingTrivia(indentation),
                        SyntaxFactory.IdentifierName("Ignore")),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(selector))))
                .WithTrailingTrivia(lineEnd);
        }

        return strict.WithExpression(access.WithExpression(chain));
    }
}
