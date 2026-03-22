using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clip.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvalidFieldsArgumentAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.InvalidFieldsArgument,
        "Invalid fields argument",
        "Expected an anonymous type or dictionary for fields, got '{0}'. Use new {{ Key = value }} syntax.",
        "Usage",
        DiagnosticSeverity.Error,
        true,
        "The fields parameter accepts anonymous objects (new { Key = value }) "
        + "or Dictionary<string, object?>. Passing primitives, strings, arrays, "
        + "or enums will throw ArgumentException at runtime.\n\n"
        + "Instead of: logger.Info(\"msg\", 42)\n"
        + "Use: logger.Info(\"msg\", new { count = 42 }).");

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

        if (!ClipTypeHelper.IsClipLogMethod(method) && !ClipTypeHelper.IsClipAddContext(method))
            return;

        // Find the object? fields parameter (skip ReadOnlySpan<Field> overloads)
        foreach (var arg in invocation.Arguments)
        {
            var param = arg.Parameter;
            if (param == null) continue;
            if (param.Type.SpecialType != SpecialType.System_Object) continue;

            // This is the object? fields parameter
            if (arg.ArgumentKind == ArgumentKind.DefaultValue) continue;

            var value = arg.Value;

            // Unwrap implicit conversions (boxing)
            while (value is IConversionOperation { IsImplicit: true } conv)
                value = conv.Operand;

            var argType = value.Type;
            if (argType == null) continue;

            // Null literal is fine
            if (value.ConstantValue is { HasValue: true, Value: null })
                continue;

            if (IsInvalidFieldsType(argType))
            {
                var displayString = argType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                context.ReportDiagnostic(Diagnostic.Create(Rule, arg.Syntax.GetLocation(), displayString));
            }
        }
    }

    private static bool IsInvalidFieldsType(ITypeSymbol type)
    {
        // Primitives: int, bool, double, float, char, byte, etc.
        if (type.IsValueType && type.SpecialType != SpecialType.None)
            return true;

        // String
        if (type.SpecialType == SpecialType.System_String)
            return true;

        // Arrays
        if (type is IArrayTypeSymbol)
            return true;

        // Enums
        return type.TypeKind == TypeKind.Enum;
    }
}
