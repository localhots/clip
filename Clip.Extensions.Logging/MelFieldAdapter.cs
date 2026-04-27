using Microsoft.Extensions.Logging;

namespace Clip.Extensions.Logging;

internal static class MelFieldAdapter
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    public static Field[] ExtractFields<TState>(
        string categoryName,
        TState state,
        EventId eventId)
    {
        // Count first, allocate once — avoids List<Field> + ToArray copy.
        // EventId and EventName are independent: a caller can supply either or both, and
        // MEL's default `EventId(0, null)` (from log macros that don't pass an event) is
        // what we want to skip. Including a Name-only EventId is rare but valid.
        var kvps = state as IReadOnlyList<KeyValuePair<string, object?>>;
        var hasEventId = eventId.Id != 0;
        var hasEventName = eventId.Name is not null;
        var count = 1; // SourceContext always present
        if (hasEventId) count++;
        if (hasEventName) count++;

        if (kvps is not null)
            for (var i = 0; i < kvps.Count; i++)
                if (kvps[i].Key != OriginalFormatKey)
                    count++;

        var fields = new Field[count];
        var idx = 0;
        fields[idx++] = new Field("SourceContext", categoryName);

        if (hasEventId)
            fields[idx++] = new Field("EventId", eventId.Id);
        if (hasEventName)
            fields[idx++] = new Field("EventName", eventId.Name!);

        if (kvps is not null)
            for (var i = 0; i < kvps.Count; i++)
            {
                var kvp = kvps[i];
                if (kvp.Key == OriginalFormatKey) continue;
                fields[idx++] = CreateFieldFromKvp(kvp.Key, kvp.Value);
            }

        return fields;
    }

    internal static Field CreateFieldFromKvp(string key, object? value)
    {
        return value switch
        {
            int v => new Field(key, v),
            long v => new Field(key, v),
            double v => new Field(key, v),
            float v => new Field(key, v),
            bool v => new Field(key, v),
            string v => new Field(key, v),
            DateTimeOffset v => new Field(key, v),
            _ => new Field(key, value),
        };
    }
}
