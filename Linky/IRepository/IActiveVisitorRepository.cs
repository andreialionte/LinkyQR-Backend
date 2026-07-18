using Linky.DTOs;

namespace Linky.IRepository
{
    public interface IActiveVisitorRepository
    {
        Task<IList<ActiveVisitorDto>> GetActiveVisitors(CancellationToken cancellationToken = default);
        Task<ActiveVisitorDto?> CreateActiveVisitor(ActiveVisitorDto visitorDto, CancellationToken cancellationToken = default);
        Task DeleteInactiveVisitors(TimeSpan timeSpan, CancellationToken cancellationToken = default);
        Task<int> GetActiveVisitorCount(CancellationToken cancellationToken = default);
    }
}
