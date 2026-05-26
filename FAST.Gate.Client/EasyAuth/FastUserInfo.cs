// FastUserInfo.cs
namespace FAST.Gate.Client;

public sealed class FastUserInfo
{
    public string? UserId { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? TenantId { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Abilities { get; init; } = Array.Empty<string>();
}