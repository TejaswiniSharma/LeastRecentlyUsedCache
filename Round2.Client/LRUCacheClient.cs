using System.Net.Http.Json;
using System.Text.Json;

namespace Round2.Client;

// ── Response models (mirror the server's) ────────────────────────────────────

public record AddResponse(int Key, DateTime Timestamp, int CurrentCount);
public record GetResponse(int Key, DateTime Timestamp);
public record StatsResponse(
    int    CurrentCount,
    int    Capacity,
    long   HitCount,
    long   MissCount,
    long   EvictionCount,
    double HitRatePct);

// ── Client ────────────────────────────────────────────────────────────────────

/// <summary>
/// Thin HTTP client that talks to the LRU Cache Web API running on localhost.
/// The API key is attached to every request via the <c>X-Api-Key</c> header.
/// </summary>
public class LRUCacheClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LRUCacheClient(string baseUrl, string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        // Set once — every request on this instance will carry the header
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    /// <summary>POST /api/cache/add — adds a key to the cache.</summary>
    public async Task<AddResponse> AddAsync(int key)
    {
        var response = await _http.PostAsJsonAsync("/api/cache/add", new { key });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddResponse>(JsonOptions))!;
    }

    /// <summary>
    /// GET /api/cache/{key} — retrieves a key and promotes it to MRU.
    /// Returns null on a cache miss.
    /// </summary>
    public async Task<GetResponse?> GetAsync(int key)
    {
        var response = await _http.GetAsync($"/api/cache/{key}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GetResponse>(JsonOptions);
    }

    /// <summary>GET /api/cache/stats — returns count, capacity and lifetime metrics.</summary>
    public async Task<StatsResponse> GetStatsAsync()
    {
        var response = await _http.GetAsync("/api/cache/stats");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StatsResponse>(JsonOptions))!;
    }

    /// <summary>GET /health — returns server health status as a JSON string (no auth required).</summary>
    public async Task<string> GetHealthAsync()
    {
        var response = await _http.GetAsync("/health");
        return await response.Content.ReadAsStringAsync();
    }

    public void Dispose() => _http.Dispose();
}
