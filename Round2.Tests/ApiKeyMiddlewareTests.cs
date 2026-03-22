using Microsoft.AspNetCore.Http;

namespace Round2.Tests;

/// <summary>
/// Tests for <see cref="ApiKeyMiddleware"/>.
///
/// Every test exercises the middleware in isolation using a plain
/// <see cref="DefaultHttpContext"/> — no server or network involved.
///
/// Failure cases (no key / wrong key) confirm the middleware short-circuits
/// with HTTP 401 and never forwards the request to the next handler.
/// </summary>
[TestFixture]
public class ApiKeyMiddlewareTests
{
    private const string ValidKey = "test-api-key-abc123";

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a DefaultHttpContext with a writable response body so
    /// WriteAsJsonAsync (used inside the middleware) doesn't throw.
    /// </summary>
    private static DefaultHttpContext MakeContext() =>
        new() { Response = { Body = new MemoryStream() } };

    /// <summary>
    /// Builds and invokes the middleware.
    /// Returns <c>(statusCode, nextWasCalled)</c> so callers can assert on
    /// both the HTTP status and whether the request reached the next handler.
    /// </summary>
    private static async Task<(int StatusCode, bool NextWasCalled)> InvokeAsync(
        HttpContext context,
        string      configuredKey)
    {
        var nextCalled = false;
        var middleware  = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            configuredKey);

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Failure cases — should all return 401 and block the pipeline
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Request_WithNoApiKeyHeader_Returns401()
    {
        var context = MakeContext();
        // No header added — simulates a raw unauthenticated request

        var (status, nextCalled) = await InvokeAsync(context, ValidKey);

        Assert.That(status,    Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(nextCalled, Is.False, "Pipeline must be blocked — next middleware should NOT run.");
    }

    [Test]
    public async Task Request_WithWrongApiKey_Returns401()
    {
        var context = MakeContext();
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = "completely-wrong-key";

        var (status, nextCalled) = await InvokeAsync(context, ValidKey);

        Assert.That(status,    Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(nextCalled, Is.False, "A wrong key must not reach the controller.");
    }

    [Test]
    public async Task Request_WithEmptyApiKey_Returns401()
    {
        var context = MakeContext();
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = string.Empty;

        var (status, nextCalled) = await InvokeAsync(context, ValidKey);

        Assert.That(status,    Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(nextCalled, Is.False, "An empty key is treated the same as no key.");
    }

    [Test]
    public async Task Request_WithWhiteSpaceApiKey_Returns401()
    {
        var context = MakeContext();
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = "   ";

        var (status, nextCalled) = await InvokeAsync(context, ValidKey);

        Assert.That(status,    Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(nextCalled, Is.False, "A whitespace-only key must be rejected.");
    }

    [Test]
    public async Task Request_WithPartiallyCorrectApiKey_Returns401()
    {
        var context = MakeContext();
        // First half of the valid key — not a full match
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = ValidKey[..(ValidKey.Length / 2)];

        var (status, nextCalled) = await InvokeAsync(context, ValidKey);

        Assert.That(status,    Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(nextCalled, Is.False, "A partial key match must still be rejected.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Success case — correct key must pass through to the next middleware
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Request_WithCorrectApiKey_CallsNextMiddleware()
    {
        var context = MakeContext();
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = ValidKey;

        var (status, nextCalled) = await InvokeAsync(context, ValidKey);

        Assert.That(nextCalled, Is.True,  "A valid key must forward the request to the next handler.");
        Assert.That(status,     Is.Not.EqualTo(StatusCodes.Status401Unauthorized));
    }
}
