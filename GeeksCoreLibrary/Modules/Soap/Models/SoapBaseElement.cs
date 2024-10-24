using System.Collections.Generic;
using System.Linq;

namespace GeeksCoreLibrary.Modules.Soap.Models;

public abstract class SoapBaseElement
{
    public string Tag { get; private set; }
    
    public abstract bool HasClosingTag { get; }

    private string @namespace;

    private Dictionary<string, string> attributes;

    public SoapBaseElement(string tag, string @namespace = "soap")
    {
        Tag = tag;
        this.@namespace = @namespace;
        attributes = new Dictionary<string, string>();
    }

    public void SetAttribute(string key, string value)
    {
        attributes.Add(key, value);
    }

    protected abstract string GetContent();
    
    public override string ToString()
    {
        string formattedAttributes = string.Join(" ", attributes.Select(kvp => $"{kvp.Key}=\"{kvp.Value}\""));
        string closingTag = HasClosingTag ? $">{GetContent()}</{Tag}>" : "/>";
        return $"<{@namespace}:{Tag} {formattedAttributes}${closingTag}";
    }
}