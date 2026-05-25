using FAST.Gate.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FAST.Gate.IntegrationTests.Helpers;

public sealed class GateWebApplicationFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    public WireMockServer MockServer { get; }
    public JwtTestHelper Jwt { get; }

    public const string TestUserId   = "user-test-1";
    public const string TestUsername = "testuser";
    public const string TestTenantId = "tenant-test-1";
    public static readonly string[] TestRoles = ["erp.admin", "fast.flowchart.user"];

    public GateWebApplicationFactory()
    {
        Jwt = new JwtTestHelper();
        MockServer = WireMockServer.Start();
        SetupMockLogto();
    }

    private void SetupMockLogto()
    {
        var validToken = Jwt.GenerateToken(TestUserId, TestUsername, TestRoles, tenantId: TestTenantId);

        // OIDC discovery
        MockServer
            .Given(Request.Create()
                .WithPath("/.well-known/openid-configuration").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Jwt.GetDiscoveryDocument(MockServer.Url!)));

        // JWKS
        MockServer
            .Given(Request.Create().WithPath("/oidc/jwks").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Jwt.GetJwksJson()));

        // Token endpoint — authorization_code exchange
        MockServer
            .Given(Request.Create().WithPath("/oidc/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($$"""
                {
                  "access_token": "{{validToken}}",
                  "refresh_token": "test-refresh-token",
                  "token_type": "Bearer",
                  "expires_in": 900
                }
                """));

        // Userinfo endpoint
        MockServer
            .Given(Request.Create().WithPath("/oidc/me").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Jwt.GetUserInfoJson(TestUserId, TestUsername, TestRoles, TestTenantId)));

        // Revocation endpoint
        MockServer
            .Given(Request.Create().WithPath("/oidc/revoke").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));
    }

    public string GenerateValidToken() =>
        Jwt.GenerateToken(TestUserId, TestUsername, TestRoles, tenantId: TestTenantId);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.Configure<GateOptions>(opts =>
            {
                opts.Identity.Provider = "logto";
                opts.Identity.Logto.IssuerUrl    = $"{MockServer.Url}/oidc";
                opts.Identity.Logto.ApiBaseUrl    = MockServer.Url;
                opts.Identity.Logto.Audience      = JwtTestHelper.TestAudience;
                opts.Identity.Logto.ServiceClientId     = "test-client-id";
                opts.Identity.Logto.ServiceClientSecret = "test-client-secret";
                opts.Identity.Logto.WebClientId     = "test-web-client-id";
                opts.Identity.Logto.WebClientSecret  = "test-web-client-secret";
                opts.Identity.Logto.ServiceRedirectUri = $"{MockServer.Url}/auth/internal-callback";
                opts.Cache.TokenCacheEnabled = true;
                opts.Cache.TokenCacheMaxSize = 100;
                opts.Sso.AllowedRedirectUris = ["https://testapp.fast.internal/auth/callback"];
            });
        });
    }

    public new async ValueTask DisposeAsync()
    {
        MockServer.Stop();
        MockServer.Dispose();
        Jwt.Dispose();
        await base.DisposeAsync();
    }
}
