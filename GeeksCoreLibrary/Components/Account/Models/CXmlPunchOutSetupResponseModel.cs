using System;
using System.Xml.Serialization;

namespace GeeksCoreLibrary.Components.Account.Models;

[XmlRoot("cXML")]
public class CXmlPunchOutSetupResponseModel
{
    public CXmlPunchOutSetupResponseModel()
    {
        // Initialize the Response property to avoid null reference
        Response = new Response();
        Response.PunchOutSetupResponse = new PunchOutSetupResponse();
        Response.PunchOutSetupResponse.StartPage = new StartPage();
        Response.Status = new Status();
        Timestamp = DateTime.Now;
    }
    
    [XmlAttribute("payloadID")]
    public string PayloadID { get; set; }

    [XmlAttribute("timestamp")]
    public DateTime Timestamp { get; set; }

    [XmlElement("Response")]
    public Response Response { get; set; }
}

public class Response
{
    [XmlElement("Status")]
    public Status Status { get; set; }

    [XmlElement("PunchOutSetupResponse")]
    public PunchOutSetupResponse PunchOutSetupResponse { get; set; }
}

public class Status
{
    [XmlAttribute("code")]
    public int Code { get; set; }

    [XmlAttribute("text")]
    public string Text { get; set; }
    
    [XmlText]
    public string InnerText { get; set; }
}

public class PunchOutSetupResponse
{
    [XmlElement("StartPage")]
    public StartPage StartPage { get; set; }
}

public class StartPage
{
    [XmlElement("URL")]
    public string URL { get; set; }
}