using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Round2;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton(new LRUCache(capacity: 100));

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
//  1. ApiKeyMiddleware  — reject unauthenticated requests before they hit the limiter
//  2. UseRateLimiter    — apply per-client token bucket to authenticated requests
//  3. MapControllers    — route to controller actions
app.UseMiddleware<ApiKeyMiddleware>(apiKey);
app.UseRateLimiter();
app.MapControllers();

await app.StartAsync();

var port     = new Uri(app.Urls.First()).Port;
var portFile = Path.Combine(Path.GetTempPath(), "lrucache_server.port");
await File.WriteAllTextAsync(portFile, port.ToString());

Console.WriteLine($"[Server] Listening on        http://127.0.0.1:{port}");
Console.WriteLine($"[Server] Port written to     {portFile}");

await app.WaitForShutdownAsync();
