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
                configuration.GetSection("Mqtt").Bind(opts);

                opts.Host = FirstRealValue(
                    Env("Mqtt__Host"),
                    Env("MQTT_HOST"),
                    opts.Host) ?? "localhost";

                if (int.TryParse(FirstRealValue(Env("Mqtt__Port"), Env("MQTT_PORT"), opts.Port.ToString()), out var port)
                    && port > 0)
                {
                    opts.Port = port;
                }
                else
                {
                    opts.Port = 1883;
                }

                opts.ClientId = FirstRealValue(
                    Env("Mqtt__ClientId"),
                    Env("MQTT_CLIENT_ID"),
                    opts.ClientId) ?? "LinkyBackendService";

                opts.Username = FirstRealValue(
                    Env("Mqtt__Username"),
                    Env("MQTT_USERNAME"),
                    opts.Username);

                opts.Password = FirstRealValue(
                    Env("Mqtt__Password"),
                    Env("MQTT_PASSWORD"),
                    opts.Password);

                if (bool.TryParse(FirstRealValue(Env("Mqtt__UseTls"), Env("MQTT_USE_TLS")), out var useTls))
                {
                    opts.UseTls = useTls;
                }

                if (int.TryParse(FirstRealValue(Env("Mqtt__KeepAliveIntervalSeconds"), Env("MQTT_KEEPALIVE_SECONDS")), out var keepAlive)
                    && keepAlive > 0)
                {
                    opts.KeepAliveIntervalSeconds = keepAlive;
                }

                if (int.TryParse(FirstRealValue(Env("Mqtt__ReconnectDelaySeconds"), Env("MQTT_RECONNECT_SECONDS")), out var reconnect)
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
                if (string.IsNullOrWhiteSpace(value) || IsPlaceholderValue(value) || value == "0")
                {
                    continue;
                }

                return value;
            }

            return null;
        }
    }
}
