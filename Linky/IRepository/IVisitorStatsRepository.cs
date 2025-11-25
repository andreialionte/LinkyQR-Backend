using Linky.Models;

namespace Linky.IRepository
{
    public interface IVisitorStatsRepository
    {
        Task AggregateStatsForDate(DateOnly date);
        Task AggregateStatsForRange(DateTime startUtc, DateTime endUtc);
        Task<VisitorStats?> GetStatsForDate(DateOnly date);
        Task<IEnumerable<VisitorStats>> GetStatsRange(DateOnly start, DateOnly end);
    }
}
