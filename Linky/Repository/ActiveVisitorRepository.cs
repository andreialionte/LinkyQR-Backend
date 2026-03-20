using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Linky.Repository
{
    public class ActiveVisitorRepository : IActiveVisitorRepository
    {
        private readonly DataContextEf _context;
        private readonly ICacheService _cacheService;
        private readonly ActiveVisitorMapper _mapper;

        private const string VISITOR_COUNT_KEY = "active_visitor_count";
        private const string VISITORS_LIST_KEY = "active_visitors_list";

        public ActiveVisitorRepository(DataContextEf context, ICacheService cacheService, ActiveVisitorMapper mapper)
        {
            _context = context;
            _cacheService = cacheService;
            _mapper = mapper;
        }

        public async Task<IList<ActiveVisitorDto>> GetActiveVisitors()
        {
            // Always query fresh from DB for active visitors (don't use stale cache)
            // Active visitors list changes frequently - cache timeout is too risky
            var result = await _context.ActiveVisitors
                .Where(v => v.LastSeenUtc > DateTime.UtcNow.AddMinutes(-5))  // Only within last 5 minutes
                .ToListAsync();

            if (result == null || result.Count == 0)
            {
                return new List<ActiveVisitorDto>();  // Return empty list instead of throwing
            }

            var visitors = result.Select(_mapper.ToDto).ToList();
            
            // Cache only for 5 seconds (fast-moving data)
            await _cacheService.SetAsync(VISITORS_LIST_KEY, visitors, TimeSpan.FromSeconds(5));

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
                // SessionId exists - just UPDATE
                existing.LastSeenUtc = visitorDto.LastSeenUtc;
                existing.CurrentPath = visitorDto.CurrentPath;
                existing.UserAgent = visitorDto.UserAgent;
                existing.Ip = visitorDto.Ip;

                _context.ActiveVisitors.Update(existing);
                await _context.SaveChangesAsync();

                // update may affect cached list/order invalidate caches
                await _cacheService.RemoveAsync(VISITORS_LIST_KEY);
                await _cacheService.RemoveAsync(VISITOR_COUNT_KEY);

                return _mapper.ToDto(existing);
            }
            else
            {
                // New SessionId - INSERT
                var newVisitor = _mapper.ToModel(visitorDto);

                _context.ActiveVisitors.Add(newVisitor);
                await _context.SaveChangesAsync();

                // Invalidate cache only on new INSERT
                await _cacheService.RemoveAsync(VISITOR_COUNT_KEY);
                await _cacheService.RemoveAsync(VISITORS_LIST_KEY);

                return _mapper.ToDto(newVisitor);
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