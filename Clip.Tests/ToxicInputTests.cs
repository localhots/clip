using System.Collections;
using System.Text;
using System.Text.Json;

namespace Clip.Tests;

public class ToxicInputTests
{
    private static (Logger logger, MemoryStream ms) MakeJsonLogger()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(ms));
        return (logger, ms);
    }

    private static (Logger logger, MemoryStream jsonMs, MemoryStream consoleMs) MakeDualLogger()
    {
        var jsonMs = new MemoryStream();
        var consoleMs = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(jsonMs)
            .WriteTo.Console(consoleMs, false));
        return (logger, jsonMs, consoleMs);
    }

    private static JsonDocument[] ReadLines(MemoryStream ms)
    {
        ms.Position = 0;
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l))
            .ToArray();
    }

    //
    // Message edge cases
    //

    [Fact]
    public void EmptyMessage_NoCrash()
    {
        var (logger, ms, _) = MakeDualLogger();

        var ex = Record.Exception(() =>
        {
            logger.Info("");
            logger.Info("", new Field("k", 1));
        });

        Assert.Null(ex);
        var docs = ReadLines(ms);
        Assert.Equal(2, docs.Length);
        Assert.Equal("", docs[0].RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void WhitespaceOnlyMessage_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();
        var ex = Record.Exception(() => logger.Info("   \t\n"));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("   \t\n", docs[0].RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void NullByteMessage_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();
        var ex = Record.Exception(() => logger.Info("\0\0\0"));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal("\0\0\0", docs[0].RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void VeryLongMessage_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();
        var longMsg = new string('x', 1_000_000);

        var ex = Record.Exception(() => logger.Info(longMsg));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.Equal(longMsg, docs[0].RootElement.GetProperty("msg").GetString());
    }

    //
    // Toxic object fields (ergonomic tier)
    //

    [Fact]
    public void ObjectField_ToStringThrows_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Info("msg", new { Evil = (object)new ThrowingToStringObject() }));
        Assert.Null(ex);
    }

    [Fact]
    public void ObjectField_PropertyGetterThrows_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Info("msg", new ThrowingPropertyObject()));
        Assert.Null(ex);
    }

    [Fact]
    public void ObjectField_CircularReference_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();
        var circular = new CircularObject();

        var ex = Record.Exception(() => logger.Info("msg", new { Loop = (object)circular }));
        Assert.Null(ex);
    }

    [Fact]
    public void ObjectField_ThrowingDictionary_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Info("msg", new ThrowingDictionary()));
        Assert.Null(ex);
    }

    [Fact]
    public void AnonymousType_AllNullProperties_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();

        var ex =
            Record.Exception(() => logger.Info("msg", new { A = (string?)null, B = (object?)null, C = (int?)null }));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    [Fact]
    public void DictionaryWithNullKey_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Info("msg", new NullKeyDictionary()));
        Assert.Null(ex);
    }

    //
    // Field value edge cases (zero-alloc tier)
    //

    [Fact]
    public void Field_EmptyStringKey_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();

        var ex = Record.Exception(() => logger.Info("msg", new Field("", 42)));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    [Fact]
    public void Field_VeryLongKey_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();
        var longKey = new string('k', 10_000);

        var ex = Record.Exception(() => logger.Info("msg", new Field(longKey, 1)));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    [Fact]
    public void Field_NullStringValue_NoCrash()
    {
        var (logger, _, consoleMs) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Info("msg", new Field("k", (string)null!)));
        Assert.Null(ex);

        // Console sink should not crash (bug fix validates this)
        Assert.True(consoleMs.Length > 0);
    }

    [Fact]
    public void Field_NullObjectValue_NoCrash()
    {
        var (logger, jsonMs, consoleMs) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Info("msg", new Field("k", (object?)null)));
        Assert.Null(ex);

        Assert.True(jsonMs.Length > 0);
        Assert.True(consoleMs.Length > 0);
    }

    [Fact]
    public void Field_ObjectToStringReturnsNull_NoCrash()
    {
        var (logger, _, consoleMs) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Info("msg", new Field("k", new NullToStringObject())));
        Assert.Null(ex);
        Assert.True(consoleMs.Length > 0);
    }

    //
    // Exception edge cases
    //

    [Fact]
    public void Exception_ToStringThrows_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() =>
            logger.Error("fail", new ThrowingToStringException(), new Field("k", 1)));
        Assert.Null(ex);
    }

    [Fact]
    public void AggregateException_MultipleInners_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();
        var agg = new AggregateException("batch",
            new InvalidOperationException("first"),
            new ArgumentException("second"),
            new TimeoutException("third"));

        var ex = Record.Exception(() => logger.Error("fail", agg));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
        Assert.True(docs[0].RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void Exception_DataThrows_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Error("fail", new ThrowingDataException()));
        Assert.Null(ex);
    }

    [Fact]
    public void Exception_StackTraceThrows_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Error("fail", new ThrowingStackTraceException()));
        Assert.Null(ex);
    }

    [Fact]
    public void Exception_NullMessage_NoCrash()
    {
        var (logger, _, _) = MakeDualLogger();

        var ex = Record.Exception(() => logger.Error("fail", new NullMessageException()));
        Assert.Null(ex);
    }

    //
    // Context edge cases
    //

    [Fact]
    public void Context_NullFieldValues_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();

        using (Logger.AddContext(new Field("k", (string)null!)))
        {
            logger.Info("msg");
        }

        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    [Fact]
    public void Context_DeeplyNested100Scopes_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();
        var scopes = new List<IDisposable>();

        var ex = Record.Exception(() =>
        {
            for (var i = 0; i < 100; i++)
                scopes.Add(Logger.AddContext(new Field($"scope{i}", i)));

            logger.Info("deep");

            for (var i = scopes.Count - 1; i >= 0; i--)
                scopes[i].Dispose();
        });

        Assert.Null(ex);
        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    [Fact]
    public void Context_DisposedOutOfOrder_NoCrash()
    {
        var (logger, _) = MakeJsonLogger();

        var ex = Record.Exception(() =>
        {
            var scopeA = Logger.AddContext(new Field("a", 1));
            var scopeB = Logger.AddContext(new Field("b", 2));
            scopeA.Dispose(); // out of order
            logger.Info("msg");
            scopeB.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Context_EmptyObject_NoCrash()
    {
        var (logger, ms) = MakeJsonLogger();

        var ex = Record.Exception(() =>
        {
            using (Logger.AddContext(new { }))
            {
                logger.Info("msg");
            }
        });

        Assert.Null(ex);
        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    //
    // Enricher/redactor chaos
    //

    [Fact]
    public void Enricher_ThrowsEveryCall_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Enrich.With(new ThrowingEnricher())
            .WriteTo.Json(ms));

        var ex = Record.Exception(() => logger.Info("msg"));
        Assert.Null(ex);
    }

    [Fact]
    public void Enricher_AddsThousandsOfFields_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Enrich.With(new FloodEnricher(5000))
            .WriteTo.Json(ms));

        var ex = Record.Exception(() => logger.Info("msg"));
        Assert.Null(ex);

        var docs = ReadLines(ms);
        Assert.Single(docs);
    }

    [Fact]
    public void Redactor_Throws_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Redact.With(new ThrowingRedactor())
            .WriteTo.Json(ms));

        var ex = Record.Exception(() => logger.Info("msg", new Field("secret", "value")));
        Assert.Null(ex);
    }

    //
    // Sink stress
    //

    [Fact]
    public void JsonSink_ClosedStream_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Json(ms));

        ms.Close();

        var ex = Record.Exception(() => logger.Info("msg"));
        Assert.Null(ex);
    }

    [Fact]
    public void BackgroundSink_RapidFireAndDispose_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        var ex = Record.Exception(() =>
        {
            for (var i = 0; i < 10_000; i++)
                logger.Info($"msg-{i}");
            logger.Dispose();
        });

        Assert.Null(ex);
    }

    //
    // Helper classes
    //

    private sealed class ThrowingToStringObject
    {
        public override string ToString()
        {
            throw new InvalidOperationException("ToString exploded");
        }
    }

    private sealed class NullToStringObject
    {
        public override string? ToString()
        {
            return null;
        }
    }

    private sealed class ThrowingPropertyObject
    {
        public string Name => throw new InvalidOperationException("Getter exploded");
        public int Value => throw new InvalidOperationException("Getter exploded");
    }

    private sealed class CircularObject
    {
        public CircularObject Self => this;
        public string Name => "circular";
    }

    private sealed class NullKeyDictionary : IDictionary
    {
        public object? this[object key]
        {
            get => null;
            set { }
        }

        public ICollection Keys => new object?[] { null };
        public ICollection Values => new object[] { "v" };
        public bool IsReadOnly => true;
        public bool IsFixedSize => true;
        public int Count => 1;
        public object SyncRoot => this;
        public bool IsSynchronized => false;

        public void Add(object key, object? value)
        {
        }

        public void Clear()
        {
        }

        public bool Contains(object key)
        {
            return false;
        }

        public void CopyTo(Array array, int index)
        {
        }

        public void Remove(object key)
        {
        }

        public IDictionaryEnumerator GetEnumerator()
        {
            return new NullKeyEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private sealed class NullKeyEnumerator : IDictionaryEnumerator
        {
            private bool _moved;
            public DictionaryEntry Entry => new(null!, "v");
            public object Key => null!;
            public object Value => "v";
            public object Current => Entry;

            public bool MoveNext()
            {
                if (_moved) return false;
                _moved = true;
                return true;
            }

            public void Reset()
            {
                _moved = false;
            }
        }
    }

    private sealed class ThrowingDictionary : IDictionary
    {
        public object? this[object key]
        {
            get => throw new InvalidOperationException("Indexer exploded");
            set => throw new InvalidOperationException("Indexer exploded");
        }

        public ICollection Keys => throw new InvalidOperationException("Keys exploded");
        public ICollection Values => throw new InvalidOperationException("Values exploded");
        public bool IsReadOnly => false;
        public bool IsFixedSize => false;
        public int Count => throw new InvalidOperationException("Count exploded");
        public object SyncRoot => this;
        public bool IsSynchronized => false;

        public void Add(object key, object? value)
        {
            throw new InvalidOperationException();
        }

        public void Clear()
        {
            throw new InvalidOperationException();
        }

        public bool Contains(object key)
        {
            throw new InvalidOperationException();
        }

        public void CopyTo(Array array, int index)
        {
            throw new InvalidOperationException();
        }

        public void Remove(object key)
        {
            throw new InvalidOperationException();
        }

        public IDictionaryEnumerator GetEnumerator()
        {
            throw new InvalidOperationException("Enumerator exploded");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new InvalidOperationException("Enumerator exploded");
        }
    }

    private sealed class ThrowingToStringException() : Exception("inner msg")
    {
        public override string ToString()
        {
            throw new InvalidOperationException("ToString exploded");
        }
    }

    private sealed class ThrowingDataException() : Exception("data exploded")
    {
        public override IDictionary Data => throw new InvalidOperationException("Data exploded");
    }

    private sealed class NullMessageException : Exception
    {
        public override string Message => null!;
    }

    private sealed class ThrowingStackTraceException() : Exception("stack exploded")
    {
        public override string StackTrace => throw new InvalidOperationException("StackTrace exploded");
    }

    private sealed class ThrowingEnricher : ILogEnricher
    {
        public void Enrich(List<Field> target)
        {
            throw new InvalidOperationException("Enricher exploded");
        }
    }

    private sealed class FloodEnricher(int count) : ILogEnricher
    {
        public void Enrich(List<Field> target)
        {
            for (var i = 0; i < count; i++)
                target.Add(new Field($"flood_{i}", i));
        }
    }

    private sealed class ThrowingRedactor : ILogRedactor
    {
        public void Redact(ref Field field)
        {
            throw new InvalidOperationException("Redactor exploded");
        }
    }
}
