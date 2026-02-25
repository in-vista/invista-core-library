using JetBrains.Annotations;

namespace GeeksCoreLibrary.Modules.GoogleAuth.Models
{
    public sealed record GoogleUserInfo(
        string EmailAddress,
        [CanBeNull] string FirstName,
        [CanBeNull] string LastName,
        [CanBeNull] string GoogleSubjectIdentifier,
        bool? IsEmailVerified);

    public sealed record GoogleUserLoginResult(
        bool IsSuccess,
        [CanBeNull] string FailureStatus,
        ulong AccountId,
        ulong UserId,
        bool IsNewUser,
        [CanBeNull] string CookieValue);
    
    public static class GoogleAuthEntityTypes
    {
        public const string WiserUser = "WiserUser";
        public const string Customer = "Customer";

        public static bool IsAllowed(string entityType) =>
            entityType is WiserUser or Customer;
    }
}