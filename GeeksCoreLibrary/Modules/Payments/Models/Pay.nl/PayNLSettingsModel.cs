using GeeksCoreLibrary.Components.OrderProcess.Models;

namespace GeeksCoreLibrary.Modules.Payments.PayNl.Models;

public class PayNlSettingsModel : PaymentServiceProviderSettingsModel
{
    /// <summary>
    /// Gets or sets the account code
    /// This can either be with an AT code and an SL code
    /// </summary>
    public string? AccountCode { get; set; }

    /// <summary>
    /// Gets or sets the token.
    /// This is a token if the username is an AT code or a secret if the username is a SL code
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the Service ID. Required if logging in with an AT-code/token
    /// </summary>
    public string? ServiceId { get; set; }
}