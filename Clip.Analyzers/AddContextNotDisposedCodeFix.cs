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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddContextNotDisposedCodeFix))]
[Shared]
public sealed class AddContextNotDisposedCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticIds.AddContextReturnDiscarded];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // The diagnostic is reported on the invocation, find the enclosing expression statement
        var expressionStatement = node.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (expressionStatement == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add 'using' to scope context",
                ct => AddUsingAsync(context.Document, expressionStatement, ct),
                equivalenceKey: DiagnosticIds.AddContextReturnDiscarded),
            diagnostic);
    }

    private static async Task<Document> AddUsingAsync(
        Document document,
        ExpressionStatementSyntax expressionStatement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Logger.AddContext(...); → using var _ = Logger.AddContext(...);
        var usingStatement = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.UsingKeyword)),
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName(
                    SyntaxFactory.Identifier(
                        SyntaxFactory.TriviaList(),
                        SyntaxKind.VarKeyword,
                        "var",
                        "var",
                        SyntaxFactory.TriviaList(SyntaxFactory.Space))),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(
                        SyntaxFactory.Identifier("_"))
                    .WithInitializer(
                        SyntaxFactory.EqualsValueClause(expressionStatement.Expression.WithoutLeadingTrivia())
                            .WithEqualsToken(
                                SyntaxFactory.Token(
                                    SyntaxFactory.TriviaList(SyntaxFactory.Space),
                                    SyntaxKind.EqualsToken,
                                    SyntaxFactory.TriviaList(SyntaxFactory.Space)))))));

        // Preserve leading trivia (indentation)
        usingStatement = usingStatement
            .WithLeadingTrivia(expressionStatement.GetLeadingTrivia())
            .WithTrailingTrivia(expressionStatement.GetTrailingTrivia());

        var newRoot = root.ReplaceNode(expressionStatement, usingStatement);
        return document.WithSyntaxRoot(newRoot);
    }
}
