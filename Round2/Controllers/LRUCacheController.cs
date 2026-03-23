using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Round2.Controllers;

// ── Request / Response models ────────────────────────────────────────────────

public record AddRequest(int Key);

public record AddResponse(int Key, DateTime Timestamp, int CurrentCount);

public record GetResponse(int Key, DateTime Timestamp);

public record StatsResponse(
    int    CurrentCount,
    int    Capacity,
    long   HitCount,
    long   MissCount,
    long   EvictionCount,
    double HitRatePct);      // 0–100, rounded to 2 dp

// ── Controller ───────────────────────────────────────────────────────────────

[ApiController]
[Route("api/cache")]
[EnableRateLimiting("per-client")]   // token bucket, keyed per API key
public class LRUCacheController : ControllerBase
{
    private readonly LRUCache _cache;

    public LRUCacheController(LRUCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// POST /api/cache/add
    /// Adds the key to the cache (evicts LRU if at capacity).
    /// Returns 429 if the caller's token bucket and queue are both full.
    /// </summary>
    [HttpPost("add")]
    public async Task<ActionResult<AddResponse>> Add([FromBody] AddRequest request)
    {
        DateTime timestamp = await _cache.AddItemAsync(request.Key);
        return Ok(new AddResponse(request.Key, timestamp, _cache.Count));
    }

    /// <summary>
    /// GET /api/cache/{key}
    /// Returns the timestamp for the key and promotes it to MRU.
    /// Returns 404 if the key is not in the cache.
    /// Returns 429 if the caller's token bucket and queue are both full.
    /// </summary>
    [HttpGet("{key:int}")]
    public async Task<ActionResult<GetResponse>> Get(int key)
    {
        DateTime? timestamp = await _cache.GetAsync(key);

        if (timestamp is null)
            return NotFound(new { message = $"Key {key} not found in cache." });

        return Ok(new GetResponse(key, timestamp.Value));
    }

    /// <summary>
    /// GET /api/cache/stats
    /// Returns current count, capacity, and lifetime hit/miss/eviction counters.
    /// All values are snapshot reads — eventually consistent under high concurrency.
    /// </summary>
    [HttpGet("stats")]
    public ActionResult<StatsResponse> Stats()
    {
        return Ok(new StatsResponse(
            CurrentCount:  _cache.Count,
            Capacity:      _cache.Capacity,
            HitCount:      _cache.HitCount,
            MissCount:     _cache.MissCount,
            EvictionCount: _cache.EvictionCount,
            HitRatePct:    Math.Round(_cache.HitRate * 100, 2)));
    }
}
