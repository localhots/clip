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
        var kvps = state as IReadOnlyList<KeyValuePair<string, object?>>;
        var count = 1; // SourceContext always present
        if (eventId.Id != 0)
        {
            count++;
            if (eventId.Name is not null) count++;
        }

        if (kvps is not null)
            for (var i = 0; i < kvps.Count; i++)
                if (kvps[i].Key != OriginalFormatKey)
                    count++;

        var fields = new Field[count];
        var idx = 0;
        fields[idx++] = new Field("SourceContext", categoryName);

        if (eventId.Id != 0)
        {
            fields[idx++] = new Field("EventId", eventId.Id);
            if (eventId.Name is not null)
                fields[idx++] = new Field("EventName", eventId.Name);
        }

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
