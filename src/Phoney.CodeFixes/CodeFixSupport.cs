using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Phoney.CodeFixes;

/// <summary>Helpers shared by the code fixes.</summary>
internal static class CodeFixSupport
{
    /// <summary>
    /// The invocation a diagnostic reports at <paramref name="span"/>: the analyzers report on the method name of
    /// <c>receiver.Method(...)</c>, or on the whole invocation when it has no receiver.
    /// </summary>
    public static InvocationExpressionSyntax? InvocationAt(SyntaxNode root, TextSpan span) =>
        root.FindNode(span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => (i.Expression is MemberAccessExpressionSyntax access ? access.Name.Span : i.Span) == span || i.Span == span);

    /// <summary><paramref name="name"/> as an identifier, escaped with <c>@</c> when it is a reserved keyword.</summary>
    public static string Identifier(string name) =>
        SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(name)) ? "@" + name : name;
}
