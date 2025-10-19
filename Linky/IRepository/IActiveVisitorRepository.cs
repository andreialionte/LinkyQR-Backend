using Linky.DTOs;

namespace Linky.IRepository
{
    public interface IActiveVisitorRepository
    {
        Task<IList<ActiveVisitorDto>> GetActiveVisitors();
        Task<ActiveVisitorDto?> CreateActiveVisitor(ActiveVisitorDto visitorDto);
        Task DeleteInactiveVisitors(TimeSpan timeSpan);
        Task<int> GetActiveVisitorCount();
    }
}
