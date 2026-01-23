using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Linky.Models;
using Microsoft.EntityFrameworkCore;

namespace Linky.Repository
{
    public class ActiveVisitorRepository : IActiveVisitorRepository
    {
        private readonly DataContextEf _context;
        private readonly ICacheService _cacheService;

        private const string VISITOR_COUNT_KEY = "active_visitor_count";
        private const string VISITORS_LIST_KEY = "active_visitors_list";

        public ActiveVisitorRepository(DataContextEf context, ICacheService cacheService)
        {
            _context = context;
            _cacheService = cacheService;
        }

        public async Task<IList<ActiveVisitorDto>> GetActiveVisitors()
        {
            var cached = await _cacheService.GetAsync<List<ActiveVisitorDto>>(VISITORS_LIST_KEY);
            if (cached != null && cached.Count > 0)
                return cached;

            var result = await _context.ActiveVisitors.ToListAsync();

            if (result == null || result.Count == 0)
            {
                throw new Exception("No active visitors found");
            }

            var visitors = result.Select(ActiveVisitorMapping.ToDto).ToList();
            await _cacheService.SetAsync(VISITORS_LIST_KEY, visitors, TimeSpan.FromSeconds(41));

            return visitors;
        }

        public async Task<ActiveVisitorDto?> CreateActiveVisitor(ActiveVisitorDto visitorDto)
        {
            if (visitorDto.SessionId == Guid.Empty)
                throw new Exception("SessionId cannot be empty.");

            // Check if this SessionId already exists
            var existing = await _context.ActiveVisitors
                .FirstOrDefaultAsync(a => a.SessionId == visitorDto.SessionId);

            if (existing != null)
            {
                // SessionId exists → just UPDATE
                existing.LastSeenUtc = visitorDto.LastSeenUtc;
                existing.CurrentPath = visitorDto.CurrentPath;
                existing.UserAgent = visitorDto.UserAgent;
                existing.Ip = visitorDto.Ip;

                _context.ActiveVisitors.Update(existing);
                await _context.SaveChangesAsync();

                // update may affect cached list/order invalidate caches
                await _cacheService.RemoveAsync(VISITORS_LIST_KEY);
                await _cacheService.RemoveAsync(VISITOR_COUNT_KEY);

                return ActiveVisitorMapping.ToDto(existing);
            }
            else
            {
                // New SessionId → INSERT
                var newVisitor = ActiveVisitorMapping.ToModel(visitorDto);

                _context.ActiveVisitors.Add(newVisitor);
                await _context.SaveChangesAsync();

                // Invalidate cache only on new INSERT
                await _cacheService.RemoveAsync(VISITOR_COUNT_KEY);
                await _cacheService.RemoveAsync(VISITORS_LIST_KEY);

                return ActiveVisitorMapping.ToDto(newVisitor);
            }
        }

        public async Task DeleteInactiveVisitors(TimeSpan timeSpan)
        {
            var cutoffTime = DateTime.UtcNow - timeSpan;

            var deletedCount = await _context.ActiveVisitors
                .Where(a => a.LastSeenUtc < cutoffTime)
                .ExecuteDeleteAsync();

            if (deletedCount > 0)
            {
                await _cacheService.RemoveAsync(VISITOR_COUNT_KEY);
                await _cacheService.RemoveAsync(VISITORS_LIST_KEY);
            }
        }

        public async Task<int> GetActiveVisitorCount()
        {
            var cachedNullable = await _cacheService.GetAsync<int?>(VISITOR_COUNT_KEY);
            if (cachedNullable.HasValue)
                return cachedNullable.Value;

            var count = await _context.ActiveVisitors.CountAsync();

            await _cacheService.SetAsync(VISITOR_COUNT_KEY, count, TimeSpan.FromSeconds(41));

            return count;
        }
    }
}