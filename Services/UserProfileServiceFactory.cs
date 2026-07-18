using System.Security.Claims;

namespace IdentityBridge.Services
{
    /// <summary>Simple Factory: resolves the adapter matching the normalized <c>idp</c> claim.</summary>
    public interface IUserProfileServiceFactory
    {
        /// <summary>
        /// Resolves the adapter matching the normalized <c>idp</c> claim. Throws if no match is found.
        /// </summary>
        /// <param name="user">The <see cref="ClaimsPrincipal"/> representing the authenticated user.</param>
        /// <returns>The <see cref="IUserProfileService"/> corresponding to the user's identity provider.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no <c>idp</c> claim is present or no matching adapter is registered.</exception>
        IUserProfileService Resolve(ClaimsPrincipal user);
    }

    /// <summary>
    /// Inspects the normalized <c>idp</c> claim and returns the matching adapter. Adding a
    /// provider = one new adapter registered in DI; this factory and all call sites stay
    /// provider-agnostic (they depend only on the abstraction).
    /// </summary>
    public sealed class UserProfileServiceFactory : IUserProfileServiceFactory
    {
        private readonly IReadOnlyDictionary<string, IUserProfileService> _byIdp;

        public UserProfileServiceFactory(IEnumerable<IUserProfileService> services)
        {
            _byIdp = services.ToDictionary(s => s.Idp, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the adapter matching the normalized <c>idp</c> claim. Throws if no match is found.
        /// </summary>
        /// <param name="user">The <see cref="ClaimsPrincipal"/> representing the authenticated user.</param>
        /// <returns>The <see cref="IUserProfileService"/> corresponding to the user's identity provider.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no <c>idp</c> claim is present or no matching adapter is registered.</exception>
        public IUserProfileService Resolve(ClaimsPrincipal user)
        {
            var idp = user.FindFirst("idp")?.Value;
            if (string.IsNullOrEmpty(idp))
                throw new InvalidOperationException("No 'idp' claim present; cannot resolve profile adapter.");
            if (!_byIdp.TryGetValue(idp, out var service))
                throw new InvalidOperationException($"No adapter registered for idp '{idp}'.");
            return service;
        }
    }
}
