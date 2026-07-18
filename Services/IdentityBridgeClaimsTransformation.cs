using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace IdentityBridge.Services
{
    /// <summary>
    /// Backfills the normalized <c>idp</c> claim from raw provider claims when it is absent,
    /// so the factory can route regardless of which upstream signed the user in. Idempotent:
    /// runs on every request but adds the claim at most once.
    /// </summary>
    public sealed class IdentityBridgeClaimsTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
                return Task.FromResult(principal);

            if (principal.HasClaim(c => c.Type == "idp"))
                return Task.FromResult(principal);

            var idp = InferIdp(principal);
            if (idp is not null)
                identity.AddClaim(new Claim("idp", idp));

            return Task.FromResult(principal);
        }

        private static string? InferIdp(ClaimsPrincipal p)
        {
            if (p.HasClaim(c => c.Type == "oid") || p.HasClaim(c => c.Type == "preferred_username"))
                return "entra";
            if (p.HasClaim(c => c.Type == "cognito:username"))
                return "cognito";
            if (p.FindFirst("sub")?.Value?.StartsWith("auth0|", StringComparison.OrdinalIgnoreCase) == true)
                return "auth0";
            return null;
        }
    }
}
