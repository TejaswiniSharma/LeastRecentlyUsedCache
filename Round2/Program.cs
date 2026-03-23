using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Round2;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton(new LRUCache(capacity: 100));

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck<LRUCacheHealthCheck>("lrucache");

// Port 0 → OS assigns the next available port automatically
builder.WebHost.UseUrls("http://127.0.0.1:0");

// ── Rate Limiter ──────────────────────────────────────────────────────────────
// Token Bucket, partitioned per API key so every caller gets its own bucket.
//
//  TokenLimit          = 10  — bucket capacity (max burst)
//  TokensPerPeriod     = 10  — tokens refilled every second
//  QueueLimit          = 5   — up to 5 requests wait in line before rejecting
//  QueueProcessingOrder= OldestFirst — FIFO
//
// When the bucket AND the queue are both full, the request is rejected with
// 429 Too Many Requests + Retry-After: 1.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("per-client", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: httpContext.Request.Headers["X-Api-Key"].ToString(),
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit           = 10,
                TokensPerPeriod      = 10,
                ReplenishmentPeriod  = TimeSpan.FromSeconds(1),
                QueueLimit           = 5,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment    = true
            }));

    // Only reached when both the bucket AND the queue are full
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "1";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Rate limit exceeded. Request queue is full — retry after 1 second." },
            token);
    };
});

var app = builder.Build();

// ── API Key ───────────────────────────────────────────────────────────────────
var apiKey  = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var keyFile = Path.Combine(Path.GetTempPath(), "lrucache_server.apikey");
await File.WriteAllTextAsync(keyFile, apiKey);
Console.WriteLine($"[Server] API key written to  {keyFile}");

// Middleware order matters:
//  1. /health  — no auth, no rate limit (load balancers need unauthenticated access)
//  2. ApiKeyMiddleware  — reject unauthenticated requests before the limiter
//  3. UseRateLimiter    — per-client token bucket for authenticated requests
//  4. MapControllers    — route to controller actions
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name   = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data   = e.Value.Data
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
});

// Apply API key check to every route EXCEPT /health
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/health"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>(apiKey));

app.UseRateLimiter();
app.MapControllers();

await app.StartAsync();

var port     = new Uri(app.Urls.First()).Port;
var portFile = Path.Combine(Path.GetTempPath(), "lrucache_server.port");
await File.WriteAllTextAsync(portFile, port.ToString());

Console.WriteLine($"[Server] Listening on        http://127.0.0.1:{port}");
Console.WriteLine($"[Server] Port written to     {portFile}");

await app.WaitForShutdownAsync();
