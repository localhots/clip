using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LowercaseMessageAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.LowercaseMessage,
        "Lowercase log message",
        "Log message should start with an uppercase letter",
        "Usage",
        DiagnosticSeverity.Info,
        true,
        "Log messages should start with an uppercase letter for consistency "
        + "and readability in log output.");

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

        if (message.Length > 0 && char.IsLower(message[0]))
            context.ReportDiagnostic(Diagnostic.Create(Rule, messageArg.Syntax.GetLocation()));
    }
}
