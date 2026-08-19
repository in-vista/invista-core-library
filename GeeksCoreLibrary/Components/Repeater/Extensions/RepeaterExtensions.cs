using System;
using System.Net.Http;

namespace GeeksCoreLibrary.Components.Repeater.Extensions;

public static class RepeaterExtensions
{
    public static HttpMethod ToNativeHttpMethod(this Repeater.HttpMethod method)
    {
        return method switch
        {
            Repeater.HttpMethod.Get => HttpMethod.Get,
            Repeater.HttpMethod.Post => HttpMethod.Post,
            Repeater.HttpMethod.Put => HttpMethod.Put,
            Repeater.HttpMethod.Patch => HttpMethod.Patch,
            Repeater.HttpMethod.Delete => HttpMethod.Delete,
            Repeater.HttpMethod.Options => HttpMethod.Options,
            Repeater.HttpMethod.Head => HttpMethod.Head,
            Repeater.HttpMethod.Connect => HttpMethod.Connect,
            Repeater.HttpMethod.Trace => HttpMethod.Trace,
            _ => throw new ArgumentNullException(nameof(method))
        };
    }
}