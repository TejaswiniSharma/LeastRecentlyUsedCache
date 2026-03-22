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
/// LRU Cache using a doubly linked list + dictionary for O(1) get and add.
/// Head = most recently used, Tail = least recently used.
///
/// <para>
/// The optional <paramref name="clock"/> parameter lets callers (e.g. unit tests)
/// inject a deterministic time source so timestamp-based assertions are stable.
/// Production code leaves it null and gets <see cref="DateTime.UtcNow"/> automatically.
/// </para>
/// </summary>
public class LRUCache
{
    private readonly int _capacity;
    private readonly Dictionary<int, Node> _map;
    private readonly Func<DateTime> _clock;

    // Sentinel nodes to avoid null checks at boundaries
    private readonly Node _head; // MRU side
    private readonly Node _tail; // LRU side

    public int Count => _map.Count;
    public int Capacity => _capacity;

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

    /// <summary>
    /// Adds a new key to the cache (or refreshes it if it already exists).
    /// Evicts the LRU item when over capacity.
    /// </summary>
    public void AddItem(int key)
    {
        if (_map.TryGetValue(key, out Node? existing))
        {
            // Key already present — refresh its timestamp and move to MRU
            existing.Value = _clock();
            MoveToFront(existing);
            return;
        }

        if (_map.Count >= _capacity)
            RemoveLRU();

        var node = new Node(key) { Value = _clock() };
        _map[key] = node;
        InsertAtFront(node);
    }

    /// <summary>
    /// Retrieves the timestamp for the given key and moves it to MRU position.
    /// Returns null if the key is not in the cache.
    /// </summary>
    public DateTime? Get(int key)
    {
        if (!_map.TryGetValue(key, out Node? node))
            return null;

        MoveToFront(node);
        return node.Value;
    }

    // ── private helpers ──────────────────────────────────────────────────────

    /// <summary>Inserts node right after the head sentinel (MRU position).</summary>
    private void InsertAtFront(Node node)
    {
        node.Next = _head.Next;
        node.Prev = _head;
        _head.Next!.Prev = node;
        _head.Next = node;
    }

    /// <summary>Removes a node from wherever it currently sits in the list.</summary>
    private static void Detach(Node node)
    {
        node.Prev!.Next = node.Next;
        node.Next!.Prev = node.Prev;
        node.Prev = null;
        node.Next = null;
    }

    /// <summary>Moves an existing node to the MRU position.</summary>
    private void MoveToFront(Node node)
    {
        Detach(node);
        InsertAtFront(node);
    }

    /// <summary>Evicts the least recently used item (the node just before the tail sentinel).</summary>
    private void RemoveLRU()
    {
        Node lru = _tail.Prev!;
        if (lru == _head) return; // cache is empty

        Detach(lru);
        _map.Remove(lru.Key);

        Console.WriteLine($"[LRUCache] Evicted key={lru.Key} (last used: {lru.Value:O})");
    }
}
