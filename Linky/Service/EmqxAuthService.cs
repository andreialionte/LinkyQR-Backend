using Linky.IService;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;

namespace Linky.Service
{
    public sealed class EmqxAuthService : IEmqxAuthService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<EmqxAuthService> _logger;

        public EmqxAuthService(IConnectionMultiplexer redis, ILogger<EmqxAuthService> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProvisionMqttUserAsync(string username, string password, bool isSuperUser = false, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(username);
            ArgumentException.ThrowIfNullOrWhiteSpace(password);

            // Generate random 16-byte salt
            byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
            string saltHex = Convert.ToHexString(saltBytes).ToLowerInvariant();

            // Hash: password + salt (SHA256) matching EMQX authn/redis configuration
            byte[] passwordAndSaltBytes = Encoding.UTF8.GetBytes(password + saltHex);
            byte[] hashBytes = SHA256.HashData(passwordAndSaltBytes);
            string hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

            string redisKey = $"mqtt_user:{username}";

            var db = _redis.GetDatabase();
            var entries = new HashEntry[]
            {
                new("password_hash", hashHex),
                new("salt", saltHex),
                new("is_superuser", isSuperUser ? "true" : "false")
            };

            await db.HashSetAsync(redisKey, entries).ConfigureAwait(false);
            _logger.LogInformation("Provisioned EMQX MQTT credentials in Redis for user '{Username}' (Redis Key: {Key})", username, redisKey);
        }

        public async Task RevokeMqttUserAsync(string username, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(username);
            string redisKey = $"mqtt_user:{username}";

            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            _logger.LogInformation("Revoked EMQX MQTT credentials from Redis for user '{Username}'", username);
        }
    }
}
