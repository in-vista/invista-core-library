using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GeeksCoreLibrary.Core.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GeeksCoreLibrary.Core.Extensions;

public static class MiddlewareExtensions
{
    /// <summary>
    /// Registers a global middleware that can be excluded by endpoints that use the <see cref="ExcludeMiddlewareAttribute"/> attribute.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> of the current app.</param>
    /// <typeparam name="T">The type of middleware to register to the app.</typeparam>
    /// <returns>The <see cref="IApplicationBuilder"/> instance of the app.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the middleware does not contain an Invoke / InvokeAsync method.</exception>
    public static IApplicationBuilder UseMiddlewareWithExclude<T>(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            // Retrieve the exclusion attributes set on the endpoint's method.
            Endpoint endpoint = context.GetEndpoint();
            IReadOnlyList<ExcludeMiddlewareAttribute> excludes = endpoint?.Metadata.GetOrderedMetadata<ExcludeMiddlewareAttribute>();
            
            // Check if the exclusion attribute has the current middleware set.
            if (excludes?.Any(x => x.MiddlewareType.Contains(typeof(T))) == true)
            {
                await next(context);
                return;
            }

            // Create middleware instance through DI.
            T middleware = ActivatorUtilities.CreateInstance<T>(context.RequestServices, next);

            // Find and validate the Invoke or InvokeAsync method.
            MethodInfo method = typeof(T).GetMethod("InvokeAsync") ?? typeof(T).GetMethod("Invoke");
            if (method == null)
                throw new InvalidOperationException("Middleware must have an Invoke or InvokeAsync method.");

            // Build parameter list (HttpContext + optional DI)
            object[] parameters = method.GetParameters()
                .Select(parameterInfo => parameterInfo.ParameterType == typeof(HttpContext)
                    ? context
                    : context.RequestServices.GetService(parameterInfo.ParameterType))
                .ToArray();
            
            // Invoke the middleware.
            await (Task)method.Invoke(middleware, parameters)!;
        });
    }
}