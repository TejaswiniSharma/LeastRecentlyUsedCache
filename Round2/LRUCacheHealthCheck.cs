using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Round2;

/// <summary>
/// Reports the operational state of the LRU cache as an ASP.NET Core health check.
///
/// <list type="bullet">
///   <item><c>Healthy</c>   — cache is running and fill rate is below 90 %</item>
///   <item><c>Degraded</c>  — cache is running but fill rate is ≥ 90 % (evictions imminent)</item>
/// </list>
///
/// The response body always includes hit/miss/eviction counters so dashboards
/// and log scrapers can track cache efficiency alongside liveness.
/// </summary>
public class LRUCacheHealthCheck : IHealthCheck
{
    private readonly LRUCache _cache;

    public LRUCacheHealthCheck(LRUCache cache) => _cache = cache;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken  cancellationToken = default)
    {
        var fillRate = _cache.Capacity > 0
            ? (double)_cache.Count / _cache.Capacity
            : 0.0;

        var data = new Dictionary<string, object>
        {
            ["count"]         = _cache.Count,
            ["capacity"]      = _cache.Capacity,
            ["fillPct"]       = Math.Round(fillRate       * 100, 1),
            ["hitCount"]      = _cache.HitCount,
            ["missCount"]     = _cache.MissCount,
            ["evictionCount"] = _cache.EvictionCount,
            ["hitRatePct"]    = Math.Round(_cache.HitRate * 100, 1)
        };

        var result = fillRate >= 0.9
            ? HealthCheckResult.Degraded(
                $"Cache is {Math.Round(fillRate * 100, 1)} % full — evictions imminent.", data: data)
            : HealthCheckResult.Healthy("Cache is operational.", data: data);

        return Task.FromResult(result);
    }
}
