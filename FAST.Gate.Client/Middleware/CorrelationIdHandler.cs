namespace FAST.Gate.Client.Middleware;

/// <summary>
/// DelegatingHandler that threads the X-Request-Id correlation ID
/// through every outgoing HTTP request made via the GateAuthClient.
///
/// If the current context already has a request ID (set by inbound middleware),
/// it is reused. Otherwise a new UUID is generated.
///
/// The same ID is returned in the response header and present in all proxy log lines,
/// enabling full end-to-end trace from caller → FAST.Gate → Logto.
/// </summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    public const string HeaderName = "X-Request-Id";

    private readonly ICorrelationIdAccessor _accessor;

    public CorrelationIdHandler(ICorrelationIdAccessor accessor)
    {
        _accessor = accessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = _accessor.GetCurrentId() ?? Guid.NewGuid().ToString("N");

        if (!request.Headers.Contains(HeaderName))
            request.Headers.TryAddWithoutValidation(HeaderName, correlationId);

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Provides access to the current request's correlation ID.
/// Implement this in the host application (e.g. via IHttpContextAccessor)
/// and register in DI.
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>Returns the current correlation ID, or null if none is active.</summary>
    string? GetCurrentId();
}

/// <summary>
/// Default implementation that always generates a new ID.
/// Replace with an IHttpContextAccessor-based implementation in ASP.NET Core hosts.
/// </summary>
public sealed class DefaultCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string? GetCurrentId() => null; // Forces a new UUID per request
}
