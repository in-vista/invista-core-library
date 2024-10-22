using System.Collections.Generic;
using System.Linq;

namespace GeeksCoreLibrary.Modules.Soap.Models;

public class SoapElement
{
    public string Tag { get; private set; }
    
    private object content;

    private Dictionary<string, string> attributes;

    public SoapElement(string tag, object content)
    {
        Tag = tag;
        this.content = content;
        attributes = new Dictionary<string, string>();
    }
    
    public SoapElement(string tag) : this(tag, null) { }

    public void SetAttribute(string key, string value)
    {
        attributes.Add(key, value);
    }

    public T GetContent<T>()
    {
        return (T)content;
    }
    
    public override string ToString()
    {
        string formattedAttributes = string.Join(" ", attributes.Select(kvp => $"{kvp.Key}=\"{kvp.Value}\""));
        string formattedContent = content is SoapElement[] soapElements
            ? string.Join(string.Empty, soapElements.Select(soapElement => soapElement.ToString()))
            : content.ToString();
        string closingTag = content == null ? "/>" : $">{formattedContent}</{Tag}>";
        return $"<{Tag} {formattedAttributes}${closingTag}";
    }
}