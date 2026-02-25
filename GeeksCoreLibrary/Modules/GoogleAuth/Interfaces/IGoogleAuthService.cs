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
        /// <param name="accountId">The parent account ID.</param>
        /// <param name="entityType">The entity type (e.g. WiserUser or Customer).</param>
        /// <returns>
        /// A <see cref="GoogleUserLoginResult"/> describing the login outcome,
        /// including user ID and generated cookie value if successful.
        /// </returns>
        Task<GoogleUserLoginResult> HandleGoogleCallbackAsync(
            ClaimsPrincipal googlePrincipal,
            ulong accountId,
            string entityType = "WiserUser");
    }
}