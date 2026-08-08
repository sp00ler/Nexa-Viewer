namespace ViewerPrn.Infrastructure.Caching;

/// <summary>
/// Least-recently-used cache bounded by total bytes rather than entry count (DECISION-0010):
/// image sizes vary by orders of magnitude, so an entry limit gives no memory guarantee at all.
/// </summary>
// ponytail: one lock around the whole cache. Ceiling is contention, which needs many threads
// hammering it at once; move to striped locks only if a profile ever shows that happening.
public sealed class BoundedLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly long _maxBytes;
    private readonly Func<TValue, long> _sizeOf;
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _index;
    private readonly LinkedList<Entry> _order = new();

    public BoundedLruCache(long maxBytes, Func<TValue, long> sizeOf, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        ArgumentNullException.ThrowIfNull(sizeOf);

        _maxBytes = maxBytes;
        _sizeOf = sizeOf;
        _index = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public long CurrentBytes { get; private set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _index.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                value = default!;
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        long size = Math.Max(1, _sizeOf(value));

        lock (_gate)
        {
            if (_index.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                CurrentBytes -= existing.Value.Size;
                _order.Remove(existing);
                _index.Remove(key);
            }

            // An entry larger than the whole budget is not cached: storing it would evict
            // everything else and still not fit.
            if (size > _maxBytes)
            {
                return;
            }

            LinkedListNode<Entry> node = _order.AddFirst(new Entry(key, value, size));
            _index[key] = node;
            CurrentBytes += size;

            while (CurrentBytes > _maxBytes && _order.Last is { } last)
            {
                _order.RemoveLast();
                _index.Remove(last.Value.Key);
                CurrentBytes -= last.Value.Size;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _index.Clear();
            _order.Clear();
            CurrentBytes = 0;
        }
    }

    private readonly record struct Entry(TKey Key, TValue Value, long Size);
}
