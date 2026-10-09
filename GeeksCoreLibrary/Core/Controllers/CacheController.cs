using GeeksCoreLibrary.Core.Enums;
using GeeksCoreLibrary.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GeeksCoreLibrary.Core.Controllers
{
    [Area("Cache")]
    public class CacheController : Controller
    {
        // Bind the multiple servers property to all GET requests to optionally avoid clearing all servers
        [BindProperty(SupportsGet = true)] 
        public bool MultipleServers { get; set; } = true;
        
        private readonly ICacheService cacheService;

        public CacheController(ICacheService cacheService)
        {
            this.cacheService = cacheService;
        }

        [Route("clear{cacheArea}cache.gcl")]
        [HttpGet]
        public IActionResult ClearCacheInArea(CacheAreas cacheArea)
        {
            if (cacheArea == CacheAreas.Unknown)
            {
                // Treat unknown cache areas as 404.
                return NotFound();
            }

            cacheService.ClearCacheInArea(cacheArea, MultipleServers);
            return Ok();
        }
        
        [Route("clearmemorycache.gcl")]
        [Route("clearcache.gcl")]
        [Route("clearcache.jcl")]
        [HttpGet]
        public IActionResult ClearCache()
        {
            cacheService.ClearMemoryCache(MultipleServers);
            return Ok();
        }

        [Route("clearcontentcache.gcl")]
        [Route("clearcontentcache.jcl")]
        [HttpGet]
        public IActionResult ClearOutputCache()
        {
            cacheService.ClearOutputCache(MultipleServers);
            return Ok();
        }

        [Route("clearfilescache.gcl")]
        [Route("clearfilescache.jcl")]
        [HttpGet]
        public IActionResult ClearImageCache()
        {
            cacheService.ClearFilesCache(MultipleServers);
            return Ok();
        }
        
        [Route("clearallcache.gcl")]
        [Route("clearallcache.jcl")]
        [HttpGet]
        public IActionResult ClearAllCache()
        {
            cacheService.ClearAllCache(MultipleServers);
            return Ok();
        }
    }
}
