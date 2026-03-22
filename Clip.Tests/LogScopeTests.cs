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
