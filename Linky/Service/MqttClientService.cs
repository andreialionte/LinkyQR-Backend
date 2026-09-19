using Linky.Config;
using Linky.IService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace Linky.Service
{
    public sealed class MqttClientService : IMqttClientService, IHostedService, IDisposable
    {
        private readonly IManagedMqttClient _managedMqttClient;
        private readonly MqttConfig _config;
        private readonly ILogger<MqttClientService> _logger;

        public MqttClientService(IOptions<MqttConfig> configOptions, ILogger<MqttClientService> logger)
        {
            _config = configOptions.Value;
            _logger = logger;
            _managedMqttClient = new MqttFactory().CreateManagedMqttClient();
            RegisterEventHandlers();
        }

        public bool IsConnected => _managedMqttClient.IsConnected;

        private void RegisterEventHandlers()
        {
            _managedMqttClient.ConnectedAsync += async e =>
            {
                _logger.LogInformation("Successfully connected to EMQX MQTT Broker at {Host}:{Port}", _config.Host, _config.Port);
                await Task.CompletedTask;
            };

            _managedMqttClient.DisconnectedAsync += async e =>
            {
                _logger.LogWarning("Disconnected from EMQX MQTT Broker. Automatic reconnect active.");
                await Task.CompletedTask;
            };

            _managedMqttClient.ConnectingFailedAsync += async e =>
            {
                _logger.LogError(e.Exception, "Connection to EMQX MQTT Broker failed at {Host}:{Port}", _config.Host, _config.Port);
                await Task.CompletedTask;
            };
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var clientOptionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_config.Host, _config.Port)
                .WithClientId($"{_config.ClientId}_{Guid.NewGuid():N}")
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_config.KeepAliveIntervalSeconds))
                .WithCleanSession();

            if (!string.IsNullOrEmpty(_config.Username))
            {
                clientOptionsBuilder.WithCredentials(_config.Username, _config.Password);
            }

            if (_config.UseTls)
            {
                clientOptionsBuilder.WithTlsOptions(o => o.UseTls());
            }

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(_config.ReconnectDelaySeconds))
                .WithClientOptions(clientOptionsBuilder.Build())
                .Build();

            _logger.LogInformation("Starting Managed MQTT Client connecting to EMQX at {Host}:{Port}...", _config.Host, _config.Port);
            await _managedMqttClient.StartAsync(managedOptions);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Managed MQTT Client...");
            await _managedMqttClient.StopAsync();
        }

        public async Task PublishAsync(string topic, string payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce, bool retain = false, CancellationToken cancellationToken = default)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(retain)
                .Build();

            await _managedMqttClient.EnqueueAsync(message);
        }

        public async Task PublishAsync(string topic, byte[] payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce, bool retain = false, CancellationToken cancellationToken = default)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(retain)
                .Build();

            await _managedMqttClient.EnqueueAsync(message);
        }

        public void Dispose()
        {
            _managedMqttClient?.Dispose();
        }
    }
}
