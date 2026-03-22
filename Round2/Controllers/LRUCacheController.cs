using Microsoft.AspNetCore.Mvc;

namespace Round2.Controllers;

// ── Request / Response models ────────────────────────────────────────────────

public record AddRequest(int Key);

public record AddResponse(int Key, DateTime Timestamp, int CurrentCount);

public record GetResponse(int Key, DateTime Timestamp);

public record StatsResponse(int CurrentCount, int Capacity);

// ── Controller ───────────────────────────────────────────────────────────────

[ApiController]
[Route("api/cache")]
public class LRUCacheController : ControllerBase
{
    private readonly LRUCache _cache;

    public LRUCacheController(LRUCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// POST /api/cache/add
    /// Body: { "key": 42 }
    /// Adds the key to the cache (evicts LRU if at capacity).
    /// </summary>
    [HttpPost("add")]
    public ActionResult<AddResponse> Add([FromBody] AddRequest request)
    {
        _cache.AddItem(request.Key);

        // Read back the timestamp that was just stored
        DateTime timestamp = _cache.Get(request.Key)!.Value;

        return Ok(new AddResponse(request.Key, timestamp, _cache.Count));
    }

    /// <summary>
    /// GET /api/cache/{key}
    /// Returns the timestamp for the key and promotes it to MRU.
    /// Returns 404 if the key is not in the cache.
    /// </summary>
    [HttpGet("{key:int}")]
    public ActionResult<GetResponse> Get(int key)
    {
        DateTime? timestamp = _cache.Get(key);

        if (timestamp is null)
            return NotFound(new { message = $"Key {key} not found in cache." });

        return Ok(new GetResponse(key, timestamp.Value));
    }

    /// <summary>
    /// GET /api/cache/stats
    /// Returns current item count and configured capacity.
    /// </summary>
    [HttpGet("stats")]
    public ActionResult<StatsResponse> Stats()
    {
        return Ok(new StatsResponse(_cache.Count, _cache.Capacity));
    }
}
