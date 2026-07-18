using System.Security.Claims;

namespace IdentityBridge.Services
{
    /// <summary>
    /// Adapter for Azure Entra ID. Maps Entra's raw claim shape (oid / preferred_username)
    /// onto the normalized <see cref="IUserProfile"/>. A real fork would enrich from Graph
    /// API here; MVP reads claims only — no HTTP round-trip.
    /// </summary>
    public sealed class EntraUserProfileService : IUserProfileService
    {
        public string Idp => "entra";

        public IUserProfile GetProfile(ClaimsPrincipal user)
        {
            var id = user.FindFirst("oid")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? "";
            var name = user.FindFirst("preferred_username")?.Value
                       ?? user.Identity?.Name
                       ?? "";
            return new UserProfile(id, name, Idp);
        }
    }
}
