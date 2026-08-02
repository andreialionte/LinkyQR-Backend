using System.Text.Json;
using Linky.IService;
using UiPath.Caching;
using ZstdSharp;

namespace Linky.Service
{
    public sealed class CacheService : ICacheService
    {
        private const string ProviderName = "Redis"; // Garnet/Dragonfly nu suportă comenzile de Redis Streams necesare pentru InMemoryRedis broadcast
        
        // Only worth compressing above this size — Zstd has fixed overhead
        // (frame headers etc.) that makes it a net loss on tiny payloads
        // like a cached int? or a short string.
        private const int CompressionThresholdBytes = 0; //to work 100% i can set it later
        
        // in the future i can make like : 
        /*private const int FastLevel = -3;
        // Payload-uri mari (liste, agregate) — merită un ratio mai bun,
        // CPU-ul extra e neglijabil comparativ cu economia de bytes.
        private const int BalancedLevel = 3;
        // Peste pragul ăsta chiar merită nivelul "balanced" în loc de "fast".
        private const int LargePayloadBytes = 4096;*/

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ICache _cache;

        public CacheService(ICacheFactory cacheFactory)
        {
            _cache = cacheFactory.CreateCache(ProviderName);
        }

        public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var entry = await _cache.GetCacheEntryAsync<byte[]>(key, cancellationToken).ConfigureAwait(false);
            if (!entry.Found || entry.Value is null || entry.Value.Length == 0)
                return default;

            var raw = entry.Value;
            byte[] jsonBytes = raw[0] == 1
                ? Decompress(raw.AsSpan(1))
                : raw[1..]; // flag byte 0 => stored uncompressed, skip it

            return JsonSerializer.Deserialize<T>(jsonBytes, JsonOptions);
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            await _cache.RemoveAsync<object>(key, cancellationToken).ConfigureAwait(false);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

            byte[] payload;
            if (jsonBytes.Length >= CompressionThresholdBytes)
            {
                using var compressor = new Compressor(level: -4); // 1 (fastest) – 22 (smallest); 3 is Zstd's own default, 6 is a good speed/ratio tradeoff for hot-path caches
                var compressed = compressor.Wrap(jsonBytes);
                payload = new byte[compressed.Length + 1];
                payload[0] = 1; // flag: compressed
                compressed.CopyTo(payload.AsSpan(1));
            }
            else
            {
                payload = new byte[jsonBytes.Length + 1];
                payload[0] = 0; // flag: not compressed
                jsonBytes.CopyTo(payload.AsSpan(1));
            }

            await _cache.SetAsync(key, payload, ttl, cancellationToken).ConfigureAwait(false);
        }

        private static byte[] Decompress(ReadOnlySpan<byte> compressed)
        {
            using var decompressor = new Decompressor();
            return decompressor.Unwrap(compressed).ToArray();
        }
    }
}