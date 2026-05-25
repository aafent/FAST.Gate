namespace FAST.Gate.Client.Abstractions;

public enum HealthState { Healthy, Degraded, Unhealthy }

/// <summary>Health report returned by providers and surfaced at /health.</summary>
public sealed record HealthStatus
{
    public required HealthState State { get; init; }
    public required string Component { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan? Latency { get; init; }

    public static HealthStatus Healthy(string component, TimeSpan? latency = null) =>
        new() { State = HealthState.Healthy, Component = component, Latency = latency };

    public static HealthStatus Degraded(string component, string detail) =>
        new() { State = HealthState.Degraded, Component = component, Detail = detail };

    public static HealthStatus Unhealthy(string component, string detail) =>
        new() { State = HealthState.Unhealthy, Component = component, Detail = detail };
}
