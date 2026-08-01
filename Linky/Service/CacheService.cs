using Linky.IService;
using UiPath.Caching;

namespace Linky.Service
{
    public sealed class CacheService : ICacheService
    {
        private const string ProviderName = "InMemoryRedis";  // or "InMemoryRedis" or "Redis"
        private readonly ICache _cache;

        public CacheService(ICacheFactory cacheFactory)
        {
            _cache = cacheFactory.CreateCache(ProviderName);
        }

        public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var entry = await _cache.GetCacheEntryAsync<T>(key, cancellationToken).ConfigureAwait(false);
            return entry.Found ? entry.Value : default;
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            // Safe now: IdentityCacheKeyStrategy means the physical key never depends on T,
            // so removing without knowing the original T is correct, not a bug.
            await _cache.RemoveAsync<object>(key, cancellationToken).ConfigureAwait(false);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            await _cache.SetAsync(key, value!, ttl, cancellationToken).ConfigureAwait(false);
        }
    }
}