# Plan — Wire IdentityBridge auth trio (MVP, lean runnable)

> Status: **Implemented** (awaiting manual verify) · Last synced: 2026-07-18

## Context
Every `Services/*.cs` is an empty stub (`public class X {}`), `Program.cs` is the stock
MVC template with zero auth, and `IdentityBridge.csproj` pulls no auth packages. README is
the behavior spec: prove the **Adapter + Factory + Strategy** trio behind one normalized
identity contract, deliverable = `/Home/Profile` returning normalized claims as JSON. Config
stays fake, no real IdP round-trip.

Chosen scope (user-confirmed): **lean runnable** — per-provider cookie schemes + a
PolicyScheme/`ForwardDefaultSelector` (Strategy) + a dev-only "simulate login" that injects
provider-shaped raw claims, so `/Home/Profile` actually returns JSON. **Zero new NuGet
packages** (cookie auth built into framework). Test project **deferred**.

Load-bearing invariant: each provider gets its own named auth scheme AND its own dedicated
cookie name (`ib.entra` / `ib.auth0` / `ib.cognito`) — never shared. Controllers/services
depend only on `IUserProfileService` + `IUserProfileServiceFactory`, never concrete adapters.

## Design (normalized contract)
```csharp
public interface IUserProfile { string Id { get; } string DisplayName { get; } string Idp { get; } }
public interface IUserProfileService {
    string Idp { get; }                          // "entra" | "auth0" | "cognito"
    IUserProfile GetProfile(ClaimsPrincipal user);
}
public interface IUserProfileServiceFactory { IUserProfileService Resolve(ClaimsPrincipal user); }
```
`idp` normalized claim is the Factory routing key. Set during simulate-login, backfilled by
the claims transformer from raw provider claims if absent.

## Task checklist

### Services/ (rewrite all 6 stubs) — ✅ DONE
- [x] `IUserProfileService.cs` — `IUserProfile` + `IUserProfileService` interfaces + small `UserProfile` record impl.
- [x] `EntraUserProfileService.cs` — Adapter, `Idp="entra"`, maps raw `oid`/`preferred_username`. Graph call stubbed (claims only, no HTTP).
- [x] `Auth0UserProfileService.cs` — Adapter, `Idp="auth0"`, maps raw `sub` (`auth0|xxx`)/`name`. Mgmt API stubbed.
- [x] `CognitoUserProfileService.cs` — Adapter, `Idp="cognito"`, maps raw `cognito:username`, `sub` fallback. Identity API stubbed.
- [x] `UserProfileServiceFactory.cs` — `IUserProfileServiceFactory` impl. Inject `IEnumerable<IUserProfileService>`, read `idp` claim, return matching adapter (throw if none).
- [x] `IdentityBridgeClaimsTransformation.cs` — `: IClaimsTransformation`. If `idp` missing, infer from present raw provider claims + add it. Idempotent.

### Program.cs (Strategy + schemes + DI) — ✅ DONE
- [x] `AddAuthentication("IdentityBridge")` + `AddPolicyScheme("IdentityBridge", ...)` with `ForwardDefaultSelector` picking scheme by `?idp=` query (Strategy — dispatch only).
- [x] `AddCookie("EntraCookie", o=>o.Cookie.Name="ib.entra")` + `Auth0Cookie`/`ib.auth0` + `CognitoCookie`/`ib.cognito` (distinct names = invariant).
- [x] `AddAuthorization()`; register `IClaimsTransformation`→transformer, 3 adapters as `IUserProfileService`, `IUserProfileServiceFactory`.
- [x] Insert `app.UseAuthentication();` before existing `app.UseAuthorization();`.
- [x] DataProtection multi-node note: one commented `// PersistKeysToFileSystem(...)` + "adapt per project" (not wired — single-node MVP).

### Controllers/ — ✅ DONE
- [x] `AccountController.cs` (rewrite) — `GET /Account/Simulate?idp=...` dev-only (`env.IsDevelopment()` guard): build `ClaimsIdentity` with that provider's raw claim shape + normalized `idp`, `SignInAsync` into matching cookie scheme, redirect `/Home/Profile?idp=...`. `POST /Account/Logout?idp=` (`[ValidateAntiForgeryToken]`) → `SignOutAsync`.
- [x] `HomeController.cs` — added `[Authorize] Profile()` → `factory.Resolve(User).GetProfile(User)` as `Json(...)`. Injects `IUserProfileServiceFactory` via ctor. Index/Privacy/Error untouched.

## Out of scope
No OIDC packages, no real IdP handlers, no test project, no UI/Views, no Graph/Mgmt/Cognito
HTTP calls, no `Models/`/`wwwroot/` changes, no `.csproj` package edits.

## Verification
1. `dotnet build` — succeeds, zero new packages.
2. `dotnet run` → `/Account/Simulate?idp=auth0` → redirect `/Home/Profile`, JSON `{ id, displayName, idp:"auth0" }` normalized from Auth0 raw claims.
3. Repeat `?idp=entra` / `?idp=cognito` — each own normalized shape, each own cookie (`ib.*` in devtools).
4. `/Home/Profile` unauthenticated → challenge (default-deny holds).
