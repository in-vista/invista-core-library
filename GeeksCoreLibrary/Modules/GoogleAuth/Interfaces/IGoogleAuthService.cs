using System.Security.Claims;
using System.Threading.Tasks;
using GeeksCoreLibrary.Modules.GoogleAuth.Models;

namespace GeeksCoreLibrary.Modules.GoogleAuth.Interfaces
{

    public interface IGoogleAuthService
    {
        /// <summary>
        /// Handles the Google authentication callback, validates the user,
        /// and logs them in or creates a new account if necessary.
        /// </summary>
        /// <param name="googlePrincipal">The authenticated Google claims principal.</param>
        /// <param name="settings">The settings/rules for authentication defined in easy_objects.</param>
        /// <param name="entityType">The entity type for creating the Customer or Employee</param>
        /// <param name="accountId">The parent account ID.</param>
        /// <returns>
        /// A <see cref="GoogleUserLoginResult"/> describing the login outcome,
        /// including user ID and generated cookie value if successful.
        /// </returns>
        Task<GoogleUserLoginResult> HandleGoogleCallbackAsync(
            ClaimsPrincipal googlePrincipal,
            GoogleEasyObjectsSettings settings,
            string entityType,
            ulong accountId = 0);
    }
}