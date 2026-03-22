using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Clip.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InvalidFieldsArgumentCodeFix))]
[Shared]
public sealed class InvalidFieldsArgumentCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticIds.InvalidFieldsArgument];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // The diagnostic location is on the argument syntax
        var argument = node.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Wrap in anonymous type",
                ct => WrapInAnonymousTypeAsync(context.Document, argument, ct),
                equivalenceKey: DiagnosticIds.InvalidFieldsArgument),
            diagnostic);
    }

    private static async Task<Document> WrapInAnonymousTypeAsync(
        Document document,
        ArgumentSyntax argument,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var expression = argument.Expression;
        var keyName = DeriveKeyName(expression);

        // Build: new { keyName = expression }
        AnonymousObjectCreationExpressionSyntax anonymousType;

        if (expression is IdentifierNameSyntax && keyName != "value")
        {
            // Simple identifier like `count` → new { count } (shorthand)
            anonymousType = SyntaxFactory.AnonymousObjectCreationExpression(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(expression)));
        }
        else
        {
            // Literal or complex expression → new { value = expr }
            anonymousType = SyntaxFactory.AnonymousObjectCreationExpression(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(
                        SyntaxFactory.NameEquals(keyName),
                        expression)));
        }

        var newArgument = argument.WithExpression(anonymousType);
        var newRoot = root.ReplaceNode(argument, newArgument);
        return document.WithSyntaxRoot(newRoot);
    }

    private static string DeriveKeyName(ExpressionSyntax expression)
    {
        // If the expression is a simple identifier, use it as the key
        if (expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.Text;

        // For member access like obj.Count, use the member name
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.Text;

        return "value";
    }
}
