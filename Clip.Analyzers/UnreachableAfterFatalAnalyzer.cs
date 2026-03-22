using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnreachableAfterFatalAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UnreachableAfterFatal,
        "Unreachable code after Fatal",
        "Code after Fatal is unreachable. Fatal flushes all sinks and terminates the process.",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "Logger.Fatal() flushes all sinks and calls Environment.Exit(1). "
        + "Any code after a Fatal call in the same block will never execute. "
        + "Move cleanup logic before the Fatal call or into a sink's flush handler.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
    {
        var exprStatement = (ExpressionStatementSyntax)context.Node;
        if (exprStatement.Expression is not InvocationExpressionSyntax invocation)
            return;

        // Check if this is a Fatal call
        var methodName = GetMethodName(invocation);
        if (methodName != "Fatal")
            return;

        // Verify it's on a Clip type via a semantic model
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        if (!ClipTypeHelper.IsClipLoggerType(method.ContainingType))
            return;

        // Check for subsequent statements in the same block
        if (exprStatement.Parent is not BlockSyntax block)
            return;

        var statements = block.Statements;
        var index = statements.IndexOf(exprStatement);
        if (index < 0 || index >= statements.Count - 1)
            return;

        // Flag the first unreachable statement
        var nextStatement = statements[index + 1];
        context.ReportDiagnostic(Diagnostic.Create(Rule, nextStatement.GetLocation()));
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null,
        };
    }
}
