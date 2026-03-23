using Clip.OpenTelemetry.Mapping;
using OpenTelemetry.Proto.Common.V1;

namespace Clip.OpenTelemetry.Tests;

public class ExceptionMappingTests
{
    [Fact]
    public void AddsExceptionType()
    {
        var ex = new InvalidOperationException("something broke");
        var attributes = new Google.Protobuf.Collections.RepeatedField<KeyValue>();

        FieldMapper.AddExceptionAttributes(attributes, ex);

        Assert.Contains(attributes, kv => kv.Key == "exception.type"
            && kv.Value.StringValue == "System.InvalidOperationException");
    }

    [Fact]
    public void AddsExceptionMessage()
    {
        var ex = new InvalidOperationException("something broke");
        var attributes = new Google.Protobuf.Collections.RepeatedField<KeyValue>();

        FieldMapper.AddExceptionAttributes(attributes, ex);

        Assert.Contains(attributes, kv => kv.Key == "exception.message"
            && kv.Value.StringValue == "something broke");
    }

    [Fact]
    public void AddsStackTrace_IncludesInnerException()
    {
        Exception ex;
        try
        {
            try { throw new ArgumentException("inner"); }
            catch (Exception inner) { throw new InvalidOperationException("outer", inner); }
        }
        catch (Exception caught) { ex = caught; }

        var attributes = new Google.Protobuf.Collections.RepeatedField<KeyValue>();
        FieldMapper.AddExceptionAttributes(attributes, ex);

        var stacktrace = attributes.First(kv => kv.Key == "exception.stacktrace").Value.StringValue;
        Assert.Contains("outer", stacktrace);
        Assert.Contains("inner", stacktrace);
    }
}
