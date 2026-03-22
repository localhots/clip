using Clip.Sinks;

namespace Clip.Tests;

public class ConcurrentChaosTests
{
    [Fact]
    public void ConcurrentLogging_BothTiers_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        var ex = Record.Exception(() =>
        {
            Parallel.For(0, 50, i =>
            {
                for (var j = 0; j < 100; j++)
                {
                    logger.Info("ergonomic", new { Thread = i, Iter = j });
                    logger.Info("zeroalloc", new Field("thread", i), new Field("iter", j));
                    logger.Error("with-ex", new InvalidOperationException($"err-{i}-{j}"),
                        new Field("t", i));
                }
            });

            logger.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void ConcurrentLogging_WithContext_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Background(b => b.Json(ms)));

        var ex = Record.Exception(() =>
        {
            Parallel.For(0, 20, i =>
            {
                using (Logger.AddContext(new Field("ctx", i)))
                {
                    for (var j = 0; j < 50; j++)
                        logger.Info("msg", new Field("j", j));
                }
            });

            logger.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void ConcurrentLogging_WithThrowingEnricher_NoCrash()
    {
        var ms = new MemoryStream();
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .Enrich.With(new IntermittentThrowEnricher())
            .WriteTo.Background(b => b.Json(ms)));

        var ex = Record.Exception(() =>
        {
            Parallel.For(0, 20, i =>
            {
                for (var j = 0; j < 50; j++)
                    logger.Info("msg", new Field("i", i));
            });

            logger.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void ConcurrentLogging_DuringDispose_NoCrash()
    {
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new CountingSink()));

        var ex = Record.Exception(() =>
        {
            var tasks = new Task[11];
            for (var i = 0; i < 10; i++)
                tasks[i] = Task.Run(() =>
                {
                    for (var j = 0; j < 200; j++)
                        logger.Info("msg", new Field("j", j));
                });

            tasks[10] = Task.Run(async () =>
            {
                await Task.Delay(5);
                logger.Dispose();
            });

            Task.WaitAll(tasks);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void ConcurrentFieldExtraction_ManyTypes_NoCrash()
    {
        var logger = Logger.Create(c => c
            .MinimumLevel(LogLevel.Trace)
            .WriteTo.Sink(new CountingSink()));

        var ex = Record.Exception(() =>
        {
            Parallel.For(0, 50, i =>
            {
                // Each iteration uses a different anonymous type shape
                // to stress the FieldExtractor cache
                switch (i % 5)
                {
                    case 0: logger.Info("msg", new { A = i }); break;
                    case 1: logger.Info("msg", new { B = i, C = "x" }); break;
                    case 2: logger.Info("msg", new { D = (double)i, E = true }); break;
                    case 3: logger.Info("msg", new { F = i, G = "y", H = 3.14 }); break;
                    case 4: logger.Info("msg", new { I = (long)i }); break;
                }
            });
        });

        Assert.Null(ex);
    }

    private sealed class IntermittentThrowEnricher : ILogEnricher
    {
        private int _count;

        public void Enrich(List<Field> target)
        {
            if (Interlocked.Increment(ref _count) % 3 == 0)
                throw new InvalidOperationException("Intermittent failure");
            target.Add(new Field("enriched", true));
        }
    }

    private sealed class CountingSink : ILogSink
    {
        private int _count;

        public void Write(DateTimeOffset timestamp, LogLevel level, string message,
            ReadOnlySpan<Field> fields, Exception? exception)
        {
            Interlocked.Increment(ref _count);
        }

        public void Dispose()
        {
        }
    }
}
