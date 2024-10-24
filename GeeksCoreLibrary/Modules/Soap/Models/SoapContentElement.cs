namespace GeeksCoreLibrary.Modules.Soap.Models;

public class SoapContentElement : SoapBaseElement
{
    public override bool HasClosingTag => true;
    
    private string Content { get; set; }
    
    public SoapContentElement(string tag, string @namespace = "soap") : base(tag, @namespace)
    {
        
    }

    protected override string GetContent()
    {
        return Content.ToString();
    }
}