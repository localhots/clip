using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageTemplateSyntaxAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MessageTemplateSyntax,
        "Message contains template syntax",
        "Clip uses plain messages, not templates. '{{{0}}}' will appear literally in output. Pass data as fields instead.",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "Clip uses plain messages — {Placeholder} syntax is not interpolated "
        + "and will appear as literal text. Pass structured data via the fields "
        + "parameter instead.\n\n"
        + "Instead of: logger.Info(\"User {Name} logged in\")\n"
        + "Use: logger.Info(\"User logged in\", new { Name = user.Name }).");

    // Matches {Identifier} but not {{escaped}}, {0} numeric, or {"json
    private static readonly Regex TemplatePlaceholder = new(
        @"(?<!\{)\{([A-Za-z_][A-Za-z0-9_]*)\}(?!\})",
        RegexOptions.Compiled);

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

        // First parameter is the message
        if (invocation.Arguments.Length == 0)
            return;

        var messageArg = invocation.Arguments[0];
        var messageValue = messageArg.Value;

        // Only check string literals
        if (messageValue.ConstantValue is not { HasValue: true, Value: string message })
            return;

        var match = TemplatePlaceholder.Match(message);
        if (match.Success)
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                messageArg.Syntax.GetLocation(),
                match.Groups[1].Value));
    }
}
