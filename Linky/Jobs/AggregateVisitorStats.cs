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
            var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await _statsRepo.AggregateStatsForDate(yesterday);
        }
    }
}
