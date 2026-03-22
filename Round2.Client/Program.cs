using Round2.Client;

// ── Discover port ─────────────────────────────────────────────────────────────
var portFile = Path.Combine(Path.GetTempPath(), "lrucache_server.port");
var keyFile  = Path.Combine(Path.GetTempPath(), "lrucache_server.apikey");

if (!File.Exists(portFile) || !File.Exists(keyFile))
{
    Console.Error.WriteLine("ERROR: Server discovery files not found.");
    Console.Error.WriteLine($"  Expected: {portFile}");
    Console.Error.WriteLine($"  Expected: {keyFile}");
    Console.Error.WriteLine("Make sure the Round2 server is running first:");
    Console.Error.WriteLine("  dotnet run --project Round2");
    return;
}

var port      = int.Parse((await File.ReadAllTextAsync(portFile)).Trim());
var apiKey    = (await File.ReadAllTextAsync(keyFile)).Trim();
var serverUrl = $"http://localhost:{port}";

Console.WriteLine($"Connecting to LRU Cache server at {serverUrl}");
Console.WriteLine($"Using API key: {apiKey[..8]}...{apiKey[^4..]}  ({apiKey.Length} chars)\n");

using var client = new LRUCacheClient(serverUrl, apiKey);

// Quick connectivity check
try
{
    await client.GetStatsAsync();
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"ERROR: Cannot reach the server — {ex.Message}");
    return;
}

await TestRunner.RunAsync(client);
