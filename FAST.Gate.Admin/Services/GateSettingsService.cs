using FAST.Gate.Client.Configuration;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Admin.Services;

/// <summary>
/// Scoped service that allows runtime override of GateBaseUrl before login.
/// Used by the pre-login setup screen.
/// </summary>
public sealed class GateSettingsService
{
    private readonly GateClientOptions _options;

    public GateSettingsService(IOptions<GateClientOptions> options)
    {
        _options = options.Value;
    }

    public string GateBaseUrl => _options.GateBaseUrl;
    public string SsoRedirectUri => _options.SsoRedirectUri ?? string.Empty;
}
