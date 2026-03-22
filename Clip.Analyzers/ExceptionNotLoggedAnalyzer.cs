using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionNotLoggedAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ExceptionNotLogged,
        "Exception not passed to Error",
        "Error called in a catch block without passing the exception. Use the Error(message, exception, fields) overload.",
        "Usage",
        DiagnosticSeverity.Info,
        true,
        "When logging inside catch blocks, pass the caught exception to "
        + "preserve the stack trace and error details in structured output.\n\n"
        + "Instead of: logger.Error(\"Failed\")\n"
        + "Use: logger.Error(\"Failed\", ex).");

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

        // Only care about Error calls
        if (method.Name != "Error")
            return;

        if (!ClipTypeHelper.IsClipLoggerType(method.ContainingType))
            return;

        // Check if the method has an Exception parameter — if so, exception is being passed
        foreach (var param in method.Parameters)
            if (IsExceptionType(param.Type))
                return;

        // No exception parameter — check if we're inside a catch block
        if (!IsInsideCatchBlock(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation()));
    }

    private static bool IsExceptionType(ITypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private static bool IsInsideCatchBlock(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is ICatchClauseOperation)
                return true;
            current = current.Parent;
        }

        return false;
    }
}
