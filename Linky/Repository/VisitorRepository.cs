using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Linky.Models;
using Microsoft.EntityFrameworkCore;

namespace Linky.Repository
{
    public class VisitorRepository : IVisitorRepository
    {
        // Compiled EF Core queries for hot paths
        private static readonly Func<DataContextEf, int, Task<List<Visitor>>> _getRecentVisitorsCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx, int limit) =>
                ctx.Visitors.AsNoTracking().OrderByDescending(v => v.Timestamp).Take(limit).ToList());

        private static readonly Func<DataContextEf, DateTime, Task<int>> _countSinceCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx, DateTime since) =>
                ctx.Visitors.AsNoTracking().Where(v => v.Timestamp >= since).Count());
        private static readonly Func<DataContextEf, Task<int>> _countAllCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx) => ctx.Visitors.AsNoTracking().Count());

        private static readonly Func<DataContextEf, DateTime, Task<int>> _uniqueCountSinceCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx, DateTime since) =>
                ctx.Visitors.AsNoTracking().Where(v => v.Timestamp >= since).Select(v => v.Ip).Distinct().Count());

        private static readonly Func<DataContextEf, Task<int>> _uniqueCountAllCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx) => ctx.Visitors.AsNoTracking().Select(v => v.Ip).Distinct().Count());

        private static string GetCacheKey(string sessionId) => $"visitor:{sessionId}";
        private readonly DataContextEf _context;
        private readonly ICacheService _cacheService;
        private readonly VisitorMapper _mapper;

        public VisitorRepository(DataContextEf context, ICacheService cacheService, VisitorMapper mapper)
        {
            _context = context;
            _cacheService = cacheService;
            _mapper = mapper;
        }

        private static string GetRecentVisitorsKey(int limit) => $"visitors:recent:{limit}";
        private static string GetVisitorsRangeKey(DateTime start, DateTime end) => $"visitors:range:{start:O}:{end:O}";
        private static string GetTotalVisitsKey(DateTime? start) => start.HasValue ? $"visitors:total:{start.Value:O}" : "visitors:total:all";
        private static string GetUniqueVisitorsKey(DateTime? start) => start.HasValue ? $"visitors:unique:{start.Value:O}" : "visitors:unique:all";

        public async Task AddVisitor(VisitorDto visitor)
        {
            if (visitor.Id != Guid.Empty)
            {
                var existingById = await _context.Visitors.FirstOrDefaultAsync(v => v.Id == visitor.Id).ConfigureAwait(false);
                if (existingById != null)
                {
                    existingById.Timestamp = visitor.Timestamp;
                    existingById.Path = visitor.Path ?? existingById.Path;
                    existingById.Ip = visitor.Ip ?? existingById.Ip;
                    existingById.Country = visitor.Country ?? existingById.Country;
                    existingById.City = visitor.City ?? existingById.City;
                    existingById.UserAgent = visitor.UserAgent ?? existingById.UserAgent;
                    await _context.SaveChangesAsync();

                    var visitorCacheKey = GetCacheKey(existingById.Id.ToString());
                    await _cacheService.SetAsync(visitorCacheKey, existingById, TimeSpan.FromMinutes(10));
                    return;
                }
            }

            var recentVisit = await _context.Visitors
                .Where(v => v.Ip == visitor.Ip
                         && v.Path == visitor.Path
                         && v.Timestamp >= DateTime.UtcNow.AddMinutes(-30))
                .AnyAsync().ConfigureAwait(false);

            if (recentVisit)
            {
                return;
            }

            var newVisitor = _mapper.ToModel(visitor);
            _context.Visitors.Add(newVisitor);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetRecentVisitorsKey(100)).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetRecentVisitorsKey(50)).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetTotalVisitsKey(null)).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetUniqueVisitorsKey(null)).ConfigureAwait(false);
            var today = DateTime.UtcNow.Date;
            await _cacheService.RemoveAsync(GetTotalVisitsKey(today)).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetUniqueVisitorsKey(today)).ConfigureAwait(false);
            var visitorCacheKey2 = GetCacheKey(newVisitor.Id.ToString());
            await _cacheService.SetAsync(visitorCacheKey2, newVisitor, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            await _cacheService.RemoveAsync("visitorstats:today").ConfigureAwait(false);
            await _cacheService.RemoveAsync("visitorstats:7days").ConfigureAwait(false);
            await _cacheService.RemoveAsync("visitorstats:30days").ConfigureAwait(false);
            var startToday = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday, now)).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday.AddDays(-6), now)).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday.AddDays(-29), now)).ConfigureAwait(false);
        }

        public async Task<IEnumerable<Visitor>> GetRecentVisitors(int limit = 100)
        {
            var cacheKey = GetRecentVisitorsKey(limit);
            var cached = await _cacheService.GetAsync<IEnumerable<Visitor>>(cacheKey).ConfigureAwait(false);
            if (cached != null) return cached;
            var list = await _getRecentVisitorsCompiled(_context, limit).ConfigureAwait(false);

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            return list;
        }

        public async Task<IEnumerable<Visitor>> GetVisitorsByDateRange(DateTime start, DateTime end)
        {
            var cacheKey = GetVisitorsRangeKey(start, end);
            var cached = await _cacheService.GetAsync<IEnumerable<Visitor>>(cacheKey).ConfigureAwait(false);
            if (cached != null) return cached;

            var list = await _context.Visitors
                .AsNoTracking()
                .Where(v => v.Timestamp >= start && v.Timestamp <= end)
                .OrderByDescending(v => v.Timestamp)
                .ToListAsync().ConfigureAwait(false);

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            return list;
        }

        public async Task<int> GetTotalVisits(DateTime? start = null)
        {
            var cacheKey = GetTotalVisitsKey(start);
            var cached = await _cacheService.GetAsync<int?>(cacheKey).ConfigureAwait(false);
            if (cached.HasValue)
                return cached.Value;

            int count;
            if (start.HasValue)
            {
                count = await _countSinceCompiled(_context, start.Value).ConfigureAwait(false);
            }
            else
            {
                count = await _countAllCompiled(_context).ConfigureAwait(false);
            }

            await _cacheService.SetAsync(cacheKey, count, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            return count;
        }

        public async Task<int> GetUniqueVisitors(DateTime? start = null)
        {
            var cacheKey = GetUniqueVisitorsKey(start);
            var cached = await _cacheService.GetAsync<int?>(cacheKey).ConfigureAwait(false);
            if (cached.HasValue)
                return cached.Value;

            int count;
            if (start.HasValue)
            {
                count = await _uniqueCountSinceCompiled(_context, start.Value).ConfigureAwait(false);
            }
            else
            {
                count = await _uniqueCountAllCompiled(_context).ConfigureAwait(false);
            }

            await _cacheService.SetAsync(cacheKey, count, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            return count;
        }

        public async Task<Visitor?> GetVisitorBySessionId(Guid sessionId)
        {
            string cacheKey = GetCacheKey(sessionId.ToString());
            var cachedVisitor = await _cacheService.GetAsync<Visitor>(cacheKey).ConfigureAwait(false);

            if (cachedVisitor != null)
                return cachedVisitor;

            var visitor = await _context.Visitors
                .OrderByDescending(v => v.Timestamp)
                .FirstOrDefaultAsync(v => v.Id == sessionId).ConfigureAwait(false);

            if (visitor != null)
            {
                await _cacheService.SetAsync(cacheKey, visitor, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            }

            return visitor;
        }
    }
}