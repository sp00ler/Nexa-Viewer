using ViewerPrn.Infrastructure.Caching;

namespace ViewerPrn.Infrastructure.Tests;

public sealed class BoundedLruCacheTests
{
    private static BoundedLruCache<string, byte[]> Cache(long maxBytes) =>
        new(maxBytes, bytes => bytes.LongLength);

    private static byte[] Blob(int size) => new byte[size];

    [Fact]
    public void StoresAndReturnsValues()
    {
        BoundedLruCache<string, byte[]> cache = Cache(1000);
        cache.Set("a", Blob(10));

        Assert.True(cache.TryGet("a", out byte[]? value));
        Assert.Equal(10, value.Length);
        Assert.Equal(10, cache.CurrentBytes);
    }

    [Fact]
    public void MissingKeysReportFailure()
    {
        Assert.False(Cache(1000).TryGet("nope", out _));
    }

    [Fact]
    public void EvictsTheLeastRecentlyUsedWhenOverBudget()
    {
        BoundedLruCache<string, byte[]> cache = Cache(100);
        cache.Set("a", Blob(50));
        cache.Set("b", Blob(50));

        cache.Set("c", Blob(50));

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(100, cache.CurrentBytes);
    }

    [Fact]
    public void ReadingAnEntryProtectsItFromTheNextEviction()
    {
        BoundedLruCache<string, byte[]> cache = Cache(100);
        cache.Set("a", Blob(50));
        cache.Set("b", Blob(50));

        cache.TryGet("a", out _);
        cache.Set("c", Blob(50));

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void ReplacingAKeyDoesNotDoubleCountItsBytes()
    {
        BoundedLruCache<string, byte[]> cache = Cache(1000);
        cache.Set("a", Blob(100));
        cache.Set("a", Blob(30));

        Assert.Equal(30, cache.CurrentBytes);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void AnEntryLargerThanTheWholeBudgetIsNotStored()
    {
        BoundedLruCache<string, byte[]> cache = Cache(100);
        cache.Set("keep", Blob(40));

        cache.Set("huge", Blob(500));

        Assert.False(cache.TryGet("huge", out _));
        Assert.True(cache.TryGet("keep", out _));
        Assert.Equal(40, cache.CurrentBytes);
    }

    [Fact]
    public void NeverExceedsTheBudget()
    {
        BoundedLruCache<string, byte[]> cache = Cache(1000);

        for (int i = 0; i < 200; i++)
        {
            cache.Set($"key{i}", Blob(37));
            Assert.True(cache.CurrentBytes <= 1000);
        }
    }

    [Fact]
    public void ClearEmptiesEverything()
    {
        BoundedLruCache<string, byte[]> cache = Cache(1000);
        cache.Set("a", Blob(10));

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CurrentBytes);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void ConcurrentWritersLeaveTheBudgetIntact()
    {
        BoundedLruCache<string, byte[]> cache = Cache(10_000);

        Parallel.For(0, 2000, i =>
        {
            cache.Set($"key{i % 500}", Blob(64));
            cache.TryGet($"key{i % 500}", out _);
        });

        Assert.True(cache.CurrentBytes <= 10_000);
    }
}
