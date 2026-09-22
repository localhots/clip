using System.Collections;
using System.Globalization;
using OpenTelemetry.Proto.Common.V1;
using SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber;

namespace Clip.OpenTelemetry.Mapping;

/// <summary>
/// Maps Clip <see cref="Field"/> values to OTLP <see cref="KeyValue"/> attributes
/// and Clip <see cref="LogLevel"/> to OTLP severity.
/// </summary>
internal static class FieldMapper
{
    // Same bounds as the console sink's defaults (ConsoleFormatConfig).
    internal const int MaxCollectionItems = 100;
    internal const int MaxCollectionDepth = 3;

    // Total elements converted per field across all nesting levels. The console sink is
    // bounded by its byte buffer; without this, nested lists could reach 100^3 values.
    internal const int MaxCollectionElements = 1000;

    //
    // Pre-allocated severity tuples — no string allocation per call
    //

    private static readonly (SeverityNumber Number, string Text) SeverityTrace = (SeverityNumber.Trace, "TRACE");
    private static readonly (SeverityNumber Number, string Text) SeverityDebug = (SeverityNumber.Debug, "DEBUG");
    private static readonly (SeverityNumber Number, string Text) SeverityInfo = (SeverityNumber.Info, "INFO");
    private static readonly (SeverityNumber Number, string Text) SeverityWarn = (SeverityNumber.Warn, "WARN");
    private static readonly (SeverityNumber Number, string Text) SeverityError = (SeverityNumber.Error, "ERROR");
    private static readonly (SeverityNumber Number, string Text) SeverityFatal = (SeverityNumber.Fatal, "FATAL");
    private static readonly (SeverityNumber Number, string Text) SeverityUnspecified = (SeverityNumber.Unspecified, "");

    //
    // Field to KeyValue
    //

    internal static KeyValue ToKeyValue(in Field field)
    {
        return new KeyValue
        {
            Key = field.Key,
            Value = ToAnyValue(in field),
        };
    }

    private static AnyValue ToAnyValue(in Field field)
    {
        return field.Type switch
        {
            FieldType.Bool => new AnyValue { BoolValue = field.BoolValue },
            FieldType.Int => new AnyValue { IntValue = field.IntValue },
            FieldType.Long => new AnyValue { IntValue = field.LongValue },
            FieldType.ULong => new AnyValue { IntValue = unchecked((long)(ulong)field.LongValue) },
            FieldType.Float => new AnyValue { DoubleValue = field.FloatValue },
            FieldType.Double => new AnyValue { DoubleValue = field.DoubleValue },
            FieldType.DateTime => new AnyValue
            {
                StringValue = new DateTimeOffset(field.LongValue, TimeSpan.Zero).ToString("o"),
            },
            FieldType.String => new AnyValue { StringValue = (string?)field.RefValue ?? "" },
            FieldType.Decimal => new AnyValue { StringValue = field.DecimalValue.ToString(CultureInfo.InvariantCulture) },
            FieldType.Guid => new AnyValue { StringValue = field.GuidValue.ToString() },
            _ when field.RefValue is AnyValue snapshot => snapshot,
            _ when SnapshotCollection(field.RefValue) is { } collection => collection,
            _ => new AnyValue { StringValue = field.RefValue?.ToString() ?? "" },
        };
    }

    //
    // Collection values
    //

    /// <summary>
    /// Converts a collection field value to an <see cref="AnyValue"/> on the calling thread,
    /// so the exported value reflects the collection at log time rather than whatever it
    /// holds when the background exporter gets to it (and so a concurrent mutation cannot
    /// throw inside the export loop and lose the whole batch). Returns <c>null</c> for
    /// values that are not collections.
    /// </summary>
    internal static AnyValue? SnapshotCollection(object? value)
    {
        if (value is not IEnumerable || value is string) return null;

        try
        {
            var budget = MaxCollectionElements;
            return ObjectToAnyValue(value, depth: 0, ref budget);
        }
        catch (Exception ex)
        {
            // Enumeration runs user code (lazy sequences, collections mutated on another thread).
            return new AnyValue { StringValue = $"<enumeration failed: {ex.GetType().Name}>" };
        }
    }

