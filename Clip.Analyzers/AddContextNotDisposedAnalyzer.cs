using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AddContextNotDisposedAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AddContextReturnDiscarded,
        "AddContext return value discarded",
        "AddContext return value must be disposed. Use 'using' to scope the context.",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "AddContext pushes fields onto the current async scope and returns "
        + "an IDisposable. If not disposed, context fields leak into unrelated "
        + "log entries downstream.\n\n"
        + "Instead of: Logger.AddContext(new { RequestId = id });\n"
        + "Use: using var _ = Logger.AddContext(new { RequestId = id });.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(OperationAnalysisContext context)
    {
        var statement = (IExpressionStatementOperation)context.Operation;

        if (statement.Operation is not IInvocationOperation invocation)
            return;

        if (!ClipTypeHelper.IsClipAddContext(invocation.TargetMethod))
            return;

        // Return value is being discarded (it's a bare expression statement)
        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
    }
}
