using System;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GeeksCoreLibrary.Components.Account.Interfaces;
using GeeksCoreLibrary.Core.DependencyInjection.Interfaces;
using GeeksCoreLibrary.Core.Interfaces;
using GeeksCoreLibrary.Core.Models;
using GeeksCoreLibrary.Modules.Databases.Interfaces;
using GeeksCoreLibrary.Modules.GoogleAuth.Interfaces;
using GeeksCoreLibrary.Modules.GoogleAuth.Models;

namespace GeeksCoreLibrary.Modules.GoogleAuth.Services
{
    public sealed class GoogleAuthService : IGoogleAuthService, IScopedService
    {
        private readonly IDatabaseConnection databaseConnection;
        private readonly IWiserItemsService wiserItemsService;
        private readonly IAccountsService accountsService;
        
        /// <summary>
        /// Creates a new instance of <see cref="GoogleAuthService"/>.
        /// </summary>
        public GoogleAuthService(
            IDatabaseConnection databaseConnection,
            IWiserItemsService wiserItemsService,
            IAccountsService accountsService
        )
        {
            this.databaseConnection = databaseConnection;
            this.wiserItemsService = wiserItemsService;
            this.accountsService = accountsService;
        }
        
        /// <inheritdoc />
        public async Task<GoogleUserLoginResult> HandleGoogleCallbackAsync(
            ClaimsPrincipal googlePrincipal,
            ulong accountId,
            string entityType = "WiserUser")
        {
            if (!GoogleAuthEntityTypes.IsAllowed(entityType))
                return new GoogleUserLoginResult(
                    IsSuccess: false,
                    FailureStatus: "entity_type_invalid",
                    AccountId: accountId,
                    UserId: 0,
                    IsNewUser: false,
                    CookieValue: null);
            
            if (accountId == 0)
                return new GoogleUserLoginResult(
                    IsSuccess: false,
                    FailureStatus: "account_id_invalid",
                    AccountId: accountId,
                    UserId: 0,
                    IsNewUser: false,
                    CookieValue: null);

            var googleUserInfo = ExtractGoogleUserInfo(googlePrincipal);

            if (string.IsNullOrWhiteSpace(googleUserInfo.EmailAddress))
            {
                return new GoogleUserLoginResult(
                    IsSuccess: false,
                    FailureStatus: "google_no_email",
                    AccountId: accountId,
                    UserId: 0,
                    IsNewUser: false,
                    CookieValue: null);
            }

            if (googleUserInfo.IsEmailVerified == false)
            {
                return new GoogleUserLoginResult(
                    IsSuccess: false,
                    FailureStatus: "google_email_not_verified",
                    AccountId: accountId,
                    UserId: 0,
                    IsNewUser: false,
                    CookieValue: null);
            }

            var (userId, isNewUser) = await GetOrCreateUserAsync(accountId, googleUserInfo, entityType);
            
            if(userId == 0)
                return new GoogleUserLoginResult(
                    IsSuccess: false,
                    FailureStatus: "linking_failed",
                    AccountId: accountId,
                    UserId: 0,
                    IsNewUser: false,
                    CookieValue: null);

            var cookieValue = await accountsService.GenerateNewCookieTokenAsync(
                userId, 0, 60, entityType, "");

            return new GoogleUserLoginResult(
                IsSuccess: true,
                FailureStatus: null,
                AccountId: accountId,
                UserId: userId,
                IsNewUser: isNewUser,
                CookieValue: cookieValue);
        }
        
