namespace GeeksCoreLibrary.Modules.Communication.Models.MailerSend;

public class MailerSendSettings
{
    /// <summary>
    /// Gets or sets the access token for the API.
    /// </summary>
    public string ApiAccessToken { get; set; }

    /// <summary>
    /// Gets or sets if this account is a professional or enterprise account. These types of accounts have additional functionality.
    /// </summary>
    public bool IsProfessionalOrEnterpriseAccount { get; set; } = true;
}