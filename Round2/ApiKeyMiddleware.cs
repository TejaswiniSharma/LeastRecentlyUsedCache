namespace Round2;

/// <summary>
/// Middleware that enforces API key authentication on every request.
/// The client must supply the key in the <c>X-Api-Key</c> request header.
/// </summary>
public class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly string _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, string apiKey)
    {
        _next        = next;
        _expectedKey = apiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var suppliedKey)
            || suppliedKey != _expectedKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                message = $"Missing or invalid '{HeaderName}' header."
            });
            return;
        }

        await _next(context);
    }
}