        /// <summary>
        /// Extracts relevant user information from the authenticated Google claims principal.
        /// </summary>
        /// <param name="googlePrincipal">The authenticated Google claims principal.</param>
        /// <returns>
        /// A <see cref="GoogleUserInfo"/> containing normalized email, name,
        /// Google subject ID, and email verification status.
        /// </returns>
        private static GoogleUserInfo ExtractGoogleUserInfo(ClaimsPrincipal googlePrincipal)
        {
            var emailAddress =
                googlePrincipal.FindFirstValue(ClaimTypes.Email) ??
                googlePrincipal.FindFirstValue("email");

            var firstName =
                googlePrincipal.FindFirstValue(ClaimTypes.GivenName) ??
                googlePrincipal.FindFirstValue("given_name");

            var lastName =
                googlePrincipal.FindFirstValue(ClaimTypes.Surname) ??
                googlePrincipal.FindFirstValue("family_name");

            var googleSubjectIdentifier =
                googlePrincipal.FindFirstValue("sub") ??
                googlePrincipal.FindFirstValue(ClaimTypes.NameIdentifier);

            var emailVerifiedRaw = googlePrincipal.FindFirstValue("email_verified");
            bool? isEmailVerified =
                emailVerifiedRaw == null ? null :
                    string.Equals(emailVerifiedRaw, "true", StringComparison.OrdinalIgnoreCase);
            
            return new GoogleUserInfo(
                EmailAddress: (emailAddress ?? string.Empty).Trim(),
                FirstName: firstName?.Trim(),
                LastName: lastName?.Trim(),
                GoogleSubjectIdentifier: googleSubjectIdentifier?.Trim(),
                IsEmailVerified: isEmailVerified);
        }
        
        /// <summary>
        /// Retrieves an existing user based on Google subject ID or email,
        /// or creates a new user if none exists.
        /// </summary>
        /// <param name="accountId">The parent account ID.</param>
        /// <param name="googleUserInfo">The extracted Google user information.</param>
        /// <param name="entityType">The entity type (e.g. WiserUser or Customer).</param>
        /// <returns>
        /// A tuple containing the user ID and a flag indicating whether a new user was created.
        /// </returns>
        private async Task<(ulong userId, bool isNewUser)> GetOrCreateUserAsync(
            ulong accountId,
            GoogleUserInfo googleUserInfo,
            string entityType = "WiserUser")
        {
            // Prefer google subject id
            if (!string.IsNullOrWhiteSpace(googleUserInfo.GoogleSubjectIdentifier))
            {
                var userIdByGoogleId =
                    await FindUserIdByGoogleSubjectIdAsync(accountId, googleUserInfo.GoogleSubjectIdentifier, entityType);
                if (userIdByGoogleId.HasValue)
                    return (userIdByGoogleId.Value, false);
            }

            // Fallback email
            var userIdByEmail = await FindUserIdByEmailAsync(accountId, googleUserInfo.EmailAddress, entityType);
            if (userIdByEmail.HasValue)
            {
                if (string.IsNullOrWhiteSpace(googleUserInfo.GoogleSubjectIdentifier))
                    return (userIdByEmail.Value, false);
                
                var canAttach = await CanAttachGoogleSubjectIdAsync(userIdByEmail.Value, entityType, googleUserInfo.GoogleSubjectIdentifier);
                if (!canAttach)
                    return (0, false);

                await TryAttachGoogleSubjectIdAsync(userIdByEmail.Value, googleUserInfo.GoogleSubjectIdentifier, entityType);

                return (userIdByEmail.Value, false);
            }

            var createdUserId = await CreateUserAsync(accountId, googleUserInfo, entityType);
            return (createdUserId, true);
        }
        
        /// <summary>
        /// Retrieves the ID of a user with the specified email address
        /// within the given account.
        /// </summary>
        /// <param name="accountId">The parent account ID.</param>
        /// <param name="emailAddress">The email address to search for.</param>
        /// <param name="entityType">The entity type (e.g. WiserUser or Customer).</param>
        /// <returns>
        /// The user ID if found; otherwise null.
        /// </returns>
        private async Task<ulong?> FindUserIdByEmailAsync(ulong accountId, string emailAddress,
            string entityType = "WiserUser")
        {
            databaseConnection.ClearParameters();
            databaseConnection.AddParameter("accountId", accountId);
            databaseConnection.AddParameter("email", emailAddress);

            DataTable table;

            switch (entityType)
            {
                case GoogleAuthEntityTypes.WiserUser:
                    table = await databaseConnection.GetAsync(@$"
                SELECT c.id
                FROM wiser_item c
                JOIN wiser_itemdetail d
                  ON d.item_id = c.id
                 AND d.`key` = 'email_address'
                 AND d.`value` = ?email
                WHERE c.parent_item_id = ?accountId
                  AND c.published_environment > 0
                LIMIT 1",
                        skipCache: true);
                    break;
                case GoogleAuthEntityTypes.Customer:
                    table = await databaseConnection.GetAsync(@$"
                SELECT c.id
                FROM Customer_wiser_item c
                WHERE c.parent_item_id = ?accountId
                  AND c.email_address = ?email
                  AND c.published_environment > 0
                LIMIT 1",
                        skipCache: true);
                    break;
                default:
                    return null;
            }

            return table.Rows.Count == 0 ? null : Convert.ToUInt64(table.Rows[0]["id"]);
        }
        
        /// <summary>
        /// Retrieves the ID of a user linked to the specified Google subject identifier
        /// within the given account.
        /// </summary>
        /// <param name="accountId">The parent account ID.</param>
        /// <param name="googleSubjectIdentifier">The Google subject ID to search for.</param>
        /// <param name="entityType">The entity type (e.g. WiserUser or Customer).</param>
        /// <returns>
        /// The user ID if found; otherwise null.
        /// </returns>
        private async Task<ulong?> FindUserIdByGoogleSubjectIdAsync(
            ulong accountId,
            string googleSubjectIdentifier,
            string entityType)
        {
            databaseConnection.ClearParameters();
            databaseConnection.AddParameter("googleSubjectIdentifier", googleSubjectIdentifier);
            databaseConnection.AddParameter("accountId", accountId);

            DataTable table;

            switch (entityType)
            {
                case GoogleAuthEntityTypes.WiserUser:
                    table = await databaseConnection.GetAsync(@"
                SELECT c.id
                FROM wiser_item c
                JOIN wiser_itemdetail d
                  ON d.item_id = c.id
                 AND d.`key` = 'google_subject_id'
                 AND d.`value` = ?googleSubjectIdentifier
                WHERE c.parent_item_id = ?accountId
                  AND c.published_environment > 0
                LIMIT 1",
                        skipCache: true);
                    break;

                case GoogleAuthEntityTypes.Customer:
                    table = await databaseConnection.GetAsync(@"
                SELECT c.id
                FROM Customer_wiser_item c
                JOIN Customer_wiser_itemdetail d
                  ON d.item_id = c.id
                 AND d.`key` = 'google_subject_id'
                 AND d.`value` = ?googleSubjectIdentifier
                WHERE c.parent_item_id = ?accountId
                  AND c.published_environment > 0
                LIMIT 1",
                        skipCache: true);
                    break;
                default:
                    return null;
            }

            return table.Rows.Count == 0 ? null : Convert.ToUInt64(table.Rows[0]["id"]);
        }
        
        /// <summary>
        /// Attaches the specified Google subject identifier to an existing user.
        /// </summary>
        /// <param name="userId">The ID of the user to update.</param>
        /// <param name="googleSubjectIdentifier">The Google subject ID to store.</param>
        /// <param name="entityType">The entity type (e.g. WiserUser or Customer).</param>
        private async Task TryAttachGoogleSubjectIdAsync(ulong userId, string googleSubjectIdentifier,
            string entityType = "WiserUser")
        {
            var user = new WiserItemModel { Id = userId, EntityType = entityType };
            user.SetDetail("google_subject_id", googleSubjectIdentifier);

            await wiserItemsService.SaveAsync(
                user,
                parentId: 0,
                alwaysSaveValues: true,
                skipPermissionsCheck: true,
                parentEntityType: entityType);
        }
        
        /// <summary>
        /// Checks whether a Google subject identifier can be safely attached
        /// to the specified user without causing a mismatch.
        /// </summary>
        /// <param name="userId">The ID of the user to check.</param>
        /// <param name="entityType">The entity type (e.g. WiserUser or Customer).</param>
        /// <param name="newGoogleSubjectIdentifier">The Google subject ID to attach.</param>
        /// <returns>
        /// True if no subject ID exists or if it matches the existing one; otherwise false.
        /// </returns>
        private async Task<bool> CanAttachGoogleSubjectIdAsync(ulong userId, string entityType, string newGoogleSubjectIdentifier)
        {
            databaseConnection.ClearParameters();
            databaseConnection.AddParameter("userId", userId);

            DataTable table; 
            
            switch (entityType)
            {
                case GoogleAuthEntityTypes.WiserUser:
                    table = await databaseConnection.GetAsync(@"
                SELECT d.`value`
        FROM wiser_itemdetail d
        WHERE d.item_id = ?userId
          AND d.`key` = 'google_subject_id'
        LIMIT 1",
                        skipCache: true);
                    break;

                case GoogleAuthEntityTypes.Customer:
                    table = await databaseConnection.GetAsync(@"
                SELECT d.`value`
        FROM Customer_wiser_itemdetail d
        WHERE d.item_id = ?userId
          AND d.`key` = 'google_subject_id'
        LIMIT 1",
                        skipCache: true);
                    break;
                default:
                    return false;
            }
            
            if (table.Rows.Count == 0)
                return true;

            var existingValue = table.Rows[0][0].ToString() ?? string.Empty;

            return string.IsNullOrWhiteSpace(existingValue) || string.Equals(existingValue, newGoogleSubjectIdentifier, StringComparison.Ordinal);
        }
        
        /// <summary>
        /// Creates a new user based on Google authentication data
        /// and saves it under the specified account.
        /// </summary>
        /// <param name="accountId">The parent account ID.</param>
        /// <param name="googleUserInfo">The extracted Google user information.</param>
        /// <param name="entityType">
        /// The entity type to create (e.g. WiserUser or Customer).
        /// </param>
        /// <returns>The ID of the newly created user.</returns>
        private async Task<ulong> CreateUserAsync(ulong accountId, GoogleUserInfo googleUserInfo,
            string entityType = "WiserUser")
        {
            var user = new WiserItemModel { EntityType = entityType };

            var firstName = googleUserInfo.FirstName?.Trim() ?? string.Empty;
            var lastName = googleUserInfo.LastName?.Trim() ?? string.Empty;
            var fullName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

            switch (entityType)
            {
                case GoogleAuthEntityTypes.WiserUser:
                    if (!string.IsNullOrEmpty(fullName))
                        user.Title = fullName;
                    break;

                case GoogleAuthEntityTypes.Customer:
                    if (!string.IsNullOrEmpty(firstName))
                        user.SetDetail("first_name", firstName);

                    if (!string.IsNullOrEmpty(lastName))
                        user.SetDetail("last_name", lastName);

                    if (!string.IsNullOrEmpty(fullName))
                        user.SetDetail("full_name", fullName);
                    break;
            }

            user.SetDetail("email_address", googleUserInfo.EmailAddress);

            if (!string.IsNullOrWhiteSpace(googleUserInfo.GoogleSubjectIdentifier))
                user.SetDetail("google_subject_id", googleUserInfo.GoogleSubjectIdentifier);

            user.ParentItemId = accountId;

            user = await wiserItemsService.SaveAsync(
                user,
                parentId: accountId,
                alwaysSaveValues: true,
                skipPermissionsCheck: true,
                parentEntityType: "Account");

            return user.Id;
        }
    }
}