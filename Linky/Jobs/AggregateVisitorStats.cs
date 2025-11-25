using Linky.IRepository;
using Linky.IService;
using Quartz;

namespace Linky.Jobs
{
    public class AggregateVisitorStats : IJob
    {
        private readonly IVisitorStatsRepository _statsRepo;
        private readonly IGeoIPService _geoIpService;

        public AggregateVisitorStats(IVisitorStatsRepository statsRepo, IGeoIPService geoIpService)
        {
            _statsRepo = statsRepo;
            _geoIpService = geoIpService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            // aggregate the 24h window from previous 06:00 UTC to current 06:00 UTC
            var endUtc = DateTime.UtcNow.Date.AddHours(6);
            if (DateTime.UtcNow < endUtc)
            {
                // if current time is before today's 06:00, the intended end is today 06:00 (still),
                // but to be safe compute end as today's 06:00
                endUtc = DateTime.UtcNow.Date.AddHours(6);
            }

            var startUtc = endUtc.AddDays(-1);

            await _statsRepo.AggregateStatsForRange(startUtc, endUtc);
        }
    }
}
