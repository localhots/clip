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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExceptionAsFieldsCodeFix))]
[Shared]
public sealed class ExceptionAsFieldsCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticIds.ExceptionAsFields];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // The diagnostic is on the assignment inside the anonymous type (e.g., Error = ex)
        var anonymousObject = node.FirstAncestorOrSelf<AnonymousObjectCreationExpressionSyntax>();
        if (anonymousObject == null) return;

        var invocation = anonymousObject.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return;

        // Find the specific member that contains the exception
        var member = node.FirstAncestorOrSelf<AnonymousObjectMemberDeclaratorSyntax>()
            ?? node as AnonymousObjectMemberDeclaratorSyntax;
        if (member == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use Error overload with exception",
                ct => ExtractExceptionAsync(context.Document, invocation, anonymousObject, member, ct),
                equivalenceKey: DiagnosticIds.ExceptionAsFields),
            diagnostic);
    }

    private static async Task<Document> ExtractExceptionAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        AnonymousObjectMemberDeclaratorSyntax exceptionMember,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Change method name to Error
        var newExpression = RenameMethod(invocation.Expression, "Error");
        if (newExpression == null) return document;

        // Extract the exception expression, stripping any stale trivia
        var exceptionExpr = exceptionMember.Expression
            .WithoutLeadingTrivia()
            .WithoutTrailingTrivia();

        // Remove the exception member from the anonymous type
        var remainingMembers = anonymousObject.Initializers.Remove(exceptionMember);

        // Build new argument list: message, exception, [fields]
        var args = invocation.ArgumentList.Arguments;
        var newArgs = SyntaxFactory.SeparatedList<ArgumentSyntax>();

        // First arg: message
        if (args.Count > 0)
            newArgs = newArgs.Add(args[0]);

        // Second arg: the exception
        newArgs = newArgs.Add(SyntaxFactory.Argument(exceptionExpr));

        // Third arg: remaining fields (only if there are properties left)
        if (remainingMembers.Count > 0)
        {
            // Rebuild the anonymous type preserving inline formatting
            var cleanMembers = new SeparatedSyntaxList<AnonymousObjectMemberDeclaratorSyntax>();
            foreach (var member in remainingMembers)
            {
                cleanMembers = cleanMembers.Add(
                    member.WithoutLeadingTrivia().WithLeadingTrivia(SyntaxFactory.Space));
            }

            var newAnon = SyntaxFactory.AnonymousObjectCreationExpression(cleanMembers)
                .WithOpenBraceToken(SyntaxFactory.Token(
                    SyntaxFactory.TriviaList(),
                    SyntaxKind.OpenBraceToken,
                    SyntaxFactory.TriviaList()))
                .WithCloseBraceToken(SyntaxFactory.Token(
                    SyntaxFactory.TriviaList(SyntaxFactory.Space),
                    SyntaxKind.CloseBraceToken,
                    SyntaxFactory.TriviaList()));
            newArgs = newArgs.Add(SyntaxFactory.Argument(newAnon));
        }

        var newInvocation = invocation
            .WithExpression(newExpression)
            .WithArgumentList(invocation.ArgumentList.WithArguments(newArgs));

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }

    private static ExpressionSyntax? RenameMethod(ExpressionSyntax expression, string newName)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.WithName(SyntaxFactory.IdentifierName(newName));

        if (expression is IdentifierNameSyntax)
            return SyntaxFactory.IdentifierName(newName);

        return null;
    }
}
