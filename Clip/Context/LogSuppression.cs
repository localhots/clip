namespace Clip.Context;

internal static class LogSuppression
{
    private static readonly AsyncLocal<bool> Suppressed = new();

    // Mirrors LogScope's _everUsed gate: avoid the AsyncLocal lookup on the hot
    // path until something has actually opened a suppression scope.
    private static volatile bool _everUsed;

    internal static bool IsActive => _everUsed && Suppressed.Value;

    internal static SuppressionScope Begin()
    {
        _everUsed = true;
        var previous = Suppressed.Value;
        Suppressed.Value = true;
        return new SuppressionScope(previous);
    }

    internal static void Restore(bool previous) => Suppressed.Value = previous;
}
