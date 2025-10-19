using Linky.Models;

namespace Linky.IRepository
{
    public interface IVisitorStatsRepository
    {
        Task AggregateStatsForDate(DateOnly date);
        Task<VisitorStats?> GetStatsForDate(DateOnly date);
        Task<IEnumerable<VisitorStats>> GetStatsRange(DateOnly start, DateOnly end);
    }
}
