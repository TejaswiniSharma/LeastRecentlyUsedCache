namespace Round2.Client;

/// <summary>
/// Drives the LRU Cache server through a series of test scenarios,
/// printing results to the console.
/// </summary>
public static class TestRunner
{
    public static async Task RunAsync(LRUCacheClient client)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║            LRU Cache  —  Test Runner             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝\n");

        // ── Scenario 1: Populate the cache ───────────────────────────────────
        await Section("1. Add keys 1–10");

        for (int i = 1; i <= 10; i++)
        {
            AddResponse r = await client.AddAsync(i);
            Console.WriteLine($"  ADD  key={r.Key,-4} ts={r.Timestamp:HH:mm:ss.fff}  count={r.CurrentCount}");
        }

        await PrintStats(client);

        // ── Scenario 2: Read some keys (promotes to MRU) ─────────────────────
        await Section("2. Read keys 1, 3, 5  (should promote them to MRU)");

        foreach (int key in new[] { 1, 3, 5 })
        {
            GetResponse? r = await client.GetAsync(key);
            Console.WriteLine(r is null
                ? $"  GET  key={key,-4} → MISS"
                : $"  GET  key={key,-4} → HIT   ts={r.Timestamp:HH:mm:ss.fff}");
        }

        // ── Scenario 3: Cache miss ────────────────────────────────────────────
        await Section("3. Read a key that was never added (key=99)");

        GetResponse? miss = await client.GetAsync(99);
        Console.WriteLine(miss is null ? "  GET  key=99  → MISS  (correct)" : $"  Unexpected hit: {miss}");

        // ── Scenario 4: Add duplicate key (refresh) ───────────────────────────
        await Section("4. Re-add key=2  (should refresh timestamp and move to MRU)");

        AddResponse refresh = await client.AddAsync(2);
        Console.WriteLine($"  ADD  key={refresh.Key}  ts={refresh.Timestamp:HH:mm:ss.fff}  count={refresh.CurrentCount}");

        // ── Scenario 5: Fill to capacity then overflow ────────────────────────
        // Server capacity is 100. We currently have 10 items. Add 90 more then
        // one extra so eviction fires.
        await Section("5. Fill cache to capacity (add keys 11–100), then add key=101 to trigger eviction");

        for (int i = 11; i <= 100; i++)
            await client.AddAsync(i);

        await PrintStats(client);

        Console.WriteLine("  Adding key=101  → should evict the current LRU...");
        AddResponse overflow = await client.AddAsync(101);
        Console.WriteLine($"  ADD  key=101  count={overflow.CurrentCount}");

        await PrintStats(client);

        // ── Scenario 6: Verify evicted key is gone ────────────────────────────
        // LRU order after scenario 4: key=2 was refreshed (MRU), keys 1,3,5 were
        // read (MRU side), remaining insertion order 4,6,7,8,9,10 → LRU is key=4.
        await Section("6. Verify evicted key=4 is no longer in cache");

        GetResponse? evictedCheck = await client.GetAsync(4);
        Console.WriteLine(evictedCheck is null
            ? "  GET  key=4  → MISS  (evicted correctly)"
            : $"  GET  key=4  → HIT   ts={evictedCheck.Timestamp:HH:mm:ss.fff}  (still in cache)");

        Console.WriteLine("\n✓ Test run complete.");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Task Section(string title)
    {
        Console.WriteLine($"\n── {title}");
        return Task.CompletedTask;
    }

    private static async Task PrintStats(LRUCacheClient client)
    {
        StatsResponse s = await client.GetStatsAsync();
        Console.WriteLine($"  [stats]  count={s.CurrentCount}  capacity={s.Capacity}");
    }
}
