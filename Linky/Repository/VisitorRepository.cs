using Linky.DataLayer;
using Linky.DTOs;
using Linky.Mappers;
using Linky.IRepository;
using Linky.IService;
using Linky.Models;
using Microsoft.EntityFrameworkCore;

namespace Linky.Repository
{
    public class VisitorRepository : IVisitorRepository
    {
        private static string GetCacheKey(string sessionId) => $"visitor:{sessionId}";
        private readonly DataContextEf _context;
        private readonly ICacheService _cacheService;

        public VisitorRepository(DataContextEf context, ICacheService cacheService)
        {
            _context = context;
            _cacheService = cacheService;
        }

        private static string GetRecentVisitorsKey(int limit) => $"visitors:recent:{limit}";
        private static string GetVisitorsRangeKey(DateTime start, DateTime end) => $"visitors:range:{start:O}:{end:O}";
        private static string GetTotalVisitsKey(DateTime? start) => start.HasValue ? $"visitors:total:{start.Value:O}" : "visitors:total:all";
        private static string GetUniqueVisitorsKey(DateTime? start) => start.HasValue ? $"visitors:unique:{start.Value:O}" : "visitors:unique:all";

        public async Task AddVisitor(VisitorDto visitor)
        {
            // Check if this IP already visited this path recently (ex: last 30 minutes)
            var recentVisit = await _context.Visitors
                .Where(v => v.Ip == visitor.Ip
                         && v.Path == visitor.Path
                         && v.Timestamp >= DateTime.UtcNow.AddMinutes(-30))
                .AnyAsync();

            if (recentVisit)
            {
                return; // Don't add duplicate visit
            }

            // Insert new visitor (reuse provided Id when available)
            var newVisitor = VisitorMapper.ToModel(visitor);

            _context.Visitors.Add(newVisitor);
            await _context.SaveChangesAsync();
            await _cacheService.RemoveAsync(GetRecentVisitorsKey(100));
            await _cacheService.RemoveAsync(GetRecentVisitorsKey(50));
            await _cacheService.RemoveAsync(GetTotalVisitsKey(null));
            await _cacheService.RemoveAsync(GetUniqueVisitorsKey(null));
            var today = DateTime.UtcNow.Date;
            await _cacheService.RemoveAsync(GetTotalVisitsKey(today));
            await _cacheService.RemoveAsync(GetUniqueVisitorsKey(today));

            // cache inserted visitor for immediate subsequent reads
            var visitorCacheKey = GetCacheKey(newVisitor.Id.ToString());
            await _cacheService.SetAsync(visitorCacheKey, newVisitor, TimeSpan.FromMinutes(10));

            // invalidate visitor stats caches so dashboards reflect recent writes
            await _cacheService.RemoveAsync("visitorstats:today");
            await _cacheService.RemoveAsync("visitorstats:7days");
            await _cacheService.RemoveAsync("visitorstats:30days");

            // ???? also invalidate common visitors range caches covering today / recent windows
            var startToday = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday, now));
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday.AddDays(-6), now));
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday.AddDays(-29), now));

        }

        public async Task<IEnumerable<Visitor>> GetRecentVisitors(int limit = 100)
        {
            var cacheKey = GetRecentVisitorsKey(limit);
            var cached = await _cacheService.GetAsync<IEnumerable<Visitor>>(cacheKey);
            if (cached != null) return cached;

            var list = await _context.Visitors
                .AsNoTracking()
                .OrderByDescending(v => v.Timestamp)
                .Take(limit)
                .ToListAsync();

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromSeconds(30));
            return list;
        }

        public async Task<IEnumerable<Visitor>> GetVisitorsByDateRange(DateTime start, DateTime end)
        {
            var cacheKey = GetVisitorsRangeKey(start, end);
            var cached = await _cacheService.GetAsync<IEnumerable<Visitor>>(cacheKey);
            if (cached != null) return cached;

            var list = await _context.Visitors
                .AsNoTracking()
                .Where(v => v.Timestamp >= start && v.Timestamp <= end)
                .OrderByDescending(v => v.Timestamp)
                .ToListAsync();

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromMinutes(1));
            return list;
        }

        public async Task<int> GetTotalVisits(DateTime? start = null)
        {
            var cacheKey = GetTotalVisitsKey(start);
            var cached = await _cacheService.GetAsync<int?>(cacheKey);
            if (cached.HasValue)
                return cached.Value;

            int count;
            if (start.HasValue)
            {
                count = await _context.Visitors
                    .AsNoTracking()
                    .Where(v => v.Timestamp >= start.Value)
                    .CountAsync();
            }
            else
            {
                count = await _context.Visitors
                    .AsNoTracking()
                    .CountAsync();
            }

            await _cacheService.SetAsync(cacheKey, count, TimeSpan.FromMinutes(1));
            return count;
        }

        public async Task<int> GetUniqueVisitors(DateTime? start = null)
        {
            var cacheKey = GetUniqueVisitorsKey(start);
            var cached = await _cacheService.GetAsync<int?>(cacheKey);
            if (cached.HasValue)
                return cached.Value;

            int count;
            if (start.HasValue)
            {
                count = await _context.Visitors
                    .AsNoTracking()
                    .Where(v => v.Timestamp >= start.Value)
                    .Select(v => v.Ip)
                    .Distinct()
                    .CountAsync();
            }
            else
            {
                count = await _context.Visitors
                    .AsNoTracking()
                    .Select(v => v.Ip)
                    .Distinct()
                    .CountAsync();
            }

            await _cacheService.SetAsync(cacheKey, count, TimeSpan.FromMinutes(1));
            return count;
        }

        public async Task<Visitor?> GetVisitorBySessionId(Guid sessionId)
        {
            string cacheKey = GetCacheKey(sessionId.ToString());
            var cachedVisitor = await _cacheService.GetAsync<Visitor>(cacheKey);

            if (cachedVisitor != null)
                return cachedVisitor;

            var visitor = await _context.Visitors
                .OrderByDescending(v => v.Timestamp)
                .FirstOrDefaultAsync(v => v.Id == sessionId);

            if (visitor != null)
            {
                await _cacheService.SetAsync(cacheKey, visitor, TimeSpan.FromMinutes(10));
            }

            return visitor;
        }
    }
}