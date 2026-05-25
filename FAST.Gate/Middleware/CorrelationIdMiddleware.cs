using Microsoft.AspNetCore.Http;

namespace FAST.Gate.Middleware;

/// <summary>
/// Ensures every request has a correlation ID.
/// - If X-Request-Id is present on the inbound request, it is adopted.
/// - Otherwise a new UUID is generated.
/// The ID is always echoed back in the response header.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Request-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        context.Items[HeaderName] = id;
        context.Response.Headers[HeaderName] = id;

        await _next(context);
    }
}