    /// <summary>
    /// Maps a boxed value to an <see cref="AnyValue"/>. Sequences become <c>ArrayValue</c> and
    /// dictionaries become <c>KvlistValue</c> rather than falling through to <c>ToString()</c>,
    /// which for most collection types yields only the type name. Scalars keep their OTLP type.
    /// </summary>
    private static AnyValue ObjectToAnyValue(object? value, int depth, ref int budget)
    {
        return value switch
        {
            null => new AnyValue(),
            string s => new AnyValue { StringValue = s },
            bool b => new AnyValue { BoolValue = b },
            int i => new AnyValue { IntValue = i },
            long l => new AnyValue { IntValue = l },
            short s => new AnyValue { IntValue = s },
            byte b => new AnyValue { IntValue = b },
            sbyte b => new AnyValue { IntValue = b },
            ushort u => new AnyValue { IntValue = u },
            uint u => new AnyValue { IntValue = u },
            ulong u => new AnyValue { IntValue = unchecked((long)u) },
            float f => new AnyValue { DoubleValue = f },
            double d => new AnyValue { DoubleValue = d },
            DateTime dt => new AnyValue { StringValue = dt.ToString("o", CultureInfo.InvariantCulture) },
            DateTimeOffset dto => new AnyValue { StringValue = dto.ToString("o", CultureInfo.InvariantCulture) },
            IDictionary dict => DictionaryToAnyValue(dict, depth, ref budget),
            IEnumerable seq => SequenceToAnyValue(seq, depth, ref budget),
            IFormattable fmt => new AnyValue { StringValue = fmt.ToString(null, CultureInfo.InvariantCulture) },
            _ => new AnyValue { StringValue = value.ToString() ?? "" },
        };
    }

    private static AnyValue SequenceToAnyValue(IEnumerable seq, int depth, ref int budget)
    {
        if (depth >= MaxCollectionDepth)
            return new AnyValue { StringValue = "[...]" };

        var array = new ArrayValue();
        var n = 0;
        foreach (var item in seq)
        {
            // The caps also stop enumeration of lazy sequences, which may be unbounded.
            if (n == MaxCollectionItems || budget <= 0)
            {
                array.Values.Add(new AnyValue { StringValue = OverflowMarker(seq as ICollection, n) });
                break;
            }

            budget--;
            array.Values.Add(ObjectToAnyValue(item, depth + 1, ref budget));
            n++;
        }

        return new AnyValue { ArrayValue = array };
    }

    private static AnyValue DictionaryToAnyValue(IDictionary dict, int depth, ref int budget)
    {
        if (depth >= MaxCollectionDepth)
            return new AnyValue { StringValue = "{...}" };

        var kvlist = new KeyValueList();
        var n = 0;
        foreach (DictionaryEntry entry in dict)
        {
            if (n == MaxCollectionItems || budget <= 0)
            {
                kvlist.Values.Add(new KeyValue
                {
                    Key = "...",
                    Value = new AnyValue { StringValue = OverflowMarker(dict, n) },
                });
                break;
            }

            budget--;
            kvlist.Values.Add(new KeyValue
            {
                Key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "",
                Value = ObjectToAnyValue(entry.Value, depth + 1, ref budget),
            });
            n++;
        }

        return new AnyValue { KvlistValue = kvlist };
    }

    /// <summary><c>... (N more)</c> when the collection is counted, otherwise <c>...</c>.</summary>
    private static string OverflowMarker(ICollection? counted, int shown)
    {
        return counted is null ? "..." : $"... ({counted.Count - shown} more)";
    }

    //
    // LogLevel → OTLP Severity
    //

    internal static (SeverityNumber Number, string Text) ToSeverity(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => SeverityTrace,
            LogLevel.Debug => SeverityDebug,
            LogLevel.Info => SeverityInfo,
            LogLevel.Warning => SeverityWarn,
            LogLevel.Error => SeverityError,
            LogLevel.Fatal => SeverityFatal,
            _ => SeverityUnspecified,
        };
    }

    //
    // Exception → OTLP semantic convention attributes
    //

    internal static void AddExceptionAttributes(
        Google.Protobuf.Collections.RepeatedField<KeyValue> attributes, Exception exception)
    {
        attributes.Add(new KeyValue
        {
            Key = "exception.type",
            Value = new AnyValue { StringValue = exception.GetType().FullName ?? exception.GetType().Name },
        });
        attributes.Add(new KeyValue
        {
            Key = "exception.message",
            Value = new AnyValue { StringValue = exception.Message },
        });
        attributes.Add(new KeyValue
        {
            Key = "exception.stacktrace",
            Value = new AnyValue { StringValue = exception.ToString() },
        });
    }
}
