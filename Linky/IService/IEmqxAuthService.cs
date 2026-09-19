namespace Linky.IService
{
    public interface IEmqxAuthService
    {
        /// <summary>
        /// Provision client credentials in Redis/Valkey for EMQX native Redis AuthN (authn/redis).
        /// WebSockets/MQTT clients authenticate directly against Redis via EMQX Erlang engine without hitting .NET backend.
        /// </summary>
        Task ProvisionMqttUserAsync(string username, string password, bool isSuperUser = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Revoke client credentials from Redis/Valkey.
        /// </summary>
        Task RevokeMqttUserAsync(string username, CancellationToken cancellationToken = default);
    }
}
