# IdentityBridge

**A production-ready MVP pattern for ASP.NET Core apps that authenticate users across multiple identity providers — Azure Entra ID, Auth0 (by Okta), and Amazon Cognito — behind one normalized identity contract.**

**Solution Architected and Designed by**: Dariem C. Macias Mora. ( Sr. Software Engineer, AI-Driven Senior Engineer )

---


## The Challenge / Trade-off

Real-world apps rarely settle on a single IdP forever. A common scenario:
an app inherits a legacy authentication provider, migrates part of its
user base to a modern IdP (or has to support enterprise SSO, partner
access, and consumer login simultaneously), and ends up needing **more
than one identity provider active at once** — selected dynamically per
user type, tenant, or app segment.

The trade-off is always the same question: **solve it in the app code, or
push it to infrastructure (reverse proxy/load balancer)?**

- Solving it purely in infra (routing rules) works only when segmentation
  is static and known upfront (subdomain, path) — it breaks down the moment
  provider selection depends on runtime data (a claim, a DB lookup, a
  feature flag).
- Solving it purely in app code without a clean pattern leads to
  `if (isEntraUser) ... else if (isAuth0User) ...` scattered across
  controllers, services, and views — a maintenance and security liability.

**IdentityBridge is the app-layer half of that answer** — a pattern that
scales from 2 providers to N without the codebase degrading into
conditional spaghetti, meant to pair with (not replace) edge-level routing
where it makes sense.

---

## What This Solves

- **Provider selection** — routes each request to the correct authentication
  scheme (Entra ID / Auth0 / Cognito) based on path, subdomain, tenant, or
  existing session — without per-request DB calls.
- **Claims normalization** — one consistent identity shape for the rest of
  the app, regardless of which provider authenticated the user. Each IdP
  shapes claims differently:
  - **Entra ID** → `oid`, `tid`, `preferred_username`
  - **Auth0** → `sub` (`auth0|xxx` / `google-oauth2|xxx`), namespaced custom
    claims (`https://yourapp.com/roles`)
  - **Cognito** → `cognito:username`, `cognito:groups`, `token_use`
- **Provider-specific services** — profile lookups, role/group mapping,
  etc., resolved by normalized claim, not string comparisons on provider name.
- **Multi-instance safety** — Data Protection key-ring sharing wired in,
  required once the app scales past a single node (each provider's OIDC
  state/nonce cookie depends on it).

---

## Providers Covered in This MVP

| Provider | Protocol | Notes |
|---|---|---|
| Azure Entra ID | OIDC via `Microsoft.Identity.Web` | Multi-tenant aware, Graph API profile stub |
| Auth0 (by Okta) | Generic OIDC | Universal Login redirect flow, custom claim namespace handling |
| Amazon Cognito | OIDC via Hosted UI | User Pool + App Client config, `cognito:groups` → app roles mapping |

Each provider is wired as its **own named authentication scheme with its
own dedicated cookie scheme** — never sharing a cookie name, which is the
single most common cause of silent session bugs in multi-IdP setups.

---

## Design Patterns Used

This solution combines three patterns, each solving a **distinct**
problem. None of them substitute for another — remove any one and a
specific part of the system breaks.

| Pattern | Problem solved | Key implementation |
|---|---|---|
| **Adapter** | Each IdP shapes identity data differently — normalize to one contract | `Services/` (6 files): `IUserProfileService`/`IUserProfile` contract + `EntraUserProfileService`/`Auth0UserProfileService`/`CognitoUserProfileService` normalizing raw claims |
| **Factory** | Pick the right adapter at runtime, keep call sites provider-agnostic | `UserProfileServiceFactory.Resolve(ClaimsPrincipal)` reads the normalized `idp` claim, returns the matching adapter |
| **Strategy** | Dispatch each request to the correct already-registered auth scheme | `Program.cs`: `AddPolicyScheme` + `ForwardDefaultSelector` by `?idp=`, over 3 distinct cookie schemes (`ib.entra`/`ib.auth0`/`ib.cognito`) |

### 1. Adapter — *the defining pattern of this solution*

**Problem it solves:** Entra ID, Auth0, and Cognito each shape identity
data completely differently — different claim names, different token
structures, different management APIs for profile/role lookups. Without
a unifying layer, every controller and service downstream would need to
know which provider it's dealing with.

**Where:** `EntraUserProfileService`, `Auth0UserProfileService`,
`CognitoUserProfileService` — each wraps a fundamentally different
upstream API (Graph API, Auth0 Management API, Cognito Identity API) and
adapts it to one shared `IUserProfile` interface.

**Why it's the defining pattern:** if every other pattern in this list
were deleted and replaced with plain `if/else`, the Adapter layer alone
would still deliver the core value — one consistent identity contract for
the rest of the app to depend on. Everything else exists to keep *access*
to these adapters clean.

### 2. Factory (Simple Factory) — selects the right adapter

**Problem it solves:** something has to decide, at runtime, *which*
adapter instance to hand back to the caller — without the caller needing
to know or care how that decision is made.

**Where:** `IUserProfileServiceFactory.Resolve(ClaimsPrincipal user)` —
inspects the normalized `idp` claim and returns the matching
`IUserProfileService` implementation.

**Why:** keeps call sites (`controller.Resolve(User)`) completely
provider-agnostic, and satisfies Open/Closed — adding a 4th provider means
adding one new adapter, not touching the factory or existing call
sites. Note: this is a parameterized Simple Factory, not GoF's strict
Factory Method (which requires subclass-driven virtual construction) —
worth knowing the distinction, though in practice most teams use the term
"Factory" for both.

**How it works (mechanics) — read this if you're forking:**

The factory is *registration-driven*, not a `switch`. There is no
`if (idp == "entra")` chain anywhere. The flow:

1. **Every adapter self-declares its key.** Each `IUserProfileService`
   exposes an `Idp` string (`"entra"` / `"auth0"` / `"cognito"`). That key
   is the adapter's own responsibility — the factory never hardcodes it.
2. **DI hands the factory all of them at once.** All three adapters are
   registered as `IUserProfileService`, so the constructor receives them as
   `IEnumerable<IUserProfileService>` (ASP.NET injects *every* registration
   of an interface into an `IEnumerable<T>` parameter automatically).
3. **The constructor builds a lookup dictionary once:**
   ```csharp
   _byIdp = services.ToDictionary(s => s.Idp, StringComparer.OrdinalIgnoreCase);
   ```
   `idp` → adapter, case-insensitive. Built once at construction, not per request.
4. **`Resolve(user)` is a dictionary lookup, not a branch:**
   ```csharp
   var idp = user.FindFirst("idp")?.Value;          // normalized routing key
   if (string.IsNullOrEmpty(idp)) throw ...;         // no key on the principal
   if (!_byIdp.TryGetValue(idp, out var service)) throw ...;  // no adapter for that key
   return service;
   ```
5. **Two distinct failure modes, two distinct meanings:**
   - *Missing `idp` claim* → the caller (`HomeController.Profile`) guards
     this and returns **400** (bad request — the principal is malformed).
   - *No adapter registered for a present `idp`* → this is a real
     **misconfiguration**, so it surfaces as **500** (the throw is correct
     here — fail loud, don't silently pick a default).

**To add a provider in your fork:** write one new class implementing
`IUserProfileService` (set its `Idp`, map that provider's raw claims to
`IUserProfile`), then register it in DI as `IUserProfileService`. The
dictionary picks it up automatically on next startup — you touch **zero**
existing code in the factory or any call site. That auto-pickup is the
Open/Closed principle paying for itself.

### 3. Strategy — selects the right authentication handler

**Problem it solves:** an incoming request must be authenticated by
whichever scheme actually matches it (Entra cookie vs Auth0 cookie vs
Cognito cookie) — the correct *behavior* to run is chosen at runtime,
not hardcoded.

**Where:** `AddPolicyScheme` + `ForwardDefaultSelector` in `Program.cs`.
Each registered scheme (Entra/Auth0/Cognito) is an interchangeable
authentication strategy; the selector predicate picks which one runs for
the current request based on path/subdomain/existing session.

**Why this is Strategy, not Factory:** it doesn't construct anything —
all three handlers are already registered in DI at startup. It only
*dispatches* to an existing, interchangeable behavior based on context.
(Earlier drafts of this doc mislabeled this step "the factory pattern" —
that was imprecise. Corrected here after a second, stricter pass against
GoF definitions.)

### Supporting principle: Dependency Inversion (not a GoF pattern, but load-bearing)

Controllers and services depend only on `IUserProfileService` and
`IUserProfileServiceFactory` — never on concrete adapter classes like `EntraUserProfileService`
etc. This is what makes the Adapter + Factory combination actually pay
off: swapping, mocking, or adding providers never touches consuming code.

---

## SOLID Principles Applied

- **Single Responsibility** — each `IUserProfileService` implementation
  only knows how to talk to *its* provider. The factory only knows how to
  pick one. The claims transformer only normalizes claims. No class does
  more than one job.
- **Open/Closed** — adding a fourth IdP means adding a new scheme
  registration + a new `IUserProfileService` implementation. Existing
  code (controllers, the factory's dictionary lookup, claims transformer)
  is not modified at all — the new adapter is picked up by DI automatically.
- **Liskov Substitution** — any `IUserProfileService` implementation can
  be swapped in for another without breaking callers, since all callers
  depend only on the `IUserProfileService` contract, never a concrete type.
- **Interface Segregation** — `IUserProfile` and `IUserProfileService`
  expose only what consumers actually need (id, display name, source idp)
  — not the full raw claims/token payload from each provider.
- **Dependency Inversion** — controllers and services depend on
  `IUserProfileServiceFactory` and `IUserProfileService` abstractions,
  never on `EntraUserProfileService` or `Auth0UserProfileService`
  concrete classes directly.

---

## Project Rules (for contributors & AI agents if you are a AI-Driven Senior Engineer)

- **All configuration values are fake, on purpose.** Every ClientId,
  TenantId, Authority URL, or secret in this repo is a placeholder
  (e.g. `REPLACE_WITH_TENANT_ID`). None of them work against a real IdP.
  Forks must supply their own — never assume a value here is usable as-is.
- **No speculative complexity.** No views, no UI, no real IdP round-trip.
  This repo proves the Adapter/Factory/Strategy pattern trio — nothing
  beyond that gets added. If a change isn't needed to demonstrate the
  pattern, it doesn't belong here.
- **Development follows Specification Driven Development (SDD).**
  `[FRAMEWORK/TOOL NAME — TBD]`. Every change is implemented against an
  approved spec artifact before code is written. See `CLAUDE.md` for the
  full agent-facing rule set.

---

## Project Structure

Flat single-project layout at the repo root (`IdentityBridge.slnx`, new
XML solution format):

```
IdentityBridge/
├── Controllers/
│   ├── AccountController.cs
│   └── HomeController.cs
├── Services/
│   ├── IdentityBridgeClaimsTransformation.cs
│   ├── IUserProfileService.cs
│   ├── UserProfileServiceFactory.cs
│   ├── EntraUserProfileService.cs
│   ├── Auth0UserProfileService.cs
│   └── CognitoUserProfileService.cs
├── Models/
├── Properties/
├── Views/
├── wwwroot/
├── appsettings.json
├── appsettings.Development.json
├── Program.cs
├── IdentityBridge.csproj
├── IdentityBridge.slnx
└── README.md
```

No dedicated tests project yet — `ClaimsTransformationTests` is the first
one to add when tests land.

The `Views/` folder is a stock-template leftover, not part of the demo.
There is deliberately no UI: with no real IdP to complete a login
round-trip against, a UI has zero demo value. `/Home/Profile` just
returns the normalized claim shape as JSON, which is the actual thing
this repo proves.

---

## Not Included / Adapt Per Project

- Production secrets management (Key Vault / AWS Secrets Manager / Auth0
  Vault integration).
- MFA / step-up auth policies per provider.
- The reverse-proxy/YARP layer for infra-level routing, useful when
  segmentation is static enough to push to the edge instead of the app
  (e.g. `entra.yourapp.com`, `partners.yourapp.com` via Cognito,
  `auth0.yourapp.com`).

---



## Status

MVP reference implementation — intended to be forked and adapted per
project, not consumed as a NuGet package.
