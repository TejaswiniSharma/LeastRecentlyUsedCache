using System.Security.Cryptography;
using Round2;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton(new LRUCache(capacity: 100));

// Port 0 → OS assigns the next available port automatically
builder.WebHost.UseUrls("http://127.0.0.1:0");

var app = builder.Build();

// ── API Key ───────────────────────────────────────────────────────────────────
// Generate a fresh cryptographically random key on every server start.
// Write it to a temp file so the client can discover it without any
// out-of-band configuration step.
var apiKey     = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); // 256-bit hex
var keyFile    = Path.Combine(Path.GetTempPath(), "lrucache_server.apikey");
await File.WriteAllTextAsync(keyFile, apiKey);
Console.WriteLine($"[Server] API key written to  {keyFile}");

app.UseMiddleware<ApiKeyMiddleware>(apiKey);
app.MapControllers();

await app.StartAsync();

var port     = new Uri(app.Urls.First()).Port;
var portFile = Path.Combine(Path.GetTempPath(), "lrucache_server.port");
await File.WriteAllTextAsync(portFile, port.ToString());

Console.WriteLine($"[Server] Listening on        http://127.0.0.1:{port}");
Console.WriteLine($"[Server] Port written to     {portFile}");

await app.WaitForShutdownAsync();
