using Linky.DataLayer;
using Linky.DTOs;
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

        public async Task AddVisitor(VisitorDto visitor)
        {
            // Check if this sessionId already exists
            var exists = await _context.Visitors
                .AnyAsync(v => v.SessionId == visitor.SessionId);

            if (exists)
            {
                return;
            }

            // Insert new visitor
            var newVisitor = new Visitor
            {
                Id = Guid.NewGuid(),
                Path = visitor.Path,
                Timestamp = visitor.Timestamp,
                Ip = visitor.Ip,
                Country = visitor.Country,
                City = visitor.City,
                Referer = visitor.Referer,
                IsUnique = visitor.IsUnique,
                UserAgent = visitor.UserAgent,
                SessionId = visitor.SessionId
            };

            _context.Visitors.Add(newVisitor);

            // Add VisitorStats entry for today if it doesn't exist
            var today = DateOnly.FromDateTime(visitor.Timestamp);
            var statsExists = await _context.VisitorStats
                .AnyAsync(s => s.Date == today);

            if (!statsExists)
            {
                var newStats = new VisitorStats
                {
                    Id = Guid.NewGuid(),
                    Date = today,
                    TotalVisits = 0,
                    UniqueVisitors = 0,
                    TopPages = new Dictionary<string, int>(),
                    TopCountries = new Dictionary<string, int>()
                };

                _context.VisitorStats.Add(newStats);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<Visitor>> GetRecentVisitors(int limit = 100)
        {
            return await _context.Visitors
                .OrderByDescending(v => v.Timestamp)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<IEnumerable<Visitor>> GetVisitorsByDateRange(DateTime start, DateTime end)
        {
            return await _context.Visitors
                .Where(v => v.Timestamp >= start && v.Timestamp <= end)
                .OrderByDescending(v => v.Timestamp)
                .ToListAsync();
        }

        public async Task<int> GetTotalVisits(DateTime? start = null)
        {
            if (start.HasValue)
            {
                return await _context.Visitors
                    .Where(v => v.Timestamp >= start.Value)
                    .CountAsync();
            }

            return await _context.Visitors.CountAsync();
        }

        public async Task<int> GetUniqueVisitors(DateTime? start = null)
        {
            if (start.HasValue)
            {
                return await _context.Visitors
                    .Where(v => v.Timestamp >= start.Value)
                    .Select(v => v.Ip)
                    .Distinct()
                    .CountAsync();
            }

            return await _context.Visitors
                .Select(v => v.Ip)
                .Distinct()
                .CountAsync();
        }

        public async Task<Visitor?> GetVisitorBySessionId(Guid sessionId)
        {
            string cacheKey = GetCacheKey(sessionId.ToString());

            var cachedVisitor = await _cacheService.GetAsync<Visitor>(cacheKey);
            if (cachedVisitor != null)
                return cachedVisitor;

            var visitor = await _context.Visitors
                .FirstOrDefaultAsync(v => v.SessionId == sessionId);

            if (visitor != null)
            {
                await _cacheService.SetAsync(cacheKey, visitor, TimeSpan.FromMinutes(10));
            }

            return visitor;
        }
    }
}