# FAST.Gate

The **single authentication and authorization gateway** for the FAST ecosystem.

Every FAST application — Blazor apps, web services, APIs — delegates all identity needs to FAST.Gate. FAST.Gate handles everything by delegating to Logto (or any future IdP). The FAST ecosystem never knows or cares about Logto directly.

[![See the docker container](https://img.shields.io/badge/See%20Docker%20Container-blue?style=for-the-badge)]([/aafent/FAST.Gate/pkgs/container/fast-gate](https://github.com/aafent/FAST.Gate/pkgs/container/fast-gate))
[![See Wiki for more](https://img.shields.io/badge/Wiki%20for%20more-green?style=for-the-badge)](https://github.com/aafent/FAST.Gate/wiki)

---

## What FAST.Gate provides

- **Authentication** — username/password, LDAP/AD, SSO (OAuth2/OIDC)
- **Authorization** — global and app-specific RBAC with fine-grained abilities
- **SSO** — configurable single sign-on across all FAST apps
- **Session management** — persistent sessions with remember me
- **Unified identity** — one user account works across all FAST applications
- **Multi-tenancy** — company/organization isolation with per-tenant roles

---

## Quick start

### 1. Logto setup

1. Go to [https://2uaw9x.logto.app](https://2uaw9x.logto.app)
2. Create an **API Resource**: name `FAST.Gate`, identifier `https://fast.gate.internal`
3. Create a **Machine-to-Machine app** named `FAST.Gate Service` — note the Client ID and Secret
4. Under the M2M app, register these redirect URIs:
   - `https://gate.fast.internal/auth/internal-callback` ← for direct login (Experience API)
   - Any SSO callback URIs for your FAST applications (e.g. `https://myapp.fast.internal/auth/callback`)

### 2. Configure

```bash
cp .env.example .env
# Fill in GATE_CLIENT_ID and GATE_CLIENT_SECRET from Logto
```

Key values in `appsettings.json` (already pre-filled for your tenant):
```json
{
  "Gate": {
    "Identity": {
      "Logto": {
        "IssuerUrl":           "https://2uaw9x.logto.app/oidc",
        "ApiBaseUrl":          "https://2uaw9x.logto.app",
        "Audience":            "https://fast.gate.internal",
        "ServiceClientId":     "paste-from-logto",
        "ServiceClientSecret": "set-via-env-var",
        "ServiceRedirectUri":  "https://gate.fast.internal/auth/internal-callback"
      }
    }
  }
}
```

### 3. Run

```bash
docker compose up
# or
dotnet run --project FAST.Gate
```

Hit `GET /health` — if it returns `healthy`, FAST.Gate is live.

---

## Endpoints

### `POST /auth/login`
Username/password login. Used by Blazor Server apps with their own login UI.

```json
Request:  { "loginName": "jdoe", "password": "...", "tenantId": "optional" }
Response: FastAuthResult
```

### `POST /auth/refresh`
Silent token refresh using a refresh token.

```json
Request:  { "refreshToken": "..." }
Response: FastAuthResult (tokenRefreshed: true)
```

### `GET /auth/validate`
Validates a Bearer token and returns the user profile.

```
Headers: Authorization: Bearer {token}
Response: FastAuthResult
```

### `POST /auth/logout`
Revokes tokens at the IdP.

```json
Request:  { "refreshToken": "optional" }
Headers:  Authorization: Bearer {token}
Response: { "loggedOut": true }
```

### `GET /auth/me`
Returns the current user's profile.

```
Headers:  Authorization: Bearer {token}
Response: UserProfile
```

### `GET /auth/sso/url?redirectUri=...`
Returns the SSO redirect URL. Redirect the user's browser here.

```json
Response: { "url": "https://2uaw9x.logto.app/oidc/auth?...", "state": "..." }
```

### `POST /auth/sso/exchange`
Exchanges the SSO auth code for a full FastAuthResult.

```json
Request:  { "code": "...", "redirectUri": "..." }
Response: FastAuthResult
```

### `GET /health`
Load balancer probe.

```json
{ "status": "healthy", "provider": "logto", "checks": [...] }
```

---

## FastAuthResult

Every auth endpoint returns this unified object:

```json
{
  "isSuccess": true,
  "tokenRefreshed": false,
  "fastToken": "",
  "idpAccessToken": "eyJ...",
  "idpRefreshToken": "...",
  "expiresAt": "2026-05-23T14:00:00Z",
  "user": {
    "id": "abc123",
    "loginName": "jdoe",
    "firstName": "John",
    "lastName": "Doe",
    "displayName": "John Doe",
    "email": "jdoe@company.com",
    "avatarUrl": "https://...",
    "tenantId": "tenant-xyz",
    "roles": ["erp.admin", "fast.flowchart.user"],
    "abilities": ["erp.accounts.delete"]
  },
  "error": null
}
```

---

## Error codes

| Code | HTTP | Meaning |
|------|------|---------|
| `gate_missing_credentials` | 400 | Username/password missing |
| `gate_invalid_credentials` | 401 | Wrong username or password |
| `gate_account_disabled` | 401 | Account locked or disabled |
| `gate_missing_token` | 401 | No Bearer token on request |
| `gate_invalid_token` | 401 | Bad token signature |
| `gate_token_expired` | 401 | Token past expiry |
| `gate_refresh_token_expired` | 401 | Refresh token expired — re-login required |
| `gate_role_denied` | 403 | Missing required role |
| `gate_ability_denied` | 403 | Missing required ability |
| `gate_tenant_not_found` | 404 | Tenant not found or inactive |
| `gate_tenant_access_denied` | 403 | User not in requested tenant |
| `gate_provider_unavailable` | 503 | Logto unreachable |
| `gate_provider_error` | 502 | Unexpected IdP error |
| `gate_internal_error` | 500 | Internal FAST.Gate error |

---

## Client SDK (`FAST.Gate.Client` NuGet)

Install in any FAST application:

```csharp
// Program.cs
builder.Services.AddFastGateClient(builder.Configuration);

// appsettings.json
{
  "FastGate": {
    "GateBaseUrl": "https://gate.fast.internal",
    "SsoEnabled": true,
    "SsoRedirectUri": "https://myapp.fast.internal/auth/callback"
  }
}
```

Inject `GateAuthClient` wherever you need auth:

```csharp
// Login (Blazor Server)
var result = await gateAuth.LoginAsync(username, password, tenantId);

// SSO (Blazor WASM)
var ssoUrl = await gateAuth.GetSsoLoginUrlAsync(tenantId);
// redirect user to ssoUrl ...
// in callback:
var result = await gateAuth.ExchangeSsoCodeAsync(code);

// Refresh
var result = await gateAuth.RefreshTokenAsync(refreshToken);

// Get profile
var profile = await gateAuth.GetUserProfileAsync(accessToken);
```

Blazor `[Authorize]` works automatically — `FastAuthStateProvider` is registered as `AuthenticationStateProvider`.

---

## Adding a new identity provider

1. Implement `IIdentityProvider` (7 methods)
2. Register it in `ServiceCollectionExtensions`
3. Add a case to `IdentityProviderFactory`
4. Set `Gate:Identity:Provider` to the new name

Zero changes to any FAST application.

---

## Using the FAST.Gate.Admin Application

[FASTGateOverview.webm](https://github.com/user-attachments/assets/9e0cf2ae-687f-4f1f-9f55-1f2fd2b441f0)

