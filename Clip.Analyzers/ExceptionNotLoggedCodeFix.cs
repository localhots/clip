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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExceptionNotLoggedCodeFix))]
[Shared]
public sealed class ExceptionNotLoggedCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticIds.ExceptionNotLogged];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return;

        // Find the enclosing catch clause
        var catchClause = invocation.FirstAncestorOrSelf<CatchClauseSyntax>();
        if (catchClause == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Pass exception to Error",
                ct => AddExceptionArgumentAsync(context.Document, invocation, catchClause, ct),
                equivalenceKey: DiagnosticIds.ExceptionNotLogged),
            diagnostic);
    }

    private static async Task<Document> AddExceptionArgumentAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CatchClauseSyntax catchClause,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var exceptionName = GetExceptionVariableName(catchClause);
        SyntaxNode newRoot;

        if (exceptionName != null)
        {
            // Catch has a named variable — just insert it as second argument
            newRoot = root.ReplaceNode(invocation, InsertExceptionArgument(invocation, exceptionName));
        }
        else
        {
            // Catch has no named variable — add one and reference it
            const string varName = "ex";
            var newInvocation = InsertExceptionArgument(invocation, varName);
            var newCatch = AddExceptionVariable(catchClause, varName);

            // Replace both nodes: catch clause first (contains invocation)
            newRoot = root.ReplaceNode(catchClause, newCatch);

            // After replacing the catch, find the invocation again in the new tree
            // and update it with the exception argument
            var updatedCatch = newRoot.FindNode(catchClause.Span).FirstAncestorOrSelf<CatchClauseSyntax>()
                ?? (SyntaxNode)newRoot;

            // Re-find the invocation in the updated catch
            foreach (var candidate in updatedCatch.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (candidate.ToString() == invocation.ToString())
                {
                    newRoot = newRoot.ReplaceNode(candidate, InsertExceptionArgument(candidate, varName));
                    break;
                }
            }
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static string? GetExceptionVariableName(CatchClauseSyntax catchClause)
    {
        var declaration = catchClause.Declaration;
        if (declaration == null) return null;

        var identifier = declaration.Identifier;
        if (identifier.IsKind(SyntaxKind.None) || string.IsNullOrEmpty(identifier.Text))
            return null;

        return identifier.Text;
    }

    private static InvocationExpressionSyntax InsertExceptionArgument(
        InvocationExpressionSyntax invocation, string exceptionName)
    {
        var args = invocation.ArgumentList.Arguments;
        var exceptionArg = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(exceptionName));

        // Insert exception as second argument: Error(message, ex, ...fields)
        SeparatedSyntaxList<ArgumentSyntax> newArgs;
        if (args.Count <= 1)
        {
            newArgs = args.Add(exceptionArg);
        }
        else
        {
            newArgs = args.Insert(1, exceptionArg);
        }

        return invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(newArgs));
    }

    private static CatchClauseSyntax AddExceptionVariable(CatchClauseSyntax catchClause, string varName)
    {
        if (catchClause.Declaration == null)
        {
            // bare `catch { }` → `catch (Exception ex) { }`
            return catchClause.WithDeclaration(
                SyntaxFactory.CatchDeclaration(
                    SyntaxFactory.ParseTypeName("Exception"),
                    SyntaxFactory.Identifier(varName))
                .WithOpenParenToken(SyntaxFactory.Token(SyntaxKind.OpenParenToken))
                .WithCloseParenToken(SyntaxFactory.Token(SyntaxKind.CloseParenToken)));
        }

        // `catch (SomeException) { }` → `catch (SomeException ex) { }`
        return catchClause.WithDeclaration(
            catchClause.Declaration.WithIdentifier(SyntaxFactory.Identifier(varName)));
    }
}
