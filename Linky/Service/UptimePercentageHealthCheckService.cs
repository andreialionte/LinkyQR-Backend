using Linky.IService;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Linky.Service
{
    public class UptimePercentageHealthCheck : IHealthCheck, IUptimePercentageHealthCheckService
    {
        private static readonly DateTime StartTime = DateTime.UtcNow;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var uptime = DateTime.UtcNow - StartTime;
            var totalSecondsInDay = 24 * 60 * 60;
            var secondsIntoDay = (DateTime.UtcNow.TimeOfDay.TotalSeconds + uptime.TotalSeconds) % totalSecondsInDay;
            var uptimePercentage = (secondsIntoDay / totalSecondsInDay) * 100;

            var data = new Dictionary<string, object?>
        {
            { "uptime_percentage", Math.Round(uptimePercentage, 2) },
            { "uptime_hours", Math.Round(uptime.TotalHours, 2) }
        };

            return Task.FromResult(HealthCheckResult.Healthy(data: data));
        }
    }
}
