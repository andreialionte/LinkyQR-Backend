using Linky.DTOs;
using Linky.Models;

namespace Linky.IRepository
{
    public interface IVisitorRepository
    {
        Task AddVisitor(VisitorDto visitor);
        Task<IEnumerable<Visitor>> GetRecentVisitors(int limit = 100);
        Task<IEnumerable<Visitor>> GetVisitorsByDateRange(DateTime start, DateTime end);
        Task<int> GetTotalVisits(DateTime? start = null);
        Task<int> GetUniqueVisitors(DateTime? start = null);
        Task<Visitor?> GetVisitorBySessionId(Guid sessionId);
    }
}
