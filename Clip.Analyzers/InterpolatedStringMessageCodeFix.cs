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

namespace Clip.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InterpolatedStringMessageCodeFix))]
[Shared]
public sealed class InterpolatedStringMessageCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticIds.InterpolatedStringMessage];

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

        var interpolated = node.DescendantNodesAndSelf()
            .OfType<InterpolatedStringExpressionSyntax>()
            .FirstOrDefault();
        if (interpolated == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Extract interpolation to fields",
                ct => ExtractToFieldsAsync(context.Document, invocation, interpolated, ct),
                equivalenceKey: DiagnosticIds.InterpolatedStringMessage),
            diagnostic);
    }

    private static async Task<Document> ExtractToFieldsAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        InterpolatedStringExpressionSyntax interpolated,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Extract text parts and interpolation expressions
        var messageParts = new List<string>();
        var fieldExpressions = new List<(string key, ExpressionSyntax expr, bool useShorthand)>();
        var fallbackCounter = 0;

        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                messageParts.Add(text.TextToken.ValueText);
            }
            else if (content is InterpolationSyntax interp)
            {
                var expr = interp.Expression;
                var (key, useShorthand) = DeriveFieldKey(expr, ref fallbackCounter);
                fieldExpressions.Add((key, expr, useShorthand));
            }
        }

        // Build clean message string
        var cleanMessage = string.Join("", messageParts);
        // Clean up double/leading/trailing spaces
        while (cleanMessage.Contains("  "))
            cleanMessage = cleanMessage.Replace("  ", " ");
        cleanMessage = cleanMessage.Trim();

        var newMessageLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(cleanMessage));

        // Build anonymous type
        var members = new SeparatedSyntaxList<AnonymousObjectMemberDeclaratorSyntax>();
        foreach (var (key, expr, useShorthand) in fieldExpressions)
        {
            if (useShorthand)
            {
                members = members.Add(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(expr));
            }
            else
            {
                members = members.Add(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(
                        SyntaxFactory.NameEquals(key), expr));
            }
        }

        var anonymousType = SyntaxFactory.AnonymousObjectCreationExpression(members);

        // Build new argument list
        var args = invocation.ArgumentList.Arguments;
        var newArgs = SyntaxFactory.SeparatedList<ArgumentSyntax>();

        // First arg: cleaned message
        newArgs = newArgs.Add(SyntaxFactory.Argument(newMessageLiteral));

        // Preserve exception argument if present (Error overload)
        if (args.Count >= 2)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel != null)
            {
                var typeInfo = semanticModel.GetTypeInfo(args[1].Expression, cancellationToken);
                if (IsExceptionType(typeInfo.Type))
                    newArgs = newArgs.Add(args[1]);
            }
        }

        // Check for existing anonymous type fields to merge
        var fieldsArgIndex = newArgs.Count; // where fields would be
        if (fieldsArgIndex < args.Count
            && args[fieldsArgIndex].Expression is AnonymousObjectCreationExpressionSyntax existingAnon)
        {
            var merged = existingAnon.Initializers.AddRange(members);
            anonymousType = existingAnon.WithInitializers(merged);
        }

        newArgs = newArgs.Add(SyntaxFactory.Argument(anonymousType));

        var newInvocation = invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(newArgs));

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }

    private static (string key, bool useShorthand) DeriveFieldKey(
        ExpressionSyntax expr, ref int fallbackCounter)
    {
        // Simple identifier: name → new { name }
        if (expr is IdentifierNameSyntax identifier)
            return (identifier.Identifier.Text, true);

        // Member access: user.Name → new { Name = user.Name }
        if (expr is MemberAccessExpressionSyntax memberAccess)
            return (memberAccess.Name.Identifier.Text, false);

        // Fallback: Value1, Value2, etc.
        fallbackCounter++;
        return ($"Value{fallbackCounter}", false);
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
