using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterpolatedStringMessageAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.InterpolatedStringMessage,
        "Interpolated string in log message",
        "Avoid interpolated strings in log messages. Pass structured data as fields instead.",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "Interpolated strings bake data into the message and lose it as structured "
        + "fields. This defeats the purpose of structured logging.\n\n"
        + "Instead of: logger.Info($\"User {name} logged in\")\n"
        + "Use: logger.Info(\"User logged in\", new { name }).");

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

        // Unwrap implicit conversions
        while (value is IConversionOperation { IsImplicit: true } conv)
            value = conv.Operand;

        if (value is IInterpolatedStringOperation)
            context.ReportDiagnostic(Diagnostic.Create(Rule, messageArg.Syntax.GetLocation()));
    }
}
