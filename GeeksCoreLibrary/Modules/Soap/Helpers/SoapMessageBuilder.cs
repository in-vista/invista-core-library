using GeeksCoreLibrary.Modules.Soap.Models;

namespace GeeksCoreLibrary.Modules.Soap.Helpers;

public class SoapMessageBuilder
{
    private SoapMessage message;

    public SoapMessageBuilder(
        string schema = "http://www.w3.org/2003/05/soap-envelope",
        string encodingStyle = "http://www.w3.org/2003/05/soap-encoding")
    {
        message = new SoapMessage(schema, encodingStyle);
    }

    public SoapMessageBuilder WithHeader()
    {
        
        
        return this;
    }

    public SoapMessageBuilder WithBody()
    {
        return this;
    }

    public SoapMessageBuilder WithFault()
    {
        return this;
    }
}