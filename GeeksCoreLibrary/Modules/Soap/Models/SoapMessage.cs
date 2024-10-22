using System.Text;

namespace GeeksCoreLibrary.Modules.Soap.Models;

internal class SoapMessage
{
    private string schema;

    private string encodingStyle;

    private SoapElement header;

    private SoapElement body;

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

        if (header != null)
            builder.Append(header);

        if (body != null)
            builder.Append(body);
        
        builder.Append("</soap:Envelope>");

        return builder.ToString();
    }
}