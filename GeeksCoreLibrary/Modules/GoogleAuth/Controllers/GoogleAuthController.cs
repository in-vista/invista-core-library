using System;
using System.Threading.Tasks;
using GeeksCoreLibrary.Core.Helpers;
using GeeksCoreLibrary.Modules.Databases.Interfaces;
using GeeksCoreLibrary.Modules.GoogleAuth.Interfaces;
using GeeksCoreLibrary.Modules.GoogleAuth.Models;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace GeeksCoreLibrary.Modules.GoogleAuth.Controllers
{
    [ApiController]
    public sealed class GoogleAuthController : ControllerBase
    {
        private readonly IGoogleAuthService googleAuthService;
        private readonly IDatabaseConnection databaseConnection;

        public GoogleAuthController(
            IGoogleAuthService googleAuthService,
            IDatabaseConnection databaseConnection)
        {
            this.databaseConnection = databaseConnection;
            this.googleAuthService = googleAuthService;
        }
        
        [HttpGet("/google-login.gcl")]
        public IActionResult SignInWithGoogle(
            [FromQuery] string returnUrl = "/",
            [FromQuery] string loginUrl = "/account.html",
            [FromQuery] bool doRedirect = true,
            [FromQuery] string entityType = "WiserUser",
            [FromQuery] ulong accountId = 0)
        {
            returnUrl = NormalizeLocalPath(returnUrl, "/");
            loginUrl = NormalizeLocalPath(loginUrl, "/account.html");
            
            if (!GoogleAuthEntityTypes.IsAllowed(entityType))
                BadRequest(new { status = "invalid_entity_type" });
            
            if (string.IsNullOrWhiteSpace(returnUrl))
                returnUrl = "/";

            // This is where Google will redirect back to after login.
            var callbackUrl = Url.ActionLink(
                action: nameof(GoogleCallback),
                controller: "GoogleAuth",
                values: new { returnUrl, loginUrl, doRedirect, entityType, accountId })!;

            var authenticationProperties = new AuthenticationProperties
            {
                RedirectUri = callbackUrl
            };

            return Challenge(authenticationProperties, GoogleDefaults.AuthenticationScheme);
        }
        
        [HttpGet("/google-callback.gcl")]
        public async Task<IActionResult> GoogleCallback(
            [FromQuery] string returnUrl = "/",
            [FromQuery] string loginUrl = "/account.html",
            [FromQuery] bool doRedirect = true,
            [FromQuery] string entityType = "WiserUser",
            [FromQuery] ulong accountId = 0)
        {
            returnUrl = NormalizeLocalPath(returnUrl, "/");
            loginUrl = NormalizeLocalPath(loginUrl, "/account.html");

            if (!GoogleAuthEntityTypes.IsAllowed(entityType))
                BadRequest(new { status = "invalid_entity_type" });
            
            var authenticateResult =
                await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                return doRedirect
                    ? Redirect($"{loginUrl}?status=google_failed")
                    : Unauthorized(new { status = "google_failed" });
            }
            
            if(accountId == 0)
                accountId = await ResolveAccountIdAsync();

            var result =
                await googleAuthService.HandleGoogleCallbackAsync(authenticateResult.Principal, accountId, entityType);

            if (!result.IsSuccess)
            {
                return doRedirect
                    ? Redirect($"{loginUrl}?status={result.FailureStatus}")
                    : BadRequest(new { status = result.FailureStatus });
            }

            // Write http cookie
            HttpContextHelpers.WriteCookie(
                HttpContext,
                "gcl_user_cookie",
                result.CookieValue!,
                DateTimeOffset.Now.AddDays(60),
                httpOnly: true,
                isEssential: true,
                secure: true
                );

            if (doRedirect)
                return Redirect(returnUrl);

            return Ok(new
            {
                status = "ok",
                accountId = result.AccountId,
                customerId = result.UserId,
                isNewUser = result.IsNewUser
            });
        }

        private async Task<ulong> ResolveAccountIdAsync()
        {
            databaseConnection.ClearParameters();
            var accountTable = await databaseConnection.GetAsync(
                @$"SELECT GetAccountIdBasedOnPrefix('{HttpContext.Request.GetDisplayUrl()}')",
                skipCache: true);

            return Convert.ToUInt64(accountTable.Rows[0][0]);
        }
        
        private static string NormalizeLocalPath([CanBeNull] string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("/", StringComparison.Ordinal))
                return fallback;

            // block scheme-relative URLs and backslashes
            if (value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\'))
                return fallback;

            // block absolute URLs
            return value.Contains("://", StringComparison.Ordinal) ? fallback : value;
        }
    }
}