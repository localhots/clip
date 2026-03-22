using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionAsFieldsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ExceptionAsFields,
        "Exception passed as field property",
        "Exception '{0}' passed as a field property. Use the Error(message, exception) overload to preserve stack trace.",
        "Usage",
        DiagnosticSeverity.Info,
        true,
        "Wrapping an exception in a fields anonymous type loses the structured "
        + "stack trace. Use the Error overload that accepts an Exception parameter "
        + "instead.\n\n"
        + "Instead of: logger.Info(\"failed\", new { Error = ex })\n"
        + "Use: logger.Error(\"failed\", ex).");

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

        // Find the object? fields parameter
        foreach (var arg in invocation.Arguments)
        {
            var param = arg.Parameter;
            if (param == null) continue;
            if (param.Type.SpecialType != SpecialType.System_Object) continue;

            if (arg.ArgumentKind == ArgumentKind.DefaultValue) continue;

            var value = arg.Value;

            // Unwrap implicit conversions (boxing)
            while (value is IConversionOperation { IsImplicit: true } conv)
                value = conv.Operand;

            // Check if the argument is an anonymous object creation
            if (value is not IAnonymousObjectCreationOperation anonCreate)
                continue;

            // Check each initializer for Exception-typed values
            foreach (var initializer in anonCreate.Initializers)
            {
                if (initializer is not ISimpleAssignmentOperation assignment)
                    continue;

                var assignedValue = assignment.Value;

                // Unwrap conversions on the value
                while (assignedValue is IConversionOperation { IsImplicit: true } valConv)
                    assignedValue = valConv.Operand;

                var valueType = assignedValue.Type;
                if (valueType != null && IsExceptionType(valueType))
                {
                    var propertyName = assignment.Target is IPropertyReferenceOperation propRef
                        ? propRef.Property.Name
                        : "Exception";
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule, assignment.Syntax.GetLocation(), propertyName));
                }
            }
        }
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
}
