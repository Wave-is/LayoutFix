using LayoutFix.Infrastructure.Services;

namespace LayoutFix.Tests;

public class ActiveProcessNameCacheTests
{
    [Fact]
    public void StableForegroundIdentity_ResolvesProcessOnlyOnce()
    {
        var cache = new ActiveProcessNameCache();
        var calls = 0;
        string Resolve(uint processId)
        {
            calls++;
            return $"process-{processId}";
        }

        var first = cache.GetOrResolve((nint)10, 20, Resolve);
        var repeated = cache.GetOrResolve((nint)10, 20, Resolve);
        var newWindow = cache.GetOrResolve((nint)11, 20, Resolve);
        var reusedWindow = cache.GetOrResolve((nint)11, 21, Resolve);

        Assert.Equal("process-20", first);
        Assert.Equal(first, repeated);
        Assert.Equal("process-20", newWindow);
        Assert.Equal("process-21", reusedWindow);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ConcurrentMisses_PublishOneResolvedEntry()
    {
        var cache = new ActiveProcessNameCache();
        var calls = 0;

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            cache.GetOrResolve((nint)10, 20, processId =>
            {
                Interlocked.Increment(ref calls);
                Thread.Sleep(10);
                return $"process-{processId}";
            }))));

        Assert.All(results, result => Assert.Equal("process-20", result));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FailedResolution_IsNotCached()
    {
        var cache = new ActiveProcessNameCache();
        var calls = 0;

        var missing = cache.GetOrResolve((nint)10, 20, _ =>
        {
            calls++;
            return string.Empty;
        });
        var recovered = cache.GetOrResolve((nint)10, 20, _ =>
        {
            calls++;
            return "recovered";
        });

        Assert.Empty(missing);
        Assert.Equal("recovered", recovered);
        Assert.Equal(2, calls);
    }
}
