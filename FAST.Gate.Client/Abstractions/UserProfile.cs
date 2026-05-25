namespace FAST.Gate.Client.Abstractions;

/// <summary>
/// Represents the authenticated user's profile.
/// Returned as part of <see cref="FastAuthResult"/> on every successful authentication.
/// This is the unified profile across the entire FAST ecosystem —
/// FAST applications never deal with Logto-specific user objects.
/// </summary>
public sealed record UserProfile
{
    /// <summary>Unique user identifier (from the IdP).</summary>
    public required string Id { get; init; }

    /// <summary>The username used to log in.</summary>
    public required string LoginName { get; init; }

    /// <summary>User's first name.</summary>
    public required string FirstName { get; init; }

    /// <summary>User's last name.</summary>
    public required string LastName { get; init; }

    /// <summary>Full display name (FirstName + LastName).</summary>
    public string DisplayName => $"{FirstName} {LastName}".Trim();

    /// <summary>User's email address.</summary>
    public required string Email { get; init; }

    /// <summary>URL to the user's avatar/profile picture. Null if not set.</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// The tenant (organization/company) this user belongs to.
    /// Null for FAST super-admins who operate outside any tenant.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Roles assigned to this user.
    /// Mix of global roles (e.g. "fast.flowchart.user") and
    /// app-specific roles (e.g. "erp.accounts.admin").
    /// </summary>
    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>
    /// Fine-grained abilities assigned to this user.
    /// Below roles — e.g. "erp.accounts.delete".
    /// Empty collection by default, populated as the ecosystem grows.
    /// </summary>
    public IReadOnlyList<string> Abilities { get; init; } = [];
}
