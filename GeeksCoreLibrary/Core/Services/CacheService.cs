using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GeeksCoreLibrary.Core.DependencyInjection.Interfaces;
using GeeksCoreLibrary.Core.Enums;
using GeeksCoreLibrary.Core.Helpers;
using GeeksCoreLibrary.Core.Interfaces;
using GeeksCoreLibrary.Modules.Objects.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace GeeksCoreLibrary.Core.Services
{
    public class CacheService : ICacheService, ISingletonService
    {
        private readonly IWebHostEnvironment webHostEnvironment;
        private readonly ILogger<CacheService> logger;
        private readonly IServiceScopeFactory serviceScopeFactory;
        private readonly IHttpClientService httpClientService;

        /// <inheritdoc />
        public ConcurrentDictionary<CacheAreas, CancellationTokenSource> CancellationTokenSources { get; }

        public CacheService(ILogger<CacheService> logger, IServiceScopeFactory serviceScopeFactory, IHttpClientService httpClientService, IWebHostEnvironment webHostEnvironment = null)
        {
            this.webHostEnvironment = webHostEnvironment;
            this.logger = logger;
            this.serviceScopeFactory = serviceScopeFactory;
            this.httpClientService = httpClientService;

            // The default concurrency of a concurrent dictionary if the processor count.
            CancellationTokenSources = new ConcurrentDictionary<CacheAreas, CancellationTokenSource>(Environment.ProcessorCount, Enum.GetNames<CacheAreas>().Length);
            foreach (var cacheArea in Enum.GetValues<CacheAreas>())
            {
                // The value "unknown" is not a valid cache area, so don't add it to the dictionary.
                if (cacheArea == CacheAreas.Unknown)
                {
                    continue;
                }

                CancellationTokenSources.TryAdd(cacheArea, new CancellationTokenSource());
            }
        }

        /// <inheritdoc />
        public CancellationTokenSource GetCacheAreaCancellationTokenSource(CacheAreas cacheArea)
        {
            if (cacheArea == CacheAreas.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(cacheArea), cacheArea.ToString("G"), $"The cache area '{cacheArea:G}' cannot be used for caching.");
            }

            CancellationTokenSources.TryAdd(cacheArea, new CancellationTokenSource());
            return CancellationTokenSources[cacheArea];
        }

        /// <inheritdoc />
        public void ClearCacheInArea(CacheAreas cacheArea, bool multipleServers = true)
        {
            if (!CancellationTokenSources.TryGetValue(cacheArea, out var cancellationTokenSource))
            {
                return;
            }

            // Cancel the token source, which triggers the cache items to expire.
            cancellationTokenSource.Cancel();

            logger.LogInformation($"Cleared '{cacheArea:G}' cache.");

            // Dispose the old CancellationTokenSource. It no longer serves a purpose.
            cancellationTokenSource.Dispose();

            // Now re-create the token source.
            CancellationTokenSources[cacheArea] = new CancellationTokenSource();
            
            if (multipleServers)
            {
                _ = ClearCacheOnConfiguredServers($"clear{cacheArea.ToString().ToLowerInvariant()}cache.gcl");
            }
        }

        /// <inheritdoc />
        public void ClearMemoryCache(bool multipleServers = true)
        {
            foreach (var cacheArea in Enum.GetValues<CacheAreas>())
            {
                // Set multipleServers to false, this is to avoid propagation.
                ClearCacheInArea(cacheArea, false);
            }
            
            if (multipleServers)
            {
                _ = ClearCacheOnConfiguredServers("clearcache.gcl");
            }
        }

        /// <inheritdoc />
        public void ClearOutputCache(bool multipleServers = true)
        {
            var outputCacheFolder = FileSystemHelpers.GetContentCacheFolderPath(webHostEnvironment);
            if (String.IsNullOrWhiteSpace(outputCacheFolder))
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(outputCacheFolder);
            if (!directoryInfo.Exists)
            {
                return;
            }

            var withIssues = false;
            // Delete files.
            foreach (var file in directoryInfo.GetFiles())
            {
                try
                {
                    file.Delete();
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, $"An error occurred trying to delete file '{file.FullName}'.");
                    withIssues = true;
                }
            }

            // Delete directories.
            foreach (var directory in directoryInfo.GetDirectories())
            {
                try
                {
                    directory.Delete(true);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, $"An error occurred trying to delete directory '{directory.FullName}'.");
                    withIssues = true;
                }
            }

            logger.LogInformation(withIssues ? "Cleared output cache, but some files/directories could not be deleted." : "Cleared output cache.");
            
            if (multipleServers)
            {
                _ = ClearCacheOnConfiguredServers("clearcontentcache.gcl");
            }
        }

        /// <inheritdoc />
        public void ClearFilesCache(bool multipleServers = true)
        {
            var contentFilesFolder = FileSystemHelpers.GetContentFilesFolderPath(webHostEnvironment);
            if (String.IsNullOrWhiteSpace(contentFilesFolder))
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(contentFilesFolder);
            if (!directoryInfo.Exists)
            {
                return;
            }

            var withIssues = false;
            foreach (var file in directoryInfo.GetFiles())
            {
                try
                {
                    file.Delete();
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, $"An error occurred trying to delete '{file.FullName}'.");
                    withIssues = true;
                }
            }

            logger.LogInformation(withIssues ? "Cleared files cache, but some files could not be deleted." : "Cleared files cache.");
            
            if (multipleServers)
            {
                _ = ClearCacheOnConfiguredServers("clearfilescache.gcl");
            }
        }

        /// <inheritdoc />
        public void ClearAllCache(bool multipleServers = true)
        {
            // Set multipleServers to false on all requests, this is to avoid propagation.
            ClearMemoryCache(false);
            ClearOutputCache(false);
            ClearFilesCache(false);
            
            if (multipleServers)
            {
                _ = ClearCacheOnConfiguredServers("clearallcache.gcl");
            }
        }

        /// <inheritdoc />
        public MemoryCacheEntryOptions CreateMemoryCacheEntryOptions(CacheAreas cacheArea)
        {
            var expireToken = new CancellationChangeToken(GetCacheAreaCancellationTokenSource(cacheArea).Token);
            return new MemoryCacheEntryOptions().AddExpirationToken(expireToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var cacheArea in Enum.GetValues<CacheAreas>())
            {
                if (CancellationTokenSources.TryGetValue(cacheArea, out var cancellationTokenSource))
                {
                    cancellationTokenSource?.Dispose();
                }
            }
        }

        private async Task<bool> ClearCacheOnConfiguredServers(string cacheEndpoint)
        {
            try
            {
                // Create a scope for objectsService, because a singleton cannot hold a scoped service.
                // A singleton lives for the entire application lifetime, while a scoped service lives only for one request/scope.
                using IServiceScope scope = serviceScopeFactory.CreateScope();

                IObjectsService objectsService = scope.ServiceProvider.GetRequiredService<IObjectsService>();

                string clusterUrls =
                    await objectsService.GetSystemObjectValueAsync("clear_cache_cluster_urls", skipCache: true);

                if (string.IsNullOrWhiteSpace(clusterUrls))
                    return false;

                string[] clusterUrlList = clusterUrls
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                int successCount = 0;
                List<string> failedUrls = [];

                foreach (string clusterUrl in clusterUrlList)
                {
                    try
                    {
                        if (!TryNormalizeClusterUrl(clusterUrl, cacheEndpoint, out string? safeClusterUrl))
                        {
                            logger.LogWarning("Skipping invalid cache cluster URL '{ClusterUrl}'.", clusterUrl);
                            failedUrls.Add(clusterUrl);
                            continue;
                        }

                        using HttpRequestMessage requestMessage = new(HttpMethod.Get, safeClusterUrl);
                        using HttpResponseMessage response = await httpClientService.Client.SendAsync(requestMessage);

                        if (response.IsSuccessStatusCode)
                        {
                            successCount++;
                        }
                        else
                        {
                            failedUrls.Add(clusterUrl);

                            logger.LogWarning(
                                "Failed to clear cache on '{ClusterUrl}' using '{CacheEndpoint}'. Status code: {StatusCode}.",
                                clusterUrl,
                                cacheEndpoint,
                                (int)response.StatusCode);
                        }
                    }
                    catch (Exception exception)
                    {
                        failedUrls.Add(clusterUrl);

                        logger.LogWarning(
                            exception,
                            "An error occurred while clearing cache on '{ClusterUrl}' using '{CacheEndpoint}'. Continuing with the remaining configured servers.",
                            clusterUrl,
                            cacheEndpoint);
                    }
                }

                if (failedUrls.Count > 0)
                {
                    logger.LogWarning(
                        "Finished clearing cache on configured servers using '{CacheEndpoint}'. Successful: {SuccessCount}, failed: {FailedCount}. Failed servers: {FailedServers}.",
                        cacheEndpoint,
                        successCount,
                        failedUrls.Count,
                        string.Join(", ", failedUrls));
                }
                else
                {
                    logger.LogInformation(
                        "Successfully cleared cache on all {ServerCount} configured servers using '{CacheEndpoint}'.",
                        successCount,
                        cacheEndpoint);
                }

                return true;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "An error occurred trying to clear the cache on the other servers.");
                return false;
            }
        }

        /// <summary>
        /// Attempts to normalize a configured cache cluster URL.
        /// Ensures that the URL uses HTTP or HTTPS, points to the provided cache endpoint,
        /// and contains <c>multipleServers=false</c> while preserving any existing query parameters.
        /// Only base cluster URLs or URLs already pointing directly to the requested cache endpoint are allowed.
        /// </summary>
        /// <param name="clusterUrl">The configured server or cluster URL.</param>
        /// <param name="cacheEndpoint">The cache endpoint to call.</param>
        /// <param name="normalizedUrl">
        /// When this method returns <c>true</c>, contains the normalized URL.
        /// Otherwise, contains <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if the URL is valid and could be normalized; otherwise, <c>false</c>.
        /// </returns>
        private static bool TryNormalizeClusterUrl(string clusterUrl, string cacheEndpoint, out string? normalizedUrl)
        {
            normalizedUrl = null;

            if (string.IsNullOrWhiteSpace(clusterUrl) || string.IsNullOrWhiteSpace(cacheEndpoint))
            {
                return false;
            }

            try
            {
                clusterUrl = clusterUrl.Trim();
                cacheEndpoint = cacheEndpoint.Trim().TrimStart('/');

                if (cacheEndpoint.Contains('/') ||
                    cacheEndpoint.Contains('\\') ||
                    cacheEndpoint.Contains('?') ||
                    cacheEndpoint.Contains('#'))
                {
                    return false;
                }

                // Default to HTTPS when no scheme has been configured.
                if (!clusterUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !clusterUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    clusterUrl = $"https://{clusterUrl}";
                }

                if (!Uri.TryCreate(clusterUrl, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                // Only allow URLs that can actually be requested over HTTP.
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(uri.Host))
                {
                    return false;
                }

                UriBuilder uriBuilder = new(uri);

                string path = uriBuilder.Path.TrimEnd('/');
                
                // Define the general cache endpoints that may already be present in the configured URL.
                List<string> cacheEndpoints =
                [
                    "clearcache.gcl",
                    "clearmemorycache.gcl",
                    "clearcontentcache.gcl",
                    "clearfilescache.gcl",
                    "clearallcache.gcl"
                ];

                // Add the endpoints for each individual cache area.
                cacheEndpoints.AddRange(
                    Enum.GetValues<CacheAreas>()
                        .Where(cacheArea => cacheArea != CacheAreas.Unknown)
                        .Select(cacheArea => $"clear{cacheArea.ToString().ToLowerInvariant()}cache.gcl"));

                // Check whether the configured URL already points to one of the known cache endpoints.
                string? existingCacheEndpoint = cacheEndpoints.FirstOrDefault(endpoint =>
                    path.Equals($"/{endpoint}", StringComparison.OrdinalIgnoreCase));

                // Only allow an empty path or one of the known cache endpoints.
                // Any other path is considered invalid configuration.
                if (!string.IsNullOrWhiteSpace(path) && existingCacheEndpoint == null)
                {
                    return false;
                }

                // If the configured URL already contains a valid cache endpoint, keep it.
                // Otherwise, use the endpoint for the cache-clear action that is currently being propagated.
                uriBuilder.Path = existingCacheEndpoint != null ? $"/{existingCacheEndpoint}" : $"/{cacheEndpoint}";
                
                // Keep all existing query parameters.
                Dictionary<string, StringValues> queryParameters = QueryHelpers.ParseQuery(uriBuilder.Query);

                // Always disable further propagation to prevent recursion.
                queryParameters["multipleServers"] = "false";

                QueryBuilder queryBuilder = new();

                foreach (var parameter in queryParameters)
                {
                    foreach (string? value in parameter.Value)
                    {
                        queryBuilder.Add(parameter.Key, value);
                    }
                }

                uriBuilder.Query = queryBuilder.ToQueryString().Value?.TrimStart('?') ?? string.Empty;

                // Fragments are not sent to the server and have no purpose here.
                uriBuilder.Fragment = string.Empty;

                normalizedUrl = uriBuilder.Uri.AbsoluteUri;
                return true;
            }
            catch (Exception)
            {
                // Invalid configuration should not prevent other configured
                // cluster URLs from being processed.
                return false;
            }
        }
    }
}
