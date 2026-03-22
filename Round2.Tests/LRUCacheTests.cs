using NUnit.Framework;

namespace Round2.Tests;

/// <summary>
/// Unit tests for <see cref="LRUCache"/>.
///
/// Every test uses a deterministic fake clock so timestamp assertions never
/// flicker. The clock increments by one second on every call:
///   tick 0 → 2024-01-01 00:00:00
///   tick 1 → 2024-01-01 00:00:01  … and so on.
/// </summary>
[TestFixture]
public class LRUCacheTests
{
    // ── Fake clock ────────────────────────────────────────────────────────────

    private static readonly DateTime BaseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private int _tick;

    private DateTime FakeClock() => BaseTime.AddSeconds(_tick++);

    [SetUp]
    public void SetUp() => _tick = 0;

    // Helper: build a cache wired to the fake clock
    private LRUCache MakeCache(int capacity = 3) => new(capacity, FakeClock);

    // ═══════════════════════════════════════════════════════════════════════════
    // Constructor
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Constructor_ValidCapacity_InitializesEmpty()
    {
        var cache = MakeCache(10);

        Assert.That(cache.Count,    Is.EqualTo(0));
        Assert.That(cache.Capacity, Is.EqualTo(10));
    }

    [Test]
    public void Constructor_ZeroCapacity_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LRUCache(0));
    }

    [Test]
    public void Constructor_NegativeCapacity_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LRUCache(-5));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AddItem
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void AddItem_NewKey_IncreasesCount()
    {
        var cache = MakeCache();

        cache.AddItem(1);

        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddItem_DuplicateKey_DoesNotIncreaseCount()
    {
        var cache = MakeCache();

        cache.AddItem(1);
        cache.AddItem(1);

        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddItem_DuplicateKey_RefreshesTimestamp()
    {
        var cache = MakeCache();

        cache.AddItem(1);                      // tick 0 → T+0s
        var firstTimestamp = cache.Get(1)!.Value;

        cache.AddItem(1);                      // tick 2 (Get also ticked) → T+2s
        var refreshedTimestamp = cache.Get(1)!.Value;

        Assert.That(refreshedTimestamp, Is.GreaterThan(firstTimestamp),
            "Re-adding an existing key should update its timestamp.");
    }

    [Test]
    public void AddItem_AtCapacity_EvictsLRU()
    {
        // capacity=2: add 1,2 → full; add 3 → key 1 (LRU) must be evicted
        var cache = MakeCache(capacity: 2);

        cache.AddItem(1);
        cache.AddItem(2);
        cache.AddItem(3); // triggers eviction of key 1

        Assert.That(cache.Count,  Is.EqualTo(2));
        Assert.That(cache.Get(1), Is.Null,      "Key 1 should have been evicted.");
        Assert.That(cache.Get(2), Is.Not.Null,  "Key 2 should still be in cache.");
        Assert.That(cache.Get(3), Is.Not.Null,  "Key 3 should be in cache.");
    }

    [Test]
    public void AddItem_EvictsCorrectLRU_AfterGet()
    {
        // Add 1, 2, 3 → Get(1) promotes 1 to MRU → add 4 → 2 is now LRU, must be evicted
        var cache = MakeCache(capacity: 3);

        cache.AddItem(1);
        cache.AddItem(2);
        cache.AddItem(3);
        cache.Get(1);     // promotes 1; order is now [1, 3, 2] (MRU → LRU)
        cache.AddItem(4); // evicts 2

        Assert.That(cache.Get(2), Is.Null,     "Key 2 should have been evicted as LRU.");
        Assert.That(cache.Get(1), Is.Not.Null, "Key 1 was promoted so it should survive.");
        Assert.That(cache.Get(3), Is.Not.Null, "Key 3 should still be in cache.");
        Assert.That(cache.Get(4), Is.Not.Null, "Key 4 should be in cache.");
    }

    [Test]
    public void AddItem_ReaddingExistingKey_ProtectsItFromEviction()
    {
        // Add 1, 2, 3 → re-add 1 promotes it to MRU → add 4 → 2 (new LRU) evicted, not 1
        var cache = MakeCache(capacity: 3);

        cache.AddItem(1);
        cache.AddItem(2);
        cache.AddItem(3);
        cache.AddItem(1); // refresh: order [1, 3, 2]
        cache.AddItem(4); // evicts 2

        Assert.That(cache.Get(2), Is.Null,     "Key 2 should be evicted.");
        Assert.That(cache.Get(1), Is.Not.Null, "Key 1 was re-added so it should survive.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Get
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Get_ExistingKey_ReturnsStoredTimestamp()
    {
        var cache = MakeCache();

        cache.AddItem(42);           // tick 0 → T+0s
        var result = cache.Get(42);  // tick 1 (clock not used here, just promotes)

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value, Is.EqualTo(BaseTime.AddSeconds(0)),
            "Get should return the timestamp that was set when the item was added.");
    }

    [Test]
    public void Get_MissingKey_ReturnsNull()
    {
        var cache = MakeCache();

        var result = cache.Get(999);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Get_ExistingKey_PromotesToMRU()
    {
        // Add 1, 2, 3 → Get(1) → add 4 → 2 must be evicted (not 1)
        var cache = MakeCache(capacity: 3);

        cache.AddItem(1);
        cache.AddItem(2);
        cache.AddItem(3);
        cache.Get(1);     // promotes 1; LRU order: [1, 3, 2]
        cache.AddItem(4); // should evict 2, not 1

        Assert.That(cache.Get(1), Is.Not.Null, "Get should have promoted key 1 away from LRU.");
        Assert.That(cache.Get(2), Is.Null,     "Key 2 should be the evicted LRU.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LRU ordering — end-to-end eviction
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Eviction_LRUOrderRespected_StrictInsertionOrder()
    {
        // No gets; items evict strictly in insertion order (FIFO of least-recently-used)
        var cache = MakeCache(capacity: 3);

        cache.AddItem(10);
        cache.AddItem(20);
        cache.AddItem(30);
        cache.AddItem(40); // evicts 10

        Assert.That(cache.Get(10), Is.Null,     "10 is the oldest — should be evicted first.");
        Assert.That(cache.Get(20), Is.Not.Null);
        Assert.That(cache.Get(30), Is.Not.Null);
        Assert.That(cache.Get(40), Is.Not.Null);
    }

    [Test]
    public void Eviction_MultipleEvictions_AllCorrect()
    {
        var cache = MakeCache(capacity: 2);

        cache.AddItem(1);
        cache.AddItem(2);
        cache.AddItem(3); // evicts 1
        cache.AddItem(4); // evicts 2

        Assert.That(cache.Get(1), Is.Null, "Key 1 should be evicted.");
        Assert.That(cache.Get(2), Is.Null, "Key 2 should be evicted.");
        Assert.That(cache.Get(3), Is.Not.Null);
        Assert.That(cache.Get(4), Is.Not.Null);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Capacity boundary
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void AddItem_ExactlyAtCapacity_NoEviction()
    {
        var cache = MakeCache(capacity: 3);

        cache.AddItem(1);
        cache.AddItem(2);
        cache.AddItem(3); // exactly at capacity — no eviction yet

        Assert.That(cache.Count, Is.EqualTo(3));
        Assert.That(cache.Get(1), Is.Not.Null, "Key 1 should NOT be evicted yet.");
    }

    [Test]
    public void AddItem_OneOverCapacity_ExactlyOneEviction()
    {
        var cache = MakeCache(capacity: 3);

        cache.AddItem(1);
        cache.AddItem(2);
        cache.AddItem(3);
        cache.AddItem(4); // one over → exactly one eviction

        Assert.That(cache.Count, Is.EqualTo(3), "Count should stay at capacity after eviction.");
    }

    [Test]
    public void AddItem_CapacityOfOne_AlwaysReplacesOnNewKey()
    {
        var cache = MakeCache(capacity: 1);

        cache.AddItem(1);
        cache.AddItem(2); // evicts 1

        Assert.That(cache.Count,  Is.EqualTo(1));
        Assert.That(cache.Get(1), Is.Null,     "Capacity-1 cache: key 1 should be gone.");
        Assert.That(cache.Get(2), Is.Not.Null, "Capacity-1 cache: key 2 should be present.");
    }
}
