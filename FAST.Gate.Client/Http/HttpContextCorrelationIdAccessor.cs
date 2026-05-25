using FAST.Gate.Client.Middleware;
using Microsoft.AspNetCore.Http;

namespace FAST.Gate.Client.Http;

/// <summary>
/// ASP.NET Core implementation of <see cref="ICorrelationIdAccessor"/>.
/// Reads X-Request-Id from the current HTTP context, enabling
/// inbound correlation IDs to propagate automatically through
/// every outgoing GateAuthClient call made within a request scope.
///
/// Register in ASP.NET Core hosts instead of the default:
/// <code>
/// builder.Services.AddHttpContextAccessor();
/// builder.Services.AddFastGateClient(builder.Configuration,
///     new HttpContextCorrelationIdAccessor(
///         builder.Services.BuildServiceProvider()
///             .GetRequiredService&lt;IHttpContextAccessor&gt;()));
/// </code>
///
/// Or with DI directly:
/// <code>
/// builder.Services.AddHttpContextAccessor();
/// builder.Services.AddSingleton&lt;ICorrelationIdAccessor, HttpContextCorrelationIdAccessor&gt;();
/// builder.Services.AddFastGateClient(builder.Configuration);
/// </code>
/// </summary>
public sealed class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
{
    public const string HeaderName = "X-Request-Id";

    private readonly IHttpContextAccessor _contextAccessor;

    public HttpContextCorrelationIdAccessor(IHttpContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    public string? GetCurrentId()
    {
        var context = _contextAccessor.HttpContext;
        if (context is null) return null;

        // Prefer the value stored by CorrelationIdMiddleware in Items
        // (already generated or adopted from the inbound header)
        if (context.Items.TryGetValue(HeaderName, out var fromItems) && fromItems is string id)
            return id;

        // Fall back to reading the header directly
        return context.Request.Headers[HeaderName].FirstOrDefault();
    }
}
