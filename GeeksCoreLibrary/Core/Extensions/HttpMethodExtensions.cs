using System;
using GeeksCoreLibrary.Core.Enums;

namespace GeeksCoreLibrary.Core.Extensions;

public static class HttpMethodExtensions
{
    public static System.Net.Http.HttpMethod ToNativeHttpMethod(this HttpMethod method)
    {
        return method switch
        {
            HttpMethod.Get => System.Net.Http.HttpMethod.Get,
            HttpMethod.Post => System.Net.Http.HttpMethod.Post,
            HttpMethod.Put => System.Net.Http.HttpMethod.Put,
            HttpMethod.Patch => System.Net.Http.HttpMethod.Patch,
            HttpMethod.Delete => System.Net.Http.HttpMethod.Delete,
            HttpMethod.Options => System.Net.Http.HttpMethod.Options,
            HttpMethod.Head => System.Net.Http.HttpMethod.Head,
            HttpMethod.Connect => System.Net.Http.HttpMethod.Connect,
            HttpMethod.Trace => System.Net.Http.HttpMethod.Trace,
            _ => throw new ArgumentNullException(nameof(method))
        };
    }
}