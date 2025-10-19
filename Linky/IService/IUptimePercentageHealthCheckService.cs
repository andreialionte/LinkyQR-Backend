using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Linky.IService
{
    public interface IUptimePercentageHealthCheckService
    {
        Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default);
    }
}
