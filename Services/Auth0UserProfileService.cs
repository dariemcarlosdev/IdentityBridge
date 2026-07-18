using System.Security.Claims;

namespace IdentityBridge.Services
{
    /// <summary>
    /// Adapter for Auth0. Maps Auth0's raw claim shape (sub = "auth0|xxx", name) onto the
    /// normalized <see cref="IUserProfile"/>. A real fork would enrich from the Auth0
    /// Management API here; MVP reads claims only — no HTTP round-trip.
    /// </summary>
    public sealed class Auth0UserProfileService : IUserProfileService
    {
        public string Idp => "auth0";

        public IUserProfile GetProfile(ClaimsPrincipal user)
        {
            var id = user.FindFirst("sub")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? "";
            var name = user.FindFirst("name")?.Value
                       ?? user.Identity?.Name
                       ?? "";
            return new UserProfile(id, name, Idp);
        }
    }
}
