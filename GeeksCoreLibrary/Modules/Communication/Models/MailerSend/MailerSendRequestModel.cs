using System;
using System.Collections.Generic;
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
    public bool TrackClicks { get; set; }
    public bool TrackOpens { get; set; }
    public bool TrackContent  { get; set; }
}

// All keys must be lower case to MailerSend API
public class LowercaseContractResolver : DefaultContractResolver
{
    protected override string ResolvePropertyName(string propertyName)
    {
        return propertyName.ToLower();
    }
}