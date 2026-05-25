using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FAST.Gate.Client.Abstractions;
using FAST.Gate.IntegrationTests.Helpers;
using FluentAssertions;

namespace FAST.Gate.IntegrationTests;

public sealed class AuthEndpointsTests : IAsyncDisposable
{
    private readonly GateWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    public AuthEndpointsTests()
    {
        _factory = new GateWebApplicationFactory();
        _client  = _factory.CreateClient();
    }

    // ── GET /auth/sso/url ─────────────────────────────────────────────────────

    [Fact]
    public async Task SsoUrl_AllowedRedirectUri_Returns200WithUrlAndState()
    {
        var redirectUri = Uri.EscapeDataString("https://testapp.fast.internal/auth/callback");
        var response = await _client.GetAsync($"/auth/sso/url?redirectUri={redirectUri}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("url");
        body.Should().Contain("state");
    }

    [Fact]
    public async Task SsoUrl_UrlContainsLogtoAuthorizationEndpoint()
    {
        var redirectUri = Uri.EscapeDataString("https://testapp.fast.internal/auth/callback");
        var response = await _client.GetAsync($"/auth/sso/url?redirectUri={redirectUri}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("response_type=code");
        body.Should().Contain("client_id=");
        body.Should().Contain("redirect_uri=");
    }

    [Fact]
    public async Task SsoUrl_DisallowedRedirectUri_Returns400()
    {
        var redirectUri = Uri.EscapeDataString("https://evil.com/steal-tokens");
        var response = await _client.GetAsync($"/auth/sso/url?redirectUri={redirectUri}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SsoUrl_ResponseContainsCorrelationId()
    {
        var redirectUri = Uri.EscapeDataString("https://testapp.fast.internal/auth/callback");
        var response = await _client.GetAsync($"/auth/sso/url?redirectUri={redirectUri}");

        response.Headers.Should().ContainKey("X-Request-Id");
    }

    [Fact]
    public async Task SsoUrl_WithTenantId_IncludesOrganizationId()
    {
        var redirectUri = Uri.EscapeDataString("https://testapp.fast.internal/auth/callback");
        var response = await _client.GetAsync(
            $"/auth/sso/url?redirectUri={redirectUri}&tenantId=tenant-abc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("url");
    }

    // ── POST /auth/sso/exchange ───────────────────────────────────────────────

    [Fact]
    public async Task SsoExchange_ValidCode_Returns200WithFullAuthResult()
    {
        var response = await _client.PostAsJsonAsync("/auth/sso/exchange", new
        {
            code = "test-auth-code",
            redirectUri = "https://testapp.fast.internal/auth/callback"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<FastAuthResult>(response);
        result.IsSuccess.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.LoginName.Should().Be(GateWebApplicationFactory.TestUsername);
        result.User.TenantId.Should().Be(GateWebApplicationFactory.TestTenantId);
        result.User.Roles.Should().Contain("erp.admin");
        result.IdpAccessToken.Should().NotBeNullOrEmpty();
        result.IdpRefreshToken.Should().NotBeNullOrEmpty();
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        result.FastToken.Should().BeEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task SsoExchange_MissingCode_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/sso/exchange", new
        {
            code = "",
            redirectUri = "https://testapp.fast.internal/auth/callback"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadAsync<FastAuthResult>(response);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(GateErrorCodes.MissingCredentials);
    }

    // ── POST /auth/refresh ────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidRefreshToken_Returns200WithTokenRefreshedTrue()
    {
        var response = await _client.PostAsJsonAsync("/auth/refresh",
            new { refreshToken = "test-refresh-token" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<FastAuthResult>(response);
        result.IsSuccess.Should().BeTrue();
        result.TokenRefreshed.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_MissingRefreshToken_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/refresh",
            new { refreshToken = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /auth/validate ────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_ValidToken_Returns200WithUserProfile()
    {
        var token = _factory.GenerateValidToken();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/auth/validate");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<FastAuthResult>(response);
        result.IsSuccess.Should().BeTrue();
        result.User.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_MissingToken_Returns401()
    {
        var response = await _client.GetAsync("/auth/validate");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var result = await ReadAsync<FastAuthResult>(response);
        result.Error!.Code.Should().Be(GateErrorCodes.MissingToken);
    }

    [Fact]
    public async Task Validate_ExpiredToken_Returns401WithTokenExpiredCode()
    {
        var expired = _factory.Jwt.GenerateExpiredToken(
            GateWebApplicationFactory.TestUserId,
            GateWebApplicationFactory.TestUsername);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", expired);

        var response = await _client.GetAsync("/auth/validate");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var result = await ReadAsync<FastAuthResult>(response);
        result.Error!.Code.Should().Be(GateErrorCodes.TokenExpired);
    }

    [Fact]
    public async Task Validate_SameToken_SecondCall_HitsCacheReturns200()
    {
        var token = _factory.GenerateValidToken();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var r1 = await _client.GetAsync("/auth/validate");
        var r2 = await _client.GetAsync("/auth/validate");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /auth/me ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_ValidToken_ReturnsFullUserProfile()
    {
        var token = _factory.GenerateValidToken();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await ReadAsync<UserProfile>(response);
        profile.LoginName.Should().Be(GateWebApplicationFactory.TestUsername);
        profile.Email.Should().Contain("@fast.internal");
        profile.AvatarUrl.Should().NotBeNullOrEmpty();
        profile.TenantId.Should().Be(GateWebApplicationFactory.TestTenantId);
        profile.Roles.Should().Contain("erp.admin");
    }

    [Fact]
    public async Task Me_NoToken_Returns401()
    {
        var response = await _client.GetAsync("/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /auth/logout ─────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ValidToken_Returns200()
    {
        var token = _factory.GenerateValidToken();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/auth/logout",
            new { refreshToken = "test-refresh-token" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /health ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Health_Returns200WhenProviderHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("healthy");
    }

    [Fact]
    public async Task Health_ResponseContainsProviderInfo()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("logto");
        body.Should().Contain("version");
    }

    // ── Correlation ID ────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_ResponseContainsCorrelationId()
    {
        var response = await _client.GetAsync("/health");
        response.Headers.Should().ContainKey("X-Request-Id");
    }

    [Fact]
    public async Task Request_InboundCorrelationId_IsPreserved()
    {
        var inboundId = "test-correlation-xyz-789";
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-Id", inboundId);

        var response = await _client.GetAsync("/health");
        response.Headers.GetValues("X-Request-Id").First().Should().Be(inboundId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(_json);
        result.Should().NotBeNull();
        return result!;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
