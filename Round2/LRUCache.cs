namespace Round2;

public class Node
{
    public int Key { get; set; }
    public DateTime Value { get; set; } // timestamp — set by LRUCache via injected clock
    public Node? Prev { get; set; }
    public Node? Next { get; set; }

    public Node(int key) => Key = key;
}

/// <summary>
/// Thread-safe LRU Cache using a doubly linked list + dictionary for O(1) get and add.
/// Head = most recently used, Tail = least recently used.
///
/// <para>
/// A <see cref="SemaphoreSlim"/> (1,1) guards every mutation so concurrent
/// ASP.NET Core requests never corrupt the linked list or dictionary.
/// Because <c>GetAsync</c> also mutates (MoveToFront), all public operations
/// acquire the same exclusive semaphore slot.
/// </para>
///
/// <para>
/// The optional <paramref name="clock"/> parameter lets callers (e.g. unit tests)
/// inject a deterministic time source. Production code leaves it null and
/// gets <see cref="DateTime.UtcNow"/> automatically.
/// </para>
/// </summary>
public class LRUCache : IDisposable
{
    private readonly int _capacity;
    private readonly Dictionary<int, Node> _map;
    private readonly Func<DateTime> _clock;

    // SemaphoreSlim(1,1) = async-compatible exclusive lock.
    // Unlike `lock`, it can be awaited without blocking a thread-pool thread.
    private readonly SemaphoreSlim _sem = new(initialCount: 1, maxCount: 1);

    // Sentinel nodes to avoid null checks at boundaries
    private readonly Node _head; // MRU side
    private readonly Node _tail; // LRU side

    public int Count    => _map.Count;
    public int Capacity => _capacity;

    // ── Metrics (guarded by the same semaphore — no extra locking needed) ─────
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;

    public long   HitCount      => _hitCount;
    public long   MissCount     => _missCount;
    public long   EvictionCount => _evictionCount;

    /// <summary>Cache hit rate as a ratio 0.0–1.0. Returns 0 when no Get calls made yet.</summary>
    public double HitRate
    {
        get
        {
            var total = _hitCount + _missCount;
            return total == 0 ? 0.0 : (double)_hitCount / total;
        }
    }

    public LRUCache(int capacity = 100, Func<DateTime>? clock = null)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));

        _capacity = capacity;
        _clock    = clock ?? (() => DateTime.UtcNow);
        _map      = new Dictionary<int, Node>(capacity);

        // Sentinels are never stored in the map
        _head = new Node(-1);
        _tail = new Node(-1);
        _head.Next = _tail;
        _tail.Prev = _head;
    }

    // ── Public async API ─────────────────────────────────────────────────────

    /// <summary>
    /// Adds a new key to the cache (or refreshes it if it already exists).
    /// Evicts the LRU item when at capacity.
    /// Returns the stored timestamp so the caller does not need a second Get call.
    /// </summary>
    public async Task<DateTime> AddItemAsync(int key)
    {
        await _sem.WaitAsync();
        try
        {
            return AddItemCore(key);
        }
        finally
        {
            _sem.Release();
        }
    }

    /// <summary>
    /// Retrieves the timestamp for the given key and moves it to the MRU position.
    /// Returns null if the key is not in the cache.
    /// </summary>
    public async Task<DateTime?> GetAsync(int key)
    {
        await _sem.WaitAsync();
        try
        {
            return GetCore(key);
        }
        finally
        {
            _sem.Release();
        }
    }

    public void Dispose() => _sem.Dispose();

    // ── Private core logic (called only while semaphore is held) ─────────────

    private DateTime AddItemCore(int key)
    {
        if (_map.TryGetValue(key, out Node? existing))
        {
            existing.Value = _clock();
            MoveToFront(existing);
            return existing.Value;
        }

        if (_map.Count >= _capacity)
            RemoveLRU();

        var node = new Node(key) { Value = _clock() };
        _map[key] = node;
        InsertAtFront(node);
        return node.Value;
    }

    private DateTime? GetCore(int key)
    {
        if (!_map.TryGetValue(key, out Node? node))
        {
            _missCount++;
            return null;
        }

        _hitCount++;
        MoveToFront(node);
        return node.Value;
    }

    private void InsertAtFront(Node node)
    {
        node.Next = _head.Next;
        node.Prev = _head;
        _head.Next!.Prev = node;
        _head.Next = node;
    }

    private static void Detach(Node node)
    {
        node.Prev!.Next = node.Next;
        node.Next!.Prev = node.Prev;
        node.Prev = null;
        node.Next = null;
    }

    private void MoveToFront(Node node)
    {
        Detach(node);
        InsertAtFront(node);
    }

    private void RemoveLRU()
    {
        Node lru = _tail.Prev!;
        if (lru == _head) return;

        Detach(lru);
        _map.Remove(lru.Key);
        _evictionCount++;

        Console.WriteLine($"[LRUCache] Evicted key={lru.Key} (last used: {lru.Value:O})");
    }
}
