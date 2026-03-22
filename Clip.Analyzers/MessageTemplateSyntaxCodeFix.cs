using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Clip.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MessageTemplateSyntaxCodeFix))]
[Shared]
public sealed class MessageTemplateSyntaxCodeFix : CodeFixProvider
{
    private static readonly Regex TemplatePlaceholder = new(
        @"(?<!\{)\{([A-Za-z_][A-Za-z0-9_]*)\}(?!\})",
        RegexOptions.Compiled);

    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticIds.MessageTemplateSyntax];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        var argument = node.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument == null) return;

        var invocation = argument.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return;

        if (argument.Expression is not LiteralExpressionSyntax literal) return;
        if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Move template placeholder to fields",
                ct => MoveToFieldsAsync(context.Document, invocation, literal, ct),
                equivalenceKey: DiagnosticIds.MessageTemplateSyntax),
            diagnostic);
    }

    private static async Task<Document> MoveToFieldsAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        LiteralExpressionSyntax messageLiteral,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var message = messageLiteral.Token.ValueText;
        var matches = TemplatePlaceholder.Matches(message);
        if (matches.Count == 0) return document;

        // Extract placeholder names
        var placeholderNames = new List<string>();
        foreach (Match match in matches)
            placeholderNames.Add(match.Groups[1].Value);

        // Strip placeholders from message
        var cleanMessage = TemplatePlaceholder.Replace(message, "");
        while (cleanMessage.Contains("  "))
            cleanMessage = cleanMessage.Replace("  ", " ");
        cleanMessage = cleanMessage.Trim();

        var newMessageLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(cleanMessage));

        var args = invocation.ArgumentList.Arguments;
        var newArgs = SyntaxFactory.SeparatedList<ArgumentSyntax>();

        // First arg: cleaned message
        newArgs = newArgs.Add(SyntaxFactory.Argument(newMessageLiteral));

        // Find the fields argument index (skip exception arg if Error overload)
        var fieldsIndex = 1;
        if (args.Count >= 2)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel != null)
            {
                var typeInfo = semanticModel.GetTypeInfo(args[1].Expression, cancellationToken);
                if (IsExceptionType(typeInfo.Type))
                {
                    newArgs = newArgs.Add(args[1]);
                    fieldsIndex = 2;
                }
            }
        }

        // Determine fields argument based on what exists
        ArgumentSyntax fieldsArg;

        if (fieldsIndex < args.Count
            && args[fieldsIndex].Expression is AnonymousObjectCreationExpressionSyntax)
        {
            // Case B: existing anonymous type — keep fields as-is, just strip message
            fieldsArg = args[fieldsIndex];
        }
        else if (fieldsIndex < args.Count)
        {
            // Case A: raw value (e.g., 123) — pair with first placeholder name
            var existingExpr = args[fieldsIndex].Expression;
            var members = new SeparatedSyntaxList<AnonymousObjectMemberDeclaratorSyntax>();

            // Pair first placeholder with the existing value
            members = members.Add(
                SyntaxFactory.AnonymousObjectMemberDeclarator(
                    SyntaxFactory.NameEquals(placeholderNames[0]),
                    existingExpr.WithoutTrivia()));

            // Remaining placeholders become Name = Name (best effort)
            for (var i = 1; i < placeholderNames.Count; i++)
            {
                members = members.Add(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(
                        SyntaxFactory.NameEquals(placeholderNames[i]),
                        SyntaxFactory.IdentifierName(placeholderNames[i])));
            }

            fieldsArg = SyntaxFactory.Argument(
                SyntaxFactory.AnonymousObjectCreationExpression(members));
        }
        else
        {
            // Case C: no fields arg — create new { Name = Name } as best effort
            var members = new SeparatedSyntaxList<AnonymousObjectMemberDeclaratorSyntax>();
            foreach (var name in placeholderNames)
            {
                members = members.Add(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(
                        SyntaxFactory.NameEquals(name),
                        SyntaxFactory.IdentifierName(name)));
            }

            fieldsArg = SyntaxFactory.Argument(
                SyntaxFactory.AnonymousObjectCreationExpression(members));
        }

        newArgs = newArgs.Add(fieldsArg);

        var newInvocation = invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(newArgs));

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsExceptionType(ITypeSymbol? type)
    {
        var current = type;
        while (current != null)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
