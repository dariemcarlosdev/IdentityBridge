using System.Security.Claims;

namespace IdentityBridge.Services
{
    /// <summary>
    /// Adapter for Amazon Cognito. Maps Cognito's raw claim shape (cognito:username,
    /// cognito:groups) onto the normalized <see cref="IUserProfile"/>. A real fork would
    /// enrich from the Cognito Identity API here; MVP reads claims only — no HTTP round-trip.
    /// </summary>
    public sealed class CognitoUserProfileService : IUserProfileService
    {
        public string Idp => "cognito";

        public IUserProfile GetProfile(ClaimsPrincipal user)
        {
            var id = user.FindFirst("sub")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? "";
            var name = user.FindFirst("cognito:username")?.Value
                       ?? user.Identity?.Name
                       ?? "";
            return new UserProfile(id, name, Idp);
        }
    }
}
