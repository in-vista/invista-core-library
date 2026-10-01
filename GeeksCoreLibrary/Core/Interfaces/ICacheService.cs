using System;
using System.Collections.Concurrent;
using System.Threading;
using GeeksCoreLibrary.Core.Enums;
using LazyCache;
using Microsoft.Extensions.Caching.Memory;

namespace GeeksCoreLibrary.Core.Interfaces
{
    /// <summary>
    /// Service that provides several <see cref="CancellationTokenSource"/> objects that can be used for the caching. Cancelling
    /// these tokens will clear all the cache items that were subscribed to that token.
    /// </summary>
    public interface ICacheService : IDisposable
    {
        /// <summary>
        /// The Dictionary responsible for all types of <see cref="CancellationTokenSources"/> objects for all different types of cache areas, as defined in <see cref="CacheAreas"/>.
        /// </summary>
        ConcurrentDictionary<CacheAreas, CancellationTokenSource> CancellationTokenSources { get; }

        /// <summary>
        /// Retrieve a <see cref="CancellationTokenSource"/> object belonging to the given cache area. If it doesn't exist yet, it will be created.
        /// </summary>
        /// <param name="cacheArea"></param>
        /// <returns></returns>
        CancellationTokenSource GetCacheAreaCancellationTokenSource(CacheAreas cacheArea);

        /// <summary>
        /// Clear the cache from a specific cache area.
        /// </summary>
        /// <param name="cacheArea"></param>
        /// <param name="multipleServers">Optional: Set to true to clear the cache on multiple servers defined in easy_objects.</param>
        void ClearCacheInArea(CacheAreas cacheArea, bool multipleServers = true);

        /// <summary>
        /// Clear the cache of all cache areas.
        /// </summary>
        /// <param name="multipleServers">Optional: Set to true to clear the cache on multiple servers defined in easy_objects.</param>
        void ClearMemoryCache(bool multipleServers = true);

        /// <summary>
        /// Attempt to delete all files in the "contentcache" folder.
        /// </summary>
        /// <param name="multipleServers">Optional: Set to true to clear the cache on multiple servers defined in easy_objects.</param>
        void ClearOutputCache(bool multipleServers = true);

        /// <summary>
        /// Attempt to delete all files in the "contentfiles" folder.
        /// </summary>
        /// <param name="multipleServers">Optional: Set to true to clear the cache on multiple servers defined in easy_objects.</param>
        void ClearFilesCache(bool multipleServers = true);

        /// <summary>
        /// Clear all memory cache and file cache.
        /// </summary>
        /// <param name="multipleServers">Optional: Set to true to clear the cache on multiple servers defined in easy_objects.</param>
        void ClearAllCache(bool multipleServers = true);

        /// <summary>
        /// Creates a <see cref="MemoryCacheEntryOptions"/> object that can be used in LazyCache's GetOrAdd function.
        /// </summary>
        /// <param name="cacheArea"></param>
        /// <returns></returns>
        MemoryCacheEntryOptions CreateMemoryCacheEntryOptions(CacheAreas cacheArea);
    }
}
