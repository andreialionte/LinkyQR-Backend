using Linky.IRepository;
using TickerQ.Utilities.Base;


namespace Linky.Jobs
{
    public class AggregateVisitorStats
    {
        private readonly IVisitorStatsRepository _statsRepo;

        public AggregateVisitorStats(IVisitorStatsRepository statsRepo)
        {
            _statsRepo = statsRepo;
        }

        [TickerFunction("AggregateVisitorStatsJob")]
        public async Task Execute(TickerFunctionContext context, CancellationToken cancellationToken)
        {

            // Aggregate the 24h window that ENDED at 06:00 UTC
            // Example: if it's 2026-03-19 08:00 UTC, aggregate 2026-03-18 06:00 to 2026-03-19 06:00
            // Example: if it's 2026-03-19 04:00 UTC, aggregate 2026-03-17 06:00 to 2026-03-18 06:00
            
            var nowUtc = DateTime.UtcNow;
            var todayAt6Utc = nowUtc.Date.AddHours(6);
            
            DateTime endUtc;
            if (nowUtc >= todayAt6Utc)
            {
                // Current time is after today's 06:00 UTC
                // Aggregate yesterday's 06:00 to today's 06:00
                endUtc = todayAt6Utc;
            }
            else
            {
                // Current time is before today's 06:00 UTC
                // Aggregate day-before-yesterday's 06:00 to yesterday's 06:00
                endUtc = todayAt6Utc.AddDays(-1);
            }

            var startUtc = endUtc.AddDays(-1);

            await _statsRepo.AggregateStatsForRange(startUtc, endUtc, cancellationToken).ConfigureAwait(false);
        }
    }
}
