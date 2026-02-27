using System;
using System.Linq;
using System.Threading.Tasks;
using GeeksCoreLibrary.Core.Helpers;
using GeeksCoreLibrary.Modules.Databases.Interfaces;
using GeeksCoreLibrary.Modules.GoogleAuth.Interfaces;
using GeeksCoreLibrary.Modules.GoogleAuth.Models;
using GeeksCoreLibrary.Modules.Objects.Interfaces;
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
        private readonly IObjectsService objectsService;

        public GoogleAuthController(
            IGoogleAuthService googleAuthService,
            IDatabaseConnection databaseConnection,
            IObjectsService objectsService)
        {
            this.databaseConnection = databaseConnection;
            this.googleAuthService = googleAuthService;
            this.objectsService = objectsService;
        }

        private string originalUrl = "/";
        
        [HttpGet("/google-login.gcl")]
        public async Task<IActionResult> SignInWithGoogle(
            [FromQuery] string entityType = "",
            [FromQuery] ulong accountId = 0)
        {
            var configurationText = await objectsService.FindSystemObjectByDomainNameAsync("SSO_setup");
            var parsed = GoogleEasyObjectsSettingsParser.Parse(configurationText);
            
            originalUrl =  Request.Headers.Referer.ToString().Split("?")[0];
            
            if(parsed.EntityTypeRulesByKey.Values.All(value => value.EntityType != entityType))
                return Redirect($"{originalUrl}?status=invalid_entity_type");
            
            if (!parsed.GetRule(entityType, "Login").GetAllowed() && !parsed.GetRule(entityType, "Create").GetAllowed())
                return Redirect($"{originalUrl}?status=no_actions_allowed");

            // This is where Google will redirect back to after login.
            var callbackUrl = Url.ActionLink(
                action: nameof(GoogleCallback),
                controller: "GoogleAuth",
                values: new { entityType, accountId })!;

            var authenticationProperties = new AuthenticationProperties
            {
                RedirectUri = callbackUrl
            };

            return Challenge(authenticationProperties, GoogleDefaults.AuthenticationScheme);
        }
        
        [HttpGet("/google-callback.gcl")]
        public async Task<IActionResult> GoogleCallback(
            [FromQuery] string entityType = "",
            [FromQuery] ulong accountId = 0)
        {
            var configurationText = await objectsService.FindSystemObjectByDomainNameAsync("SSO_setup");
            var parsed = GoogleEasyObjectsSettingsParser.Parse(configurationText);
            
            if(parsed.EntityTypeRulesByKey.Values.All(value => value.EntityType != entityType))
                return Redirect($"{originalUrl}?status=invalid_entity_type");
            
            var loginAllowed = parsed.GetRule(entityType, "Login").GetAllowed();
            var creationAllowed = parsed.GetRule(entityType, "Create").GetAllowed();
            
            if (!loginAllowed && !creationAllowed)
                return Redirect($"{originalUrl}?status=no_actions_allowed");
            
            var baseAction = loginAllowed ? "Login" : "Create";
            
            var loginUrl = parsed.GetRule(entityType, baseAction).GetLoginUrl();
            var callBackUrl = parsed.GetRule(entityType, baseAction).GetCallbackUrl();
            
            if (!parsed.AllowAccountIdOverride)
                accountId = 0;
            
            if(accountId == 0 && parsed.AccountIdMandatory)
                accountId = await ResolveAccountIdAsync();
            
            var doRedirect = parsed.GetRule(entityType, "Login").GetDoRedirect();
            
            var authenticateResult =
                await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                return doRedirect
                    ? Redirect($"{originalUrl}?status=google_failed")
                    : Unauthorized(new { status = "google_failed" });
            }

            var result =
                await googleAuthService.HandleGoogleCallbackAsync(authenticateResult.Principal, parsed, entityType, accountId);

            if (!result.IsSuccess)
            {
                return doRedirect
                    ? Redirect($"{callBackUrl ?? loginUrl}?status={result.FailureStatus}")
                    : BadRequest(new { status = result.FailureStatus });
            }

            var action = result.IsNewUser ? "Create" : "Login";

            var cookieExpire = parsed.GetRule(entityType, action).GetCookieExpireTime();
            var returnUrl = parsed.GetRule(entityType, action).GetReturnUrl();

            // Write http cookie
            HttpContextHelpers.WriteCookie(
                HttpContext,
                "gcl_user_cookie",
                result.CookieValue!,
                DateTimeOffset.Now.AddDays(cookieExpire),
                httpOnly: true,
                isEssential: true,
                secure: true
                );

            if (!doRedirect)
                return Ok(new
                {
                    status = "ok",
                    accountId = result.AccountId,
                    customerId = result.UserId,
                    isNewUser = result.IsNewUser
                });
            
            
            return Redirect(callBackUrl ?? returnUrl);
        }

        private async Task<ulong> ResolveAccountIdAsync()
        {
            databaseConnection.ClearParameters();
            var accountTable = await databaseConnection.GetAsync(
                @$"SELECT GetAccountIdBasedOnPrefix('{HttpContext.Request.GetDisplayUrl()}')",
                skipCache: true);

            return Convert.ToUInt64(accountTable.Rows[0][0]);
        }
    }
}