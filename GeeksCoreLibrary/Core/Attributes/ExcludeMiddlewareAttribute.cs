using System;

namespace GeeksCoreLibrary.Core.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class ExcludeMiddlewareAttribute : Attribute
{
    public Type[] MiddlewareType { get; private set; }
    
    public ExcludeMiddlewareAttribute(params Type[] middlewareType)
    {
        MiddlewareType = middlewareType;
    }
}