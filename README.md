# PolyIdentity Bridge-Gateway

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23_14.0-74%25-239120?logo=csharp&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Web_App-512BD4?logo=dotnet&logoColor=white)
![Azure Entra ID](https://img.shields.io/badge/Azure_Entra_ID-OIDC-0078D4?logo=microsoftazure&logoColor=white)
![Auth0](https://img.shields.io/badge/Auth0-OIDC-EB5424?logo=auth0&logoColor=white)
![Amazon Cognito](https://img.shields.io/badge/Amazon_Cognito-OIDC-FF9900?logo=amazonaws&logoColor=white)
![License](https://img.shields.io/github/license/dariemcarlosdev/IdentityBridge)
![Last Commit](https://img.shields.io/github/last-commit/dariemcarlosdev/IdentityBridge)
![MVP](https://img.shields.io/badge/status-MVP%20reference-blue)

**A production-ready MVP pattern for ASP.NET Core apps that authenticate users across multiple identity providers** — Azure Entra ID, Auth0 (by Okta), and Amazon Cognito — behind one normalized identity contract.
It takes the shape of a PolyAuth Gateway, but is implemented entirely in the app layer (not infra) and is meant to be forked and adapted per project. It demonstrates the Adapter + Factory + Strategy pattern trio, plus a claims transformation, to keep downstream code provider-agnostic while supporting multiple IdPs simultaneously.
**

**Solution Architected and Designed by**: Dariem C. Macias Mora. ( Sr. Software Engineer)

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
  shapes claims differently; this MVP's adapters read:
  - **Entra ID** → `oid`, `preferred_username`
  - **Auth0** → `sub` (`auth0|xxx`), `name`
  - **Cognito** → `sub`, `cognito:username`
- **Provider-specific services** — profile lookups (id, display name)
  resolved by normalized claim, not string comparisons on provider name.
  Role/group mapping is not implemented in this MVP — see
  [Not Included](#not-included--adapt-per-project).
- **Multi-instance safety** — Data Protection key-ring sharing wired in,
  required once the app scales past a single node (each provider's OIDC
  state/nonce cookie depends on it).

---

## Providers Covered in This MVP

| Provider | Protocol (production) | MVP adapter reads |
|---|---|---|
| Azure Entra ID | OIDC via `Microsoft.Identity.Web` | `oid`, `preferred_username` only — no Graph API call, no tenant logic |
| Auth0 (by Okta) | Generic OIDC | `sub`, `name` only — no namespaced custom claims |
| Amazon Cognito | OIDC via Hosted UI | `sub`, `cognito:username` only — no `cognito:groups` role mapping |

Each provider is wired as its **own named authentication scheme with its
own dedicated cookie scheme** — never sharing a cookie name, which is the
single most common cause of silent session bugs in multi-IdP setups.

> **Note:** the "Protocol (production)" column describes what each
> provider normally uses in production — this MVP does not perform a real
> OIDC handshake with any provider. `Program.cs` only wires the cookie
> schemes and the `PolicyScheme` dispatch selector. Sign-in is simulated
> via `AccountController.Simulate`, which injects provider-shaped raw
> claims directly into the matching cookie scheme. No `AddOpenIdConnect`
> or provider SDK is registered.

---

## Design Patterns Used

This solution combines three patterns, each solving a **distinct**
problem. None of them substitute for another — remove any one and a
specific part of the system breaks.

| Pattern | Problem solved | Key implementation |
|---|---|---|
| **Adapter** | Each IdP shapes identity data differently — normalize to one contract | `Services/` (6 files): `IUserProfileService`/`IUserProfile` contract + `EntraUserProfileService`/`Auth0UserProfileService`/`CognitoUserProfileService` implementations |
| **Factory** | Pick the right adapter at runtime, keep call sites provider-agnostic | `UserProfileServiceFactory.Resolve(ClaimsPrincipal)` reads the normalized `idp` claim, returns the matching adapter |
| **Strategy** | Dispatch each request to the correct already-registered auth scheme | `Program.cs`: `AddPolicyScheme` + `ForwardDefaultSelector` by `?idp=`, over 3 distinct cookie schemes (`ib.entra`, `ib.auth0`, `ib.cognito`) |

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

## Project Rules (for Contributors, Senior Engineers & Architects)

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
- Role/group mapping from IdP-specific claims (e.g. Cognito
  `cognito:groups`, Auth0 namespaced roles). The normalized `IUserProfile`
  contract has room for it, but no adapter in this MVP populates it —
  add a `Roles`/`Groups` member and map it per adapter if your fork needs
  authorization beyond identity.
- The reverse-proxy/YARP layer for infra-level routing, useful when
  segmentation is static enough to push to the edge instead of the app
  (e.g. `entra.yourapp.com`, `partners.yourapp.com` via Cognito,
  `auth0.yourapp.com`).

---



## Adapting This Pattern to Your Project

This repo is meant to be **read, understood, and ported** — not installed.
Below is the step-by-step guide for lifting the Adapter + Factory + Strategy
trio into an existing ASP.NET Core app. Follow it top to bottom; each step
builds on the previous one.

### Before you start — is this pattern the right fit?

Use it when **all** of these are true:

- Your app must authenticate users against **two or more OIDC providers**
  at the same time (not just migrate from one to another).
- Provider selection depends at least partly on **runtime data** (a claim,
  a tenant, a session) — not purely on static subdomain/path routing you
  could push to a reverse proxy.
- Downstream code (controllers, services) needs **one identity shape**,
  regardless of who authenticated the user.

If you only ever have one IdP, or your segmentation is fully static, this is
over-engineering — stop here and wire a single scheme (or edge routing).

### Step-by-step port

1. **Define your normalized contract first.** Copy `IUserProfileService` /
   `IUserProfile` and trim them to the fields *your* app actually consumes
   (id, display name, source `idp`, roles/groups — whatever your
   authorization layer needs). This contract is the whole point; get it
   right before writing a single adapter. Keep it minimal (Interface
   Segregation) — do not expose raw claims or tokens through it.

2. **Write one Adapter per provider.** For each IdP, create a class
   implementing `IUserProfileService` that:
   - sets its own `Idp` key string (`"entra"`, `"auth0"`, `"okta"`, …) —
     the adapter owns this key, nothing else hardcodes it;
   - wraps that provider's upstream API (Graph API, Auth0 Management API,
     Cognito Identity API, etc.) and maps its provider-specific claims/data
     onto your `IUserProfile`.
   Use `EntraUserProfileService` / `Auth0UserProfileService` /
   `CognitoUserProfileService` as templates.

3. **Register each provider as its own named auth scheme with its own
   dedicated cookie scheme.** This is the load-bearing invariant — **never
   share a cookie name across providers.** In this repo the cookie schemes
   are `ib.entra`, `ib.auth0`, `ib.cognito`. Pick a prefix for your app and
   keep them distinct. Shared cookies are the single most common cause of
   silent multi-IdP session bugs.

4. **Add the claims transformation.** Copy
   `IdentityBridgeClaimsTransformation` and adapt it to stamp the
   normalized `idp` claim onto each principal after sign-in. This claim is
   the routing key the Factory reads later — without it, `Resolve` has
   nothing to look up.

5. **Wire the Strategy (request dispatch).** In `Program.cs`, use
   `AddPolicyScheme` + `ForwardDefaultSelector` to dispatch each incoming
   request to the correct already-registered scheme. Replace this repo's
   `?idp=` selector with *your* real signal — path, subdomain, tenant, or
   existing session cookie. The selector only *dispatches*; it must not
   construct anything.

6. **Register adapters in DI as `IUserProfileService`.** Register every
   adapter against the same interface:
   ```csharp
   builder.Services.AddScoped<IUserProfileService, EntraUserProfileService>();
   builder.Services.AddScoped<IUserProfileService, Auth0UserProfileService>();
   // ...one line per provider
   builder.Services.AddScoped<IUserProfileServiceFactory, UserProfileServiceFactory>();
   ```
   The factory receives all of them as `IEnumerable<IUserProfileService>`
   and builds its `idp → adapter` dictionary once at construction.

7. **Consume only through the abstractions.** Controllers and services call
   `factory.Resolve(User)` and depend on `IUserProfileService` /
   `IUserProfileServiceFactory` **only** — never on a concrete adapter
   class. This is what makes adding/swapping/mocking providers free.

8. **Enable Data Protection key-ring sharing before you scale past one
   node.** Each provider's OIDC state/nonce cookie depends on it. Persist
   keys to a shared store (Redis, blob, shared filesystem) — otherwise
   logins break intermittently behind a load balancer.

9. **Replace every fake config value.** Swap all placeholders
   (`REPLACE_WITH_TENANT_ID`, authority URLs, client IDs/secrets) for your
   real values, sourced from your secrets store — never commit real
   secrets.

### Adding a 4th provider later

Once ported, extending is deliberately cheap: write one new adapter, add
one DI registration line, add its scheme + cookie in `Program.cs`, and
teach the selector its routing signal. You touch **zero** existing factory
or call-site code — the dictionary picks the new adapter up on next
startup. That auto-pickup is the Open/Closed principle paying for itself.

### Porting checklist

- [ ] Normalized `IUserProfile` / `IUserProfileService` contract trimmed to your app's needs
- [ ] One Adapter per provider, each with a unique `Idp` key
- [ ] Each provider has its own named scheme **and** its own dedicated cookie scheme
- [ ] Claims transformation stamps the normalized `idp` claim
- [ ] Policy scheme + `ForwardDefaultSelector` dispatches on your real routing signal
- [ ] All adapters registered in DI as `IUserProfileService`; factory registered
- [ ] Controllers/services depend only on the abstractions
- [ ] Data Protection key-ring shared (before multi-node deploy)
- [ ] All fake config replaced with real values from a secrets store

---

## Status

MVP reference implementation — intended to be forked and adapted per
project, not consumed as a NuGet package.
