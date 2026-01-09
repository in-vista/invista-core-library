using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace GeeksCoreLibrary.Modules.Communication.Models.MailerSend;

public class MailerSendRequestModel
{
    /// <summary>
    /// Gets or sets who the email is from.
    /// </summary>
    public MailerSendContactModel From { get; set; }
    
    /// <summary>
    /// Gets or sets the reply to contact
    /// </summary>
    [JsonProperty("reply_to")]
    public MailerSendContactModel ReplyTo { get; set; }
    
    /// <summary>
    /// Gets or sets the recipients the email is send to.
    /// </summary>
    public List<MailerSendContactModel> To { get; set; }
    
    /// <summary>
    /// Gets or sets the CC recipients the email is send to.
    /// </summary>
    public List<MailerSendContactModel> Cc { get; set; }
    
    /// <summary>
    /// Gets or sets the BCC recipients the email is send to.
    /// </summary>
    public List<MailerSendContactModel> Bcc { get; set; }
    
    /// <summary>
    /// Gets or sets the subject of the email.
    /// </summary>
    public string Subject { get; set; }
    
    /// <summary>
    /// Gets or sets the text body of the email.
    /// </summary>
    public string Text { get; set; }
    
    /// <summary>
    /// Gets or sets the HTML body of the email.
    /// </summary>
    public string Html { get; set; }
    
    //// <summary>
    //// Gets or sets the attachments to ben send with the email.
    //// </summary>
    public List<MailerSendAttachmentModel> Attachments { get; set; }
    
    /// <summary>
    /// Settings to use when sending the email 
    /// </summary>
    public MailerSendSettingsModel Settings { get; set; }
    
    /// <summary>
    /// Tags to save with the email. Limit is max 5 tags. 
    /// </summary>
    public List<string> Tags { get; set; }
    
    /// <summary>
    /// Headers to save with the email. Please note that this feature is available to Professional and Enterprise accounts only.
    /// </summary>
    public List<MailerSendHeadersModel> Headers { get; set; }
}

public class MailerSendContactModel
{
    public string Email { get; set; }
    public string Name { get; set; }
}

public class MailerSendAttachmentModel
{
    public string Content { get; set; }
    public string Disposition {  get; set; }
    public string FileName {  get; set; }
    public string Id {  get; set; }
}

public class MailerSendSettingsModel
{
    [JsonProperty("track_clicks")]
    public bool TrackClicks { get; set; }
    [JsonProperty("track_opens")]
    public bool TrackOpens { get; set; }
    [JsonProperty("track_content")]
    public bool TrackContent  { get; set; }
}

public class MailerSendHeadersModel
{
    public string Name { get; set; }
    public string Value { get; set; }
}

// All keys must be lower case to MailerSend API
public class LowercaseContractResolver : DefaultContractResolver
{
    protected override string ResolvePropertyName(string propertyName)
    {
        return propertyName.ToLower();
    }
}