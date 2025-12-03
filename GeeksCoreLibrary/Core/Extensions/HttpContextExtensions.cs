using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace GeeksCoreLibrary.Core.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// Gives the action context based on this HTTP context.
    /// </summary>
    /// <param name="httpContext">The HTTP context to retrieve the action context from.</param>
    /// <returns>A <see cref="ActionContext"/> instance represented by the given <see cref="HttpContext"/>.</returns>
    public static ActionContext GetActionContext(this HttpContext httpContext)
    {
        Endpoint endpoint = httpContext?.GetEndpoint();
        
        ControllerActionDescriptor actionDescriptor = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        
        if (actionDescriptor == null)
            return null;
        
        RouteData routeData = httpContext!.GetRouteData();
        
        return new ActionContext(
            httpContext,
            routeData,
            actionDescriptor
        );
    }
}