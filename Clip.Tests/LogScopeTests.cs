using Clip.Context;

namespace Clip.Tests;

public class LogScopeTests
{
    [Fact]
    public void HasCurrent_FalseByDefault()
    {
        // Run in a fresh async context to avoid cross-test contamination
        Assert.False(LogScope.HasCurrent);
    }

    [Fact]
    public void Push_SetsCurrentContext()
    {
        using (LogScope.Push([new Field("key", "value")]))
        {
            Assert.True(LogScope.HasCurrent);
        }

        Assert.False(LogScope.HasCurrent);
    }

    [Fact]
    public void CopyCurrentTo_CopiesToList()
    {
        using (LogScope.Push([new Field("k", "v")]))
        {
            var list = new List<Field>();
            LogScope.CopyCurrentTo(list);
            Assert.Single(list);
            Assert.Equal("k", list[0].Key);
        }
    }

    [Fact]
    public void Push_NewFieldsOverwriteExistingKeys()
    {
        using (LogScope.Push([new Field("x", 1)]))
        using (LogScope.Push([new Field("x", 2)]))
        {
            var list = new List<Field>();
            LogScope.CopyCurrentTo(list);
            Assert.Single(list);
            Assert.Equal(2, list[0].IntValue);
        }
    }

    [Fact]
    public void Push_NewFieldsMergeWithExistingKeys()
    {
        using (LogScope.Push([new Field("a", 1)]))
        using (LogScope.Push([new Field("b", 2)]))
        {
            var list = new List<Field>();
            LogScope.CopyCurrentTo(list);
            Assert.Equal(2, list.Count);
        }
    }

    [Fact]
    public async Task AsyncLocal_PreservedAcrossTaskRun()
    {
        // ExecutionContext flows into Task.Run, so the context pushed on the calling thread
        // must be visible inside the continuation.
        using (LogScope.Push([new Field("requestId", "abc")]))
        {
            var captured = await Task.Run(() =>
            {
                var list = new List<Field>();
                LogScope.CopyCurrentTo(list);
                return list;
            });

            Assert.Single(captured);
            Assert.Equal("requestId", captured[0].Key);
        }
    }

    [Fact]
    public async Task AsyncLocal_NotLeakedToUnrelatedTask()
    {
        // A Task.Run started from a parent ExecutionContext that has no scope must not
        // see scopes set in a sibling Task.Run.
        var sibling = Task.Run(async () =>
        {
            using (LogScope.Push([new Field("sibling-only", 1)]))
                await Task.Delay(50);
        });

        // This task runs concurrently with `sibling`. It must not see "sibling-only".
        var observed = await Task.Run(() =>
        {
            var list = new List<Field>();
            LogScope.CopyCurrentTo(list);
            return list;
        });

        await sibling;
        Assert.DoesNotContain(observed, f => f.Key == "sibling-only");
    }

    [Fact]
    public async Task AsyncLocal_NestedAcrossAwait_RestoresOuter()
    {
        using (LogScope.Push([new Field("outer", 1)]))
        {
            await Task.Yield();
            using (LogScope.Push([new Field("inner", 2)]))
            {
                await Task.Yield();
                var both = new List<Field>();
                LogScope.CopyCurrentTo(both);
                Assert.Equal(2, both.Count);
            }

            var afterInner = new List<Field>();
            LogScope.CopyCurrentTo(afterInner);
            Assert.Single(afterInner);
            Assert.Equal("outer", afterInner[0].Key);
        }
    }

    [Fact]
    public void Dispose_RestoresPreviousContext()
    {
        using (LogScope.Push([new Field("outer", 1)]))
        {
            using (LogScope.Push([new Field("inner", 2)]))
            {
                var list = new List<Field>();
                LogScope.CopyCurrentTo(list);
                Assert.Equal(2, list.Count);
            }

            var outerList = new List<Field>();
            LogScope.CopyCurrentTo(outerList);
            Assert.Single(outerList);
            Assert.Equal("outer", outerList[0].Key);
        }
    }
}
