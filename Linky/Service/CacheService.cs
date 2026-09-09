using System.Text.Json;
using Linky.IService;
using GlideConnectionMultiplexer = Valkey.Glide.ConnectionMultiplexer;
using GlideDatabase = Valkey.Glide.IDatabaseAsync;

namespace Linky.Service
{
    public sealed class CacheService : ICacheService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly GlideDatabase _cache;

        public CacheService(GlideConnectionMultiplexer connection)
        {
            _cache = connection.GetDatabase();
        }

        public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var entry = await _cache.StringGetAsync(key).ConfigureAwait(false);
            if (entry.IsNullOrEmpty)
                return default;

            return JsonSerializer.Deserialize<T>(entry.ToString()!, JsonOptions);
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            await _cache.KeyDeleteAsync(key).ConfigureAwait(false);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await _cache.StringSetAsync(key, json, ttl).ConfigureAwait(false);
        }
    }
}