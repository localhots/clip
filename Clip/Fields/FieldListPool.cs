namespace Clip.Fields;

/// <summary>
/// Single-item-per-thread pool for List&lt;Field&gt;. Avoids allocating a new list
/// on every log call while keeping the design lock-free (ThreadStatic).
/// </summary>
internal static class FieldListPool
{
    [ThreadStatic]
    private static List<Field>? _tCached;

    private const int CacheListSize = 16;

    public static List<Field> Rent()
    {
        var list = _tCached;
        if (list == null) return new List<Field>(CacheListSize);
        _tCached = null;
        return list;
    }

    public static void Return(List<Field> list)
    {
        list.Clear();
        _tCached = list;
    }
}
