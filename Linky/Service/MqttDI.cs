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
            // Bind MQTT options from appsettings section "Mqtt"
            services.Configure<MqttConfig>(configuration.GetSection("Mqtt"));

            // Register MqttClientService as Singleton and HostedService
            services.AddSingleton<MqttClientService>();
            services.AddSingleton<IMqttClientService>(sp => sp.GetRequiredService<MqttClientService>());
            services.AddHostedService(sp => sp.GetRequiredService<MqttClientService>());

            // Register CacheBroadcastService & EmqxAuthService
            services.AddSingleton<ICacheBroadcastService, CacheBroadcastService>();
            services.AddSingleton<IEmqxAuthService, EmqxAuthService>();

            return services;
        }
    }
}

