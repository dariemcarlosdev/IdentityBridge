using IdentityBridge.Models;
using IdentityBridge.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace IdentityBridge.Controllers
{
    /// <summary>
    /// Controller for the home page and user profile. Uses the IUserProfileServiceFactory to obtain a provider-agnostic user profile.
    /// </summary>
    public class HomeController : Controller
    {
        // Deliverable: factory injected via DI, resolves the correct adapter based on the normalized idp claim.
        private readonly IUserProfileServiceFactory _profileFactory;

        public HomeController(IUserProfileServiceFactory profileFactory)
        {
            _profileFactory = profileFactory;
        }

        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the user profile in a provider-agnostic format. The profile is resolved using the IUserProfileServiceFactory, which selects the appropriate adapter based on the normalized idp claim. 
        /// </summary>
        /// <returns>A JSON representation of the user profile.</returns>
        [Authorize]
        public IActionResult Profile()
        {
            // Deliverable: resolve the profile adapter based on the normalized idp claim, and return the profile in a provider-agnostic format.
            //savety guard: if the idp claim is missing, return a clean 400 Bad Request with a message indicating that the profile adapter cannot be resolved, not a 500 Internal Server Error. otherwise, return the profile in a provider-agnostic format with the id, displayName, and idp properties.
            if (User.FindFirst("idp")?.Value is not { Length: > 0 })
                return BadRequest("No 'idp' claim on principal; cannot resolve profile adapter.");

            var profile = _profileFactory.Resolve(User).GetProfile(User);
            return Json(new { id = profile.Id, displayName = profile.DisplayName, idp = profile.Idp });
        }

        /// <summary>
        /// GET: /Home/Privacy
        /// </summary>
        /// <returns>The privacy view.</returns>
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
