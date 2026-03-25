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
            _ => new AnyValue { StringValue = field.RefValue?.ToString() ?? "" },
        };
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
