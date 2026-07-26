using Linky.IService;
using UiPath.Caching;

namespace Linky.Service
{
    public sealed class CacheService : ICacheService
    {
        private const string ProviderName = "InMemoryRedis";
        private readonly ICache _cache;

        public CacheService(ICacheFactory cacheFactory)
        {
            _cache = cacheFactory.CreateCache(ProviderName);
        }

        public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var entry = await _cache.GetCacheEntryAsync<object>(key, cancellationToken).ConfigureAwait(false);

            if (!entry.Found || entry.Value is null)
                return default;

            return (T)entry.Value;
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            await _cache.RemoveAsync<object>(key, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            await _cache.SetAsync(key, value!, ttl, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
