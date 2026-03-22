namespace Clip.Context;

/// <summary>
/// Returned by <see cref="LogScope.Push"/> so that callers using
/// the concrete <see cref="Logger"/> type avoid boxing the scope.
/// </summary>
public readonly struct ContextScope : IDisposable
{
    private readonly Field[]? _previous;

    internal ContextScope(Field[]? previous) => _previous = previous;

    public void Dispose() => LogScope.Restore(_previous);
}
