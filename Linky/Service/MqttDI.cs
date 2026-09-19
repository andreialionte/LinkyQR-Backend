using Linky.Config;
using Linky.IService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Linky.Service
{
    public static class MqttDI
    {
        public static IServiceCollection AddMqttServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<MqttConfig>(opts =>
            {
                var mqtt = configuration.GetSection("Mqtt");

                opts.Host = FirstRealValue(
                    Env("Mqtt__Host"),
                    Env("MQTT_HOST"),
                    mqtt["Host"]) ?? "localhost";

                if (int.TryParse(FirstRealValue(Env("Mqtt__Port"), Env("MQTT_PORT"), mqtt["Port"]), out var port)
                    && port > 0)
                {
                    opts.Port = port;
                }

                opts.ClientId = FirstRealValue(
                    Env("Mqtt__ClientId"),
                    Env("MQTT_CLIENT_ID"),
                    mqtt["ClientId"]) ?? "LinkyBackendService";

                opts.Username = FirstRealValue(
                    Env("Mqtt__Username"),
                    Env("MQTT_USERNAME"),
                    mqtt["Username"]);

                opts.Password = FirstRealValue(
                    Env("Mqtt__Password"),
                    Env("MQTT_PASSWORD"),
                    mqtt["Password"]);

                if (bool.TryParse(FirstRealValue(Env("Mqtt__UseTls"), Env("MQTT_USE_TLS"), mqtt["UseTls"]), out var useTls))
                {
                    opts.UseTls = useTls;
                }

                if (int.TryParse(FirstRealValue(Env("Mqtt__KeepAliveIntervalSeconds"), Env("MQTT_KEEPALIVE_SECONDS"), mqtt["KeepAliveIntervalSeconds"]), out var keepAlive)
                    && keepAlive > 0)
                {
                    opts.KeepAliveIntervalSeconds = keepAlive;
                }

                if (int.TryParse(FirstRealValue(Env("Mqtt__ReconnectDelaySeconds"), Env("MQTT_RECONNECT_SECONDS"), mqtt["ReconnectDelaySeconds"]), out var reconnect)
                    && reconnect > 0)
                {
                    opts.ReconnectDelaySeconds = reconnect;
                }
            });

            services.AddSingleton<MqttClientService>();
            services.AddSingleton<IMqttClientService>(sp => sp.GetRequiredService<MqttClientService>());
            services.AddHostedService(sp => sp.GetRequiredService<MqttClientService>());

            services.AddSingleton<ICacheBroadcastService, CacheBroadcastService>();
            services.AddSingleton<IEmqxAuthService, EmqxAuthService>();

            return services;
        }

        private static string? Env(string key) => Environment.GetEnvironmentVariable(key);

        private static bool IsPlaceholderValue(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && value.StartsWith("${", StringComparison.Ordinal)
                   && value.EndsWith("}", StringComparison.Ordinal);
        }

        private static string? FirstRealValue(params string?[] values)
        {
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || IsPlaceholderValue(value))
                {
                    continue;
                }

                return value;
            }

            return null;
        }
    }
}
