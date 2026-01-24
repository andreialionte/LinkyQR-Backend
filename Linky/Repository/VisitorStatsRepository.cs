using Linky.DataLayer;
using Linky.IRepository;
using Linky.Models;
using Linky.Mappers;
using Linky.IService;
using Microsoft.EntityFrameworkCore;

namespace Linky.Repository
{
    public class VisitorStatsRepository : IVisitorStatsRepository
    {
        private readonly DataContextEf _context;
        private readonly ICacheService _cacheService;

        public VisitorStatsRepository(DataContextEf context, ICacheService cacheService)
        {
            _context = context;
            _cacheService = cacheService;
        }

        public async Task AggregateStatsForDate(DateOnly date)
        {
            // build DateTime range for filtering visitor timestamps
            var startDateTime = date.ToDateTime(TimeOnly.MinValue);
            var endDateTime = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

            // query visitors for the day (no change tracking for heavy scans)
            var visitorsQuery = _context.Visitors
                .AsNoTracking()
                .Where(v => v.Timestamp >= startDateTime && v.Timestamp < endDateTime);

            var totalVisits = await visitorsQuery.CountAsync();
            var uniqueVisitors = await visitorsQuery
                .Select(v => v.Ip)
                .Distinct()
                .CountAsync();

            // top pages
            var pages = await visitorsQuery
                .GroupBy(v => v.Path)
                .Select(g => new { Path = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            var topPages = pages
                .Where(p => p.Path != null)
                .ToDictionary(p => p.Path!, p => p.Count);

            // top countries (exclude null)
            var countries = await visitorsQuery
                .Where(v => v.Country != null)
                .GroupBy(v => v.Country)
                .Select(g => new { Country = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            var topCountries = countries
                .Where(c => c.Country != null)
                .ToDictionary(c => c.Country!, c => c.Count);

            // find existing stats by DateOnly (assumes VisitorStats.Date is DateOnly)
            var existing = await _context.VisitorStats
                .FirstOrDefaultAsync(s => s.Date == date);

            if (existing != null)
            {
                existing.TotalVisits = totalVisits;
                existing.UniqueVisitors = uniqueVisitors;

                // assign dictionaries directly (assumes entity properties are Dictionary<string,int>)
                existing.TopPages = topPages;
                existing.TopCountries = topCountries;

                _context.VisitorStats.Update(existing);
            }
            else
            {
                var dto = new DTOs.VisitorStatsDto(date, totalVisits, uniqueVisitors, topPages, topCountries);
                var newStats = VisitorStatsMapper.ToModel(new DTOs.VisitorStatsDto(dto.Date, dto.TotalVisits, dto.UniqueVisitors, dto.TopPages, dto.TopCountries));
                await _context.VisitorStats.AddAsync(newStats);
            }

            await _context.SaveChangesAsync();

            // invalidate visitor stats caches for affected ranges so controllers serve fresh data
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (date == today)
            {
                await _cacheService.RemoveAsync("visitorstats:today");
            }
            if (date >= today.AddDays(-6))
            {
                await _cacheService.RemoveAsync("visitorstats:7days");
            }
            if (date >= today.AddDays(-29))
            {
                await _cacheService.RemoveAsync("visitorstats:30days");
            }
        }

        public async Task AggregateStatsForRange(DateTime startUtc, DateTime endUtc)
        {
            var visitorsQuery = _context.Visitors
                .AsNoTracking()
                .Where(v => v.Timestamp >= startUtc && v.Timestamp < endUtc);

            var totalVisits = await visitorsQuery.CountAsync();
            var uniqueVisitors = await visitorsQuery
                .Select(v => v.Ip)
                .Distinct()
                .CountAsync();

            var pages = await visitorsQuery
                .GroupBy(v => v.Path)
                .Select(g => new { Path = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            var topPages = pages
                .Where(p => p.Path != null)
                .ToDictionary(p => p.Path!, p => p.Count);

            var countries = await visitorsQuery
                .Where(v => v.Country != null)
                .GroupBy(v => v.Country)
                .Select(g => new { Country = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            var topCountries = countries
                .Where(c => c.Country != null)
                .ToDictionary(c => c.Country!, c => c.Count);

            var dateKey = DateOnly.FromDateTime(startUtc.Date);

            var existing = await _context.VisitorStats
                .FirstOrDefaultAsync(s => s.Date == dateKey);

            if (existing != null)
            {
                existing.TotalVisits = totalVisits;
                existing.UniqueVisitors = uniqueVisitors;
                existing.TopPages = topPages;
                existing.TopCountries = topCountries;

                _context.VisitorStats.Update(existing);
            }
            else
            {
                var dto = new DTOs.VisitorStatsDto(dateKey, totalVisits, uniqueVisitors, topPages, topCountries);
                var newStats = VisitorStatsMapper.ToModel(new DTOs.VisitorStatsDto(dto.Date, dto.TotalVisits, dto.UniqueVisitors, dto.TopPages, dto.TopCountries));
                await _context.VisitorStats.AddAsync(newStats);
            }

            await _context.SaveChangesAsync();

            // invalidate visitor stats caches for affected ranges so controllers serve fresh data
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (dateKey == today)
            {
                await _cacheService.RemoveAsync("visitorstats:today");
            }
            if (dateKey >= today.AddDays(-6))
            {
                await _cacheService.RemoveAsync("visitorstats:7days");
            }
            if (dateKey >= today.AddDays(-29))
            {
                await _cacheService.RemoveAsync("visitorstats:30days");
            }
        }

        public async Task<VisitorStats?> GetStatsForDate(DateOnly date)
        {
            // query by DateOnly and return the entity directly
            var result = await _context.VisitorStats
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Date == date);

            return result;
        }

        public async Task<IEnumerable<VisitorStats>> GetStatsRange(DateOnly start, DateOnly end)
        {
            // compare DateOnly to DateOnly (assumes VisitorStats.Date is DateOnly)
            var results = await _context.VisitorStats
                .AsNoTracking()
                .Where(s => s.Date >= start && s.Date <= end)
                .OrderByDescending(s => s.Date)
                .ToListAsync();

            return results;
        }
    }
}
