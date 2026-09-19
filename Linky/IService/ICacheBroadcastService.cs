using MQTTnet.Protocol;

namespace Linky.IService
{
    public interface ICacheBroadcastService
    {
        /// <summary>
        /// Updates the Redis/Valkey cache asynchronously and immediately broadcasts the payload to EMQX via MQTT.
        /// </summary>
        /// <param name="cacheKey">The key to store in Redis/Valkey.</param>
        /// <param name="payload">The raw string/JSON payload.</param>
        /// <param name="mqttTopic">The EMQX MQTT topic to fan out payload to connected clients.</param>
        /// <param name="expiry">Optional TTL for the Redis cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task UpdateAndBroadcastAsync(
            string cacheKey,
            string payload,
            string mqttTopic,
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Serializes an object to JSON, updates Redis/Valkey cache, and broadcasts to EMQX via MQTT.
        /// </summary>
        Task UpdateAndBroadcastAsync<T>(
            string cacheKey,
            T data,
            string mqttTopic,
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default);
    }
}
