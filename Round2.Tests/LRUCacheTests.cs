using NUnit.Framework;

namespace Round2.Tests;

/// <summary>
/// Unit tests for <see cref="LRUCache"/>.
///
/// Sequential tests use a deterministic fake clock (increments by 1 s per tick).
/// Concurrency tests use a thread-safe clock backed by <see cref="Interlocked.Increment"/>
/// so ticks are unique even when multiple threads call the clock simultaneously.
/// </summary>
[TestFixture]
public class LRUCacheTests
{
    // ── Sequential fake clock ─────────────────────────────────────────────────

    private static readonly DateTime BaseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private int _tick;

    private DateTime FakeClock() => BaseTime.AddSeconds(_tick++);

    [SetUp]
    public void SetUp() => _tick = 0;

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
    public async Task AddItem_NewKey_IncreasesCount()
    {
        var cache = MakeCache();

        await cache.AddItemAsync(1);

        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task AddItem_DuplicateKey_DoesNotIncreaseCount()
    {
        var cache = MakeCache();

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(1);

        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task AddItem_DuplicateKey_RefreshesTimestamp()
    {
        var cache = MakeCache();

        await cache.AddItemAsync(1);                        // tick 0 → T+0s
        var firstTimestamp = (await cache.GetAsync(1))!.Value;

        await cache.AddItemAsync(1);                        // tick 2 → T+2s
        var refreshedTimestamp = (await cache.GetAsync(1))!.Value;

        Assert.That(refreshedTimestamp, Is.GreaterThan(firstTimestamp),
            "Re-adding an existing key should update its timestamp.");
    }

    [Test]
    public async Task AddItem_AtCapacity_EvictsLRU()
    {
        var cache = MakeCache(capacity: 2);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2);
        await cache.AddItemAsync(3); // triggers eviction of key 1

        Assert.That(cache.Count,               Is.EqualTo(2));
        Assert.That(await cache.GetAsync(1),   Is.Null,     "Key 1 should have been evicted.");
        Assert.That(await cache.GetAsync(2),   Is.Not.Null, "Key 2 should still be in cache.");
        Assert.That(await cache.GetAsync(3),   Is.Not.Null, "Key 3 should be in cache.");
    }

    [Test]
    public async Task AddItem_EvictsCorrectLRU_AfterGet()
    {
        // Add 1, 2, 3 → Get(1) promotes 1 to MRU → add 4 → 2 is now LRU, must be evicted
        var cache = MakeCache(capacity: 3);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2);
        await cache.AddItemAsync(3);
        await cache.GetAsync(1);      // promotes 1 → order: [1, 3, 2]
        await cache.AddItemAsync(4);  // evicts 2

        Assert.That(await cache.GetAsync(2), Is.Null,     "Key 2 should have been evicted as LRU.");
        Assert.That(await cache.GetAsync(1), Is.Not.Null, "Key 1 was promoted so it should survive.");
        Assert.That(await cache.GetAsync(3), Is.Not.Null, "Key 3 should still be in cache.");
        Assert.That(await cache.GetAsync(4), Is.Not.Null, "Key 4 should be in cache.");
    }

    [Test]
    public async Task AddItem_ReaddingExistingKey_ProtectsItFromEviction()
    {
        // Add 1, 2, 3 → re-add 1 → add 4 → 2 (new LRU) evicted, not 1
        var cache = MakeCache(capacity: 3);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2);
        await cache.AddItemAsync(3);
        await cache.AddItemAsync(1); // refresh: order [1, 3, 2]
        await cache.AddItemAsync(4); // evicts 2

        Assert.That(await cache.GetAsync(2), Is.Null,     "Key 2 should be evicted.");
        Assert.That(await cache.GetAsync(1), Is.Not.Null, "Key 1 was re-added so it should survive.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Get
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Get_ExistingKey_ReturnsStoredTimestamp()
    {
        var cache = MakeCache();

        await cache.AddItemAsync(42);              // tick 0 → T+0s
        var result = await cache.GetAsync(42);

        Assert.That(result,        Is.Not.Null);
        Assert.That(result!.Value, Is.EqualTo(BaseTime.AddSeconds(0)),
            "Get should return the timestamp that was set when the item was added.");
    }

    [Test]
    public async Task Get_MissingKey_ReturnsNull()
    {
        var cache = MakeCache();

        var result = await cache.GetAsync(999);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Get_ExistingKey_PromotesToMRU()
    {
        // Add 1, 2, 3 → Get(1) → add 4 → 2 must be evicted (not 1)
        var cache = MakeCache(capacity: 3);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2);
        await cache.AddItemAsync(3);
        await cache.GetAsync(1);      // promotes 1 → LRU order: [1, 3, 2]
        await cache.AddItemAsync(4);  // should evict 2, not 1

        Assert.That(await cache.GetAsync(1), Is.Not.Null, "Get should have promoted key 1 away from LRU.");
        Assert.That(await cache.GetAsync(2), Is.Null,     "Key 2 should be the evicted LRU.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LRU ordering — end-to-end eviction
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Eviction_LRUOrderRespected_StrictInsertionOrder()
    {
        var cache = MakeCache(capacity: 3);

        await cache.AddItemAsync(10);
        await cache.AddItemAsync(20);
        await cache.AddItemAsync(30);
        await cache.AddItemAsync(40); // evicts 10

        Assert.That(await cache.GetAsync(10), Is.Null,     "10 is the oldest — should be evicted first.");
        Assert.That(await cache.GetAsync(20), Is.Not.Null);
        Assert.That(await cache.GetAsync(30), Is.Not.Null);
        Assert.That(await cache.GetAsync(40), Is.Not.Null);
    }

    [Test]
    public async Task Eviction_MultipleEvictions_AllCorrect()
    {
        var cache = MakeCache(capacity: 2);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2);
        await cache.AddItemAsync(3); // evicts 1
        await cache.AddItemAsync(4); // evicts 2

        Assert.That(await cache.GetAsync(1), Is.Null, "Key 1 should be evicted.");
        Assert.That(await cache.GetAsync(2), Is.Null, "Key 2 should be evicted.");
        Assert.That(await cache.GetAsync(3), Is.Not.Null);
        Assert.That(await cache.GetAsync(4), Is.Not.Null);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Capacity boundary
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task AddItem_ExactlyAtCapacity_NoEviction()
    {
        var cache = MakeCache(capacity: 3);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2);
        await cache.AddItemAsync(3); // exactly at capacity — no eviction yet

        Assert.That(cache.Count,              Is.EqualTo(3));
        Assert.That(await cache.GetAsync(1),  Is.Not.Null, "Key 1 should NOT be evicted yet.");
    }

    [Test]
    public async Task AddItem_OneOverCapacity_ExactlyOneEviction()
    {
        var cache = MakeCache(capacity: 3);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2);
        await cache.AddItemAsync(3);
        await cache.AddItemAsync(4); // one over → exactly one eviction

        Assert.That(cache.Count, Is.EqualTo(3), "Count should stay at capacity after eviction.");
    }

    [Test]
    public async Task AddItem_CapacityOfOne_AlwaysReplacesOnNewKey()
    {
        var cache = MakeCache(capacity: 1);

        await cache.AddItemAsync(1);
        await cache.AddItemAsync(2); // evicts 1

        Assert.That(cache.Count,              Is.EqualTo(1));
        Assert.That(await cache.GetAsync(1),  Is.Null,     "Capacity-1 cache: key 1 should be gone.");
        Assert.That(await cache.GetAsync(2),  Is.Not.Null, "Capacity-1 cache: key 2 should be present.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Concurrency
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thread-safe clock: each call gets a unique tick even from multiple threads.
    /// </summary>
    private int _concurrentTick;
    private DateTime ThreadSafeClock() =>
        BaseTime.AddSeconds(Interlocked.Increment(ref _concurrentTick));

    [Test]
    public async Task Concurrent_AddItems_CountNeverExceedsCapacity()
    {
        const int capacity   = 50;
        const int goroutines = 200;
        var cache = new LRUCache(capacity, ThreadSafeClock);

        // Fire 200 AddItemAsync calls simultaneously; keys cycle through 0–49
        var tasks = Enumerable
            .Range(0, goroutines)
            .Select(i => cache.AddItemAsync(i % capacity));

        await Task.WhenAll(tasks);

        Assert.That(cache.Count, Is.LessThanOrEqualTo(capacity),
            "Count must never exceed capacity regardless of concurrency.");
    }

    [Test]
    public async Task Concurrent_MixedAddAndGet_NoExceptionThrown()
    {
        const int capacity  = 20;
        const int taskCount = 100;
        var cache = new LRUCache(capacity, ThreadSafeClock);

        // Pre-fill so Gets have something to find
        for (int i = 0; i < capacity; i++)
            await cache.AddItemAsync(i);

        // Mix adds (new keys) and gets (existing keys) all at once
        var adds = Enumerable.Range(capacity, taskCount)
                             .Select(i => cache.AddItemAsync(i));

        var gets = Enumerable.Range(0, taskCount)
                             .Select(i => cache.GetAsync(i % capacity));

        // Cast to Task so the two sequences share a common type for WhenAll
        var allTasks = adds.Cast<Task>().Concat(gets.Cast<Task>());

        Assert.DoesNotThrowAsync(
            () => Task.WhenAll(allTasks),
            "Mixed concurrent adds and gets must not throw any exception.");

        Assert.That(cache.Count, Is.LessThanOrEqualTo(capacity));
    }
}
