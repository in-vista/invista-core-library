using System.Text;

namespace GeeksCoreLibrary.Modules.Soap.Models;

internal class SoapMessage
{
    private string schema;

    private string encodingStyle;

    public SoapElement Header { get; internal set; }

    public SoapElement Body { get; internal set; }

    public SoapMessage(
        string schema = "http://www.w3.org/2003/05/soap-envelope",
        string encodingStyle = "http://www.w3.org/2003/05/soap-encoding")
    {
        this.schema = schema;
        this.encodingStyle = encodingStyle;
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();

        builder.Append("<?xml version=\"1.0\"?>");
        builder.Append($"<soap:Envelope xmlns:soap=\"{schema}\" soap:encodingStyle=\"{encodingStyle}\">");

        
        
        builder.Append("</soap:Envelope>");

        return builder.ToString();
    }
}