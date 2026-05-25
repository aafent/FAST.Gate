using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace FAST.Gate.IntegrationTests.Helpers;

/// <summary>
/// Generates real RSA-signed JWTs and matching JWKS/OIDC discovery documents
/// for integration tests — no live Logto required.
/// </summary>
public sealed class JwtTestHelper : IDisposable
{
    public const string TestIssuer   = "https://test-idp.fast.internal/oidc";
    public const string TestAudience = "https://fast.gate.internal";

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;
    private readonly SigningCredentials _signingCredentials;

    public string KeyId { get; } = Guid.NewGuid().ToString("N")[..8];

    public JwtTestHelper()
    {
        _rsa = RSA.Create(2048);
        _securityKey = new RsaSecurityKey(_rsa) { KeyId = KeyId };
        _signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);
    }

    public string GenerateToken(
        string sub,
        string username,
        string[] roles,
        int lifetimeSeconds = 900,
        string? tenantId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, sub),
            new("username", username),
            new(JwtRegisteredClaimNames.Email, $"{username}@fast.internal"),
            new("name", $"Test {username}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        foreach (var role in roles)
            claims.Add(new("roles", role));

        if (tenantId is not null)
            claims.Add(new("organization_id", tenantId));

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddSeconds(lifetimeSeconds),
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateExpiredToken(string sub, string username) =>
        GenerateToken(sub, username, [], lifetimeSeconds: -60);

    public string GetJwksJson()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var n = Base64UrlEncoder.Encode(parameters.Modulus!);
        var e = Base64UrlEncoder.Encode(parameters.Exponent!);

        return $$"""
        {
          "keys": [
            {
              "kty": "RSA",
              "use": "sig",
              "alg": "RS256",
              "kid": "{{KeyId}}",
              "n": "{{n}}",
              "e": "{{e}}"
            }
          ]
        }
        """;
    }

    public string GetDiscoveryDocument(string baseUrl) => $$"""
    {
      "issuer": "{{TestIssuer}}",
      "authorization_endpoint": "{{baseUrl}}/oidc/auth",
      "token_endpoint": "{{baseUrl}}/oidc/token",
      "userinfo_endpoint": "{{baseUrl}}/oidc/me",
      "jwks_uri": "{{baseUrl}}/oidc/jwks",
      "revocation_endpoint": "{{baseUrl}}/oidc/revoke",
      "response_types_supported": ["code"],
      "subject_types_supported": ["public"],
      "id_token_signing_alg_values_supported": ["RS256"]
    }
    """;

    public string GetUserInfoJson(string sub, string username, string[] roles, string? tenantId = null)
    {
        var rolesJson = string.Join(",", roles.Select(r => $"\"{r}\""));
        var orgJson = tenantId is not null ? $",\"organization_id\": \"{tenantId}\"" : "";
        return $$"""
        {
          "sub": "{{sub}}",
          "username": "{{username}}",
          "name": "Test {{username}}",
          "email": "{{username}}@fast.internal",
          "picture": "https://avatars.fast.internal/{{sub}}.png",
          "roles": [{{rolesJson}}]
          {{orgJson}}
        }
        """;
    }

    public void Dispose() => _rsa.Dispose();
}
