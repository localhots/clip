using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Clip.Analyzers;

internal static class ClipTypeHelper
{
    private static readonly HashSet<string> ClipTypeNames =
    [
        "Clip.ILogger",
        "Clip.IZeroLogger",
        "Clip.Logger",
    ];

    private static readonly HashSet<string> LogMethodNames =
    [
        "Trace", "Debug", "Info", "Warning", "Error", "Fatal",
    ];

    public static bool IsClipLoggerType(INamedTypeSymbol? type)
    {
        if (type == null) return false;

        var fullName = type.ToDisplayString();
        if (ClipTypeNames.Contains(fullName)) return true;

        foreach (var iface in type.AllInterfaces)
            if (ClipTypeNames.Contains(iface.ToDisplayString()))
                return true;

        return false;
    }

    public static bool IsClipLogMethod(IMethodSymbol method)
    {
        return LogMethodNames.Contains(method.Name) && IsClipLoggerType(method.ContainingType);
    }

    public static bool IsClipAddContext(IMethodSymbol method)
    {
        return method.Name == "AddContext" && IsClipLoggerType(method.ContainingType);
    }
}
