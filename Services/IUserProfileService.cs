using System.Security.Claims;

namespace IdentityBridge.Services
{
    /// <summary>Normalized identity contract every provider adapter maps onto.</summary>
    public interface IUserProfile
    {
        string Id { get; }
        string DisplayName { get; }
        string Idp { get; }
    }

    /// <summary>
    /// Adapter contract: one per identity provider. Each implementation knows how to read
    /// its own provider's raw claim shape and normalize it. Callers depend on this, never
    /// on a concrete adapter (Dependency Inversion).
    /// </summary>
    public interface IUserProfileService
    {
        /// <summary>Normalized provider key this adapter serves: "entra" | "auth0" | "cognito".</summary>
        string Idp { get; }

        IUserProfile GetProfile(ClaimsPrincipal user);
    }

    public sealed record UserProfile(string Id, string DisplayName, string Idp) : IUserProfile;
}
