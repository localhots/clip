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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LowercaseMessageCodeFix))]
[Shared]
public sealed class LowercaseMessageCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticIds.LowercaseMessage];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        var argument = node.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument == null) return;

        if (argument.Expression is not LiteralExpressionSyntax literal) return;
        if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Capitalize log message",
                ct => CapitalizeAsync(context.Document, literal, ct),
                equivalenceKey: DiagnosticIds.LowercaseMessage),
            diagnostic);
    }

    private static async Task<Document> CapitalizeAsync(
        Document document,
        LiteralExpressionSyntax literal,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var text = literal.Token.ValueText;
        if (text.Length == 0) return document;

        var capitalized = char.ToUpperInvariant(text[0]) + text.Substring(1);
        var newLiteral = literal.WithToken(SyntaxFactory.Literal(capitalized));

        var newRoot = root.ReplaceNode(literal, newLiteral);
        return document.WithSyntaxRoot(newRoot);
    }
}
