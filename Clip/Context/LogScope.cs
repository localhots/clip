namespace Clip.Context;

internal static class LogScope
{
    private static readonly AsyncLocal<Field[]?> Current = new();

    // Cheap guard to skip AsyncLocal lookups when context has never been used.
    // AsyncLocal.Value access is non-trivial (ExecutionContext lookup), so a volatile
    // bool check on the hot path avoids that cost entirely for the common case.
    private static volatile bool _everUsed;

    internal static bool HasCurrent => _everUsed && Current.Value != null;

    internal static void CopyCurrentTo(List<Field> target)
    {
        if (!_everUsed) return;
        var fields = Current.Value;
        if (fields == null) return;
        target.AddRange(fields);
    }

    internal static ContextScope Push(ReadOnlySpan<Field> newFields)
    {
        _everUsed = true;
        var previous = Current.Value;
        Current.Value = Merge(previous, newFields);
        return new ContextScope(previous);
    }

    internal static void Restore(Field[]? previous) => Current.Value = previous;

    private static Field[] Merge(Field[]? existing, ReadOnlySpan<Field> added)
    {
        // New fields overwrite existing keys.
        // Single array allocation — no intermediate List.
        if (existing is null or { Length: 0 })
            return added.ToArray();

        var keepCount = 0;
        for (var i = 0; i < existing.Length; i++)
            if (!ContainsKey(added, existing[i].Key))
                keepCount++;

        var result = new Field[keepCount + added.Length];
        var pos = 0;
        for (var i = 0; i < existing.Length; i++)
            if (!ContainsKey(added, existing[i].Key))
                result[pos++] = existing[i];
        foreach (var field in added)
            result[pos++] = field;

        return result;
    }

    private static bool ContainsKey(ReadOnlySpan<Field> fields, string key)
    {
        for (var i = 0; i < fields.Length; i++)
            if (fields[i].Key == key)
                return true;
        return false;
    }

}
