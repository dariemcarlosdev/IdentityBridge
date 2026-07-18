using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace IdentityBridge.Controllers
{
    /// <summary>
    /// Dev-only login simulator. Injects provider-shaped RAW claims (all values fake) into the
    /// matching per-provider cookie scheme so the Adapter/Factory/Strategy trio can be exercised
    /// end-to-end without any real IdP round-trip.
    /// </summary>
    public class AccountController : Controller
    {
        private static readonly Dictionary<string, (string Scheme, Claim[] Claims)> Providers = new()
        {
            ["entra"] = ("EntraCookie", new[]
            {
                new Claim("oid", "00000000-0000-0000-0000-0000000000e1"),
                new Claim("preferred_username", "alice@contoso.onmicrosoft.com"),
            }),
            ["auth0"] = ("Auth0Cookie", new[]
            {
                new Claim("sub", "auth0|000000000000000000000001"),
                new Claim("name", "Bob (Auth0)"),
            }),
            ["cognito"] = ("CognitoCookie", new[]
            {
                new Claim("sub", "00000000-0000-0000-0000-0000000000c0"),
                new Claim("cognito:username", "carol.cognito"),
            }),
        };

        // GET /Account/Simulate?idp=entra|auth0|cognito
        [HttpGet]
        public async Task<IActionResult> Simulate(string idp, [FromServices] IWebHostEnvironment env)
        {
            if (!env.IsDevelopment())
                return NotFound();

            if (idp is null || !Providers.TryGetValue(idp, out var provider))
                return BadRequest("idp must be one of: entra, auth0, cognito");

            var claims = new List<Claim>(provider.Claims) { new Claim("idp", idp) };
            var identity = new ClaimsIdentity(claims, provider.Scheme);
            await HttpContext.SignInAsync(provider.Scheme, new ClaimsPrincipal(identity));

            // Carry ?idp so the PolicyScheme selector routes the read to the same cookie scheme.
            return Redirect($"/Home/Profile?idp={idp}");
        }

        // POST /Account/Logout?idp=...
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout(string idp)
        {
            if (idp is not null && Providers.TryGetValue(idp, out var provider))
                await HttpContext.SignOutAsync(provider.Scheme);
            return Redirect("/");
        }
    }
}
