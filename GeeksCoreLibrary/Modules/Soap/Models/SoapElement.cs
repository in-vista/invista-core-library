using System.Collections.Generic;
using System.Linq;

namespace GeeksCoreLibrary.Modules.Soap.Models;

public class SoapElement : SoapBaseElement
{
    public override bool HasClosingTag => false;

    private List<SoapBaseElement> children;
    
    public SoapElement(string tag, string @namespace = "soap") : base(tag, @namespace)
    {
        children = new List<SoapBaseElement>();
    }
    
    protected override string GetContent()
    {
        return string.Join("\n", children.Select(child => child.ToString()));
    }
}