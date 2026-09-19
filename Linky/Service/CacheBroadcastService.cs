using Linky.IService;
using Microsoft.Extensions.Logging;
using MQTTnet.Protocol;
using StackExchange.Redis;
using System.Text.Json;

namespace Linky.Service
{
    public sealed class CacheBroadcastService : ICacheBroadcastService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IMqttClientService _mqttClientService;
        private readonly ILogger<CacheBroadcastService> _logger;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public CacheBroadcastService(
            IConnectionMultiplexer redis,
            IMqttClientService mqttClientService,
            ILogger<CacheBroadcastService> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _mqttClientService = mqttClientService ?? throw new ArgumentNullException(nameof(mqttClientService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task UpdateAndBroadcastAsync(
            string cacheKey,
            string payload,
            string mqttTopic,
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(mqttTopic);
            ArgumentNullException.ThrowIfNull(payload);

            try
            {
                // 1. Write payload ONCE to Redis / Valkey asynchronously
                var db = _redis.GetDatabase();
                bool redisSuccess = await db.StringSetAsync(cacheKey, payload, expiry).ConfigureAwait(false);

                if (!redisSuccess)
                {
                    _logger.LogWarning("Redis StringSetAsync returned false for key: {CacheKey}", cacheKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write update to Redis cache for key: {CacheKey}. Proceeding to broadcast via EMQX.", cacheKey);
            }

            try
            {
                // 2. Immediately publish payload ONCE to EMQX using QoS 0 (AtMostOnce) for ultra-high concurrency fan-out
                await _mqttClientService.PublishAsync(
                    topic: mqttTopic,
                    payload: payload,
                    qos: MqttQualityOfServiceLevel.AtMostOnce,
                    retain: false,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                _logger.LogDebug("Successfully published broadcast update to EMQX topic: {Topic} for cache key: {CacheKey}", mqttTopic, cacheKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish broadcast message to EMQX on topic: {Topic}", mqttTopic);
                throw;
            }
        }

        public Task UpdateAndBroadcastAsync<T>(
            string cacheKey,
            T data,
            string mqttTopic,
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            string payload = JsonSerializer.Serialize(data, JsonOptions);
            return UpdateAndBroadcastAsync(cacheKey, payload, mqttTopic, expiry, cancellationToken);
        }
    }
}
