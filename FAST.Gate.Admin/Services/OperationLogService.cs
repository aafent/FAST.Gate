namespace FAST.Gate.Admin.Services;

public sealed class OperationLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Method   { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public int    StatusCode { get; init; }
    public long   ElapsedMs  { get; init; }
    public bool   IsSuccess  { get; init; }
    public string? Detail   { get; init; }

    public string TimeDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string StatusDisplay => StatusCode > 0 ? $"{StatusCode}" : "ERR";
}

/// <summary>
/// Singleton service that holds the in-memory operation log.
/// All applets append entries here; the OpLog panel reads from it.
/// </summary>
public sealed class OperationLogService
{
    private readonly List<OperationLogEntry> _entries = [];
    private readonly object _lock = new();

    public event Action? OnChanged;

    public IReadOnlyList<OperationLogEntry> Entries
    {
        get { lock (_lock) return _entries.AsReadOnly(); }
    }

    public void Log(
        string method,
        string endpoint,
        int statusCode,
        long elapsedMs,
        bool isSuccess,
        string? detail = null)
    {
        var entry = new OperationLogEntry
        {
            Method      = method,
            Endpoint    = endpoint,
            StatusCode  = statusCode,
            ElapsedMs   = elapsedMs,
            IsSuccess   = isSuccess,
            Detail      = detail,
        };

        lock (_lock)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > 200) _entries.RemoveAt(_entries.Count - 1);
        }

        OnChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        OnChanged?.Invoke();
    }
}
