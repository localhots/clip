namespace Clip.Fields;

/// <summary>
/// Two-slot-per-thread pool for List&lt;Field&gt;. Avoids allocating a new list
/// on every log call while keeping the design lock-free (ThreadStatic).
/// Two slots support nested rental: Logger rents the outer list for field
/// accumulation, WriteWithEnrichers rents the inner list for per-sink enrichment.
/// </summary>
internal static class FieldListPool
{
    [ThreadStatic]
    private static List<Field>? _tOuter;

    [ThreadStatic]
    private static List<Field>? _tInner;

    private const int InitialCapacity = 16;

    public static List<Field> Rent()
    {
        var list = _tOuter;
        if (list != null) { _tOuter = null; return list; }
        list = _tInner;
        if (list != null) { _tInner = null; return list; }
        return new List<Field>(InitialCapacity);
    }

    public static void Return(List<Field> list)
    {
        list.Clear();
        if (_tOuter == null) _tOuter = list;
        else _tInner = list;
    }
}
