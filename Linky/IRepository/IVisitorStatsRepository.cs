using Linky.Models;

namespace Linky.IRepository
{
    public interface IVisitorStatsRepository
    {
        Task AggregateStatsForDate(DateOnly date, CancellationToken cancellationToken = default);
        Task AggregateStatsForRange(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
        Task<VisitorStats?> GetStatsForDate(DateOnly date, CancellationToken cancellationToken = default);
        Task<IEnumerable<VisitorStats>> GetStatsRange(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);
        Task<(int TotalVisits, int UniqueVisitors, Dictionary<string, int> TopPages)> GetWeeklyStats(DateOnly endDate, CancellationToken cancellationToken = default);
        Task<(int TotalVisits, int UniqueVisitors, Dictionary<string, int> TopPages)> GetMonthlyStats(DateOnly endDate, CancellationToken cancellationToken = default);
    }
}
