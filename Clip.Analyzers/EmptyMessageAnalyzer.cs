using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyMessageAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.EmptyMessage,
        "Empty log message",
        "Log message is empty or whitespace",
        "Usage",
        DiagnosticSeverity.Info,
        true,
        "Log messages should be descriptive plain-text strings. "
        + "An empty or whitespace-only message provides no context "
        + "and is likely a mistake.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!ClipTypeHelper.IsClipLogMethod(method))
            return;

        if (invocation.Arguments.Length == 0)
            return;

        var messageArg = invocation.Arguments[0];
        var value = messageArg.Value;

        if (value.ConstantValue is not { HasValue: true, Value: string message })
            return;

        if (string.IsNullOrWhiteSpace(message))
            context.ReportDiagnostic(Diagnostic.Create(Rule, messageArg.Syntax.GetLocation()));
    }
}
