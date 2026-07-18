using Linky.DTOs;
using Linky.Models;

namespace Linky.IRepository
{
    public interface IVisitorRepository
    {
        Task AddVisitor(VisitorDto visitor, CancellationToken cancellationToken = default);
        Task<IEnumerable<Visitor>> GetRecentVisitors(int limit = 100, CancellationToken cancellationToken = default);
        Task<IEnumerable<Visitor>> GetVisitorsByDateRange(DateTime start, DateTime end, CancellationToken cancellationToken = default);
        Task<int> GetTotalVisits(DateTime? start = null, CancellationToken cancellationToken = default);
        Task<int> GetUniqueVisitors(DateTime? start = null, CancellationToken cancellationToken = default);
        Task<Visitor?> GetVisitorBySessionId(Guid sessionId, CancellationToken cancellationToken = default);
    }
}
