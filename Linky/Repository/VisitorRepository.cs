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
        private static readonly Func<DataContextEf, int, CancellationToken, Task<List<Visitor>>> _getRecentVisitorsCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx, int limit) =>
                ctx.Visitors.AsNoTracking().OrderByDescending(v => v.Timestamp).Take(limit).ToList());

        private static readonly Func<DataContextEf, DateTime, CancellationToken, Task<int>> _countSinceCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx, DateTime since) =>
                ctx.Visitors.AsNoTracking().Where(v => v.Timestamp >= since).Count());
        private static readonly Func<DataContextEf, CancellationToken, Task<int>> _countAllCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx) => ctx.Visitors.AsNoTracking().Count());

        private static readonly Func<DataContextEf, DateTime, CancellationToken, Task<int>> _uniqueCountSinceCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx, DateTime since) =>
                ctx.Visitors.AsNoTracking().Where(v => v.Timestamp >= since).Select(v => v.Ip).Distinct().Count());

        private static readonly Func<DataContextEf, CancellationToken, Task<int>> _uniqueCountAllCompiled
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

        public async Task AddVisitor(VisitorDto visitor, CancellationToken cancellationToken = default)
        {
            if (visitor.Id != Guid.Empty)
            {
                var existingById = await _context.Visitors.FirstOrDefaultAsync(v => v.Id == visitor.Id, cancellationToken).ConfigureAwait(false);
                if (existingById != null)
                {
                    existingById.Timestamp = visitor.Timestamp;
                    existingById.Path = visitor.Path ?? existingById.Path;
                    existingById.Ip = visitor.Ip ?? existingById.Ip;
                    existingById.Country = visitor.Country ?? existingById.Country;
                    existingById.City = visitor.City ?? existingById.City;
                    existingById.UserAgent = visitor.UserAgent ?? existingById.UserAgent;
                    // Non-idempotent write: do not cancel the actual DB save once we have started.
                    await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

                    var visitorCacheKey = GetCacheKey(existingById.Id.ToString());
                    await _cacheService.SetAsync(visitorCacheKey, existingById, TimeSpan.FromMinutes(10), CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }

            var recentVisit = await _context.Visitors
                .Where(v => v.Ip == visitor.Ip
                         && v.Path == visitor.Path
                         && v.Timestamp >= DateTime.UtcNow.AddMinutes(-30))
                .AnyAsync(cancellationToken).ConfigureAwait(false);

            if (recentVisit)
            {
                return;
            }

            var newVisitor = _mapper.ToModel(visitor);
            _context.Visitors.Add(newVisitor);
            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetRecentVisitorsKey(100), CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetRecentVisitorsKey(50), CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetTotalVisitsKey(null), CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetUniqueVisitorsKey(null), CancellationToken.None).ConfigureAwait(false);
            var today = DateTime.UtcNow.Date;
            await _cacheService.RemoveAsync(GetTotalVisitsKey(today), CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetUniqueVisitorsKey(today), CancellationToken.None).ConfigureAwait(false);
            var visitorCacheKey2 = GetCacheKey(newVisitor.Id.ToString());
            await _cacheService.SetAsync(visitorCacheKey2, newVisitor, TimeSpan.FromMinutes(10), CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync("visitorstats:today", CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync("visitorstats:7days", CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync("visitorstats:30days", CancellationToken.None).ConfigureAwait(false);
            var startToday = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday, now), CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday.AddDays(-6), now), CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync(GetVisitorsRangeKey(startToday.AddDays(-29), now), CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<IEnumerable<Visitor>> GetRecentVisitors(int limit = 100, CancellationToken cancellationToken = default)
        {
            var cacheKey = GetRecentVisitorsKey(limit);
            var cached = await _cacheService.GetAsync<IEnumerable<Visitor>>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached != null) return cached;
            var list = await _getRecentVisitorsCompiled(_context, limit, cancellationToken).ConfigureAwait(false);

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            return list;
        }

        public async Task<IEnumerable<Visitor>> GetVisitorsByDateRange(DateTime start, DateTime end, CancellationToken cancellationToken = default)
        {
            var cacheKey = GetVisitorsRangeKey(start, end);
            var cached = await _cacheService.GetAsync<IEnumerable<Visitor>>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached != null) return cached;

            var list = await _context.Visitors
                .AsNoTracking()
                .Where(v => v.Timestamp >= start && v.Timestamp <= end)
                .OrderByDescending(v => v.Timestamp)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            return list;
        }

        public async Task<int> GetTotalVisits(DateTime? start = null, CancellationToken cancellationToken = default)
        {
            var cacheKey = GetTotalVisitsKey(start);
            var cached = await _cacheService.GetAsync<int?>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached.HasValue)
                return cached.Value;

            int count;
            if (start.HasValue)
            {
                count = await _countSinceCompiled(_context, start.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                count = await _countAllCompiled(_context, cancellationToken).ConfigureAwait(false);
            }

            await _cacheService.SetAsync(cacheKey, count, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            return count;
        }

        public async Task<int> GetUniqueVisitors(DateTime? start = null, CancellationToken cancellationToken = default)
        {
            var cacheKey = GetUniqueVisitorsKey(start);
            var cached = await _cacheService.GetAsync<int?>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached.HasValue)
                return cached.Value;

            int count;
            if (start.HasValue)
            {
                count = await _uniqueCountSinceCompiled(_context, start.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                count = await _uniqueCountAllCompiled(_context, cancellationToken).ConfigureAwait(false);
            }

            await _cacheService.SetAsync(cacheKey, count, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            return count;
        }

        public async Task<Visitor?> GetVisitorBySessionId(Guid sessionId, CancellationToken cancellationToken = default)
        {
            string cacheKey = GetCacheKey(sessionId.ToString());
            var cachedVisitor = await _cacheService.GetAsync<Visitor>(cacheKey, cancellationToken).ConfigureAwait(false);

            if (cachedVisitor != null)
                return cachedVisitor;

            var visitor = await _context.Visitors
                .OrderByDescending(v => v.Timestamp)
                .FirstOrDefaultAsync(v => v.Id == sessionId, cancellationToken).ConfigureAwait(false);

            if (visitor != null)
            {
                await _cacheService.SetAsync(cacheKey, visitor, TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
            }

            return visitor;
        }
    }
}