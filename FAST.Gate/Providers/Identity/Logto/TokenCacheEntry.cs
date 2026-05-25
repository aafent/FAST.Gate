using FAST.Gate.Client.Abstractions;

namespace FAST.Gate.Providers.Identity.Logto;

internal sealed record TokenCacheEntry(
    FastAuthResult Result,
    DateTimeOffset EvictAt)
{
    public DateTimeOffset CachedAt { get; } = DateTimeOffset.UtcNow;
}
