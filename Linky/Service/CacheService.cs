using Linky.IService;
using ZiggyCreatures.Caching.Fusion;

namespace Linky.Service
{
    public sealed class CacheService : ICacheService
    {
        private readonly IFusionCache _cache;

        public CacheService(IFusionCache cache)
        {
            _cache = cache;
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            // using TryGet to avoid boxing of value types instead of GetAsync !!!!!
            var maybeValue = await _cache.TryGetAsync<T>(key, options: null, cancellationToken)
                .ConfigureAwait(false);

            return maybeValue.HasValue ? maybeValue.Value : default;
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            await _cache.RemoveAsync(key, options: null, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            var options = new FusionCacheEntryOptions
            {
                Duration = ttl,
                AllowBackgroundDistributedCacheOperations = true
            };

            await _cache.SetAsync(key, value, options, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
