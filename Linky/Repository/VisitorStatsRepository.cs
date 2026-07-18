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
        private readonly VisitorStatsMapper _mapper;

        public VisitorStatsRepository(DataContextEf context, ICacheService cacheService, VisitorStatsMapper mapper)
        {
            _context = context;
            _cacheService = cacheService;
            _mapper = mapper;
        }

        public async Task AggregateStatsForDate(DateOnly date, CancellationToken cancellationToken = default)
        {
            // build DateTime range for filtering visitor timestamps
            var startDateTime = date.ToDateTime(TimeOnly.MinValue);
            var endDateTime = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

            // query visitors for the day (no change tracking for heavy scans)
            var visitorsQuery = _context.Visitors
                .AsNoTracking()
                .Where(v => v.Timestamp >= startDateTime && v.Timestamp < endDateTime);

            var totalVisits = await visitorsQuery.CountAsync(cancellationToken).ConfigureAwait(false);
            var uniqueVisitors = await visitorsQuery
                .Select(v => v.Ip)
                .Distinct()
                .CountAsync(cancellationToken).ConfigureAwait(false);

            // top pages
            var pages = await visitorsQuery
                .GroupBy(v => v.Path)
                .Select(g => new { Path = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

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
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var topCountries = countries
                .Where(c => c.Country != null)
                .ToDictionary(c => c.Country!, c => c.Count);

            // find existing stats by DateOnly (assumes VisitorStats.Date is DateOnly)
            var existing = await _context.VisitorStats
                .FirstOrDefaultAsync(s => s.Date == date, cancellationToken)
                .ConfigureAwait(false);

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
                var newStats = _mapper.ToModel(new DTOs.VisitorStatsDto(dto.Date, dto.TotalVisits, dto.UniqueVisitors, dto.TopPages, dto.TopCountries));
                await _context.VisitorStats.AddAsync(newStats, cancellationToken).ConfigureAwait(false);
            }

            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // Post-write cache invalidation: use CancellationToken.None so cleanup always completes
            // even if the background job is being cancelled.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (date == today)
            {
                await _cacheService.RemoveAsync("visitorstats:today", CancellationToken.None).ConfigureAwait(false);
            }
            if (date >= today.AddDays(-6))
            {
                await _cacheService.RemoveAsync("visitorstats:7days", CancellationToken.None).ConfigureAwait(false);
            }
            if (date >= today.AddDays(-29))
            {
                await _cacheService.RemoveAsync("visitorstats:30days", CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async Task AggregateStatsForRange(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        {
            // Get all visitors in the time range
            var visitorsQuery = _context.Visitors
                .AsNoTracking()
                .Where(v => v.Timestamp >= startUtc && v.Timestamp < endUtc);

            var totalVisits = await visitorsQuery.CountAsync(cancellationToken).ConfigureAwait(false);
            var uniqueVisitors = await visitorsQuery
                .Select(v => v.Ip)
                .Distinct()
                .CountAsync(cancellationToken).ConfigureAwait(false);

            var pages = await visitorsQuery
                .GroupBy(v => v.Path)
                .Select(g => new { Path = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var topPages = pages
                .Where(p => p.Path != null)
                .ToDictionary(p => p.Path!, p => p.Count);

            var countries = await visitorsQuery
                .Where(v => v.Country != null)
                .GroupBy(v => v.Country)
                .Select(g => new { Country = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var topCountries = countries
                .Where(c => c.Country != null)
                .ToDictionary(c => c.Country!, c => c.Count);

            var dateKey = DateOnly.FromDateTime(startUtc.Date);

            // Check if stats already exist for this date
            var existing = await _context.VisitorStats
                .FirstOrDefaultAsync(s => s.Date == dateKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing != null)
            {
                // RESET daily stats (overwrite)
                existing.TotalVisits = totalVisits;
                existing.UniqueVisitors = uniqueVisitors;
                existing.TopPages = topPages;
                existing.TopCountries = topCountries;

                _context.VisitorStats.Update(existing);
            }
            else
            {
                // Create new daily stats
                var dto = new DTOs.VisitorStatsDto(dateKey, totalVisits, uniqueVisitors, topPages, topCountries);
                var newStats = _mapper.ToModel(new DTOs.VisitorStatsDto(dto.Date, dto.TotalVisits, dto.UniqueVisitors, dto.TopPages, dto.TopCountries));
                await _context.VisitorStats.AddAsync(newStats, cancellationToken).ConfigureAwait(false);
            }

            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // Calculate WEEKLY stats (SUM last 7 days)
            var weekStart = dateKey.AddDays(-6);
            var weeklyStats = await _context.VisitorStats
                .AsNoTracking()
                .Where(s => s.Date >= weekStart && s.Date <= dateKey)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var weeklyTotalVisits = weeklyStats.Sum(s => s.TotalVisits);
            var weeklyUniqueVisitors = weeklyStats.Sum(s => s.UniqueVisitors);

            // Merge top pages from all 7 days
            var weeklyTopPages = new Dictionary<string, int>();
            foreach (var stat in weeklyStats)
            {
                foreach (var page in stat.TopPages)
                {
                    if (weeklyTopPages.ContainsKey(page.Key))
                        weeklyTopPages[page.Key] += page.Value;
                    else
                        weeklyTopPages[page.Key] = page.Value;
                }
            }

            // Calculate MONTHLY stats (SUM last 30 days)
            var monthStart = dateKey.AddDays(-29);
            var monthlyStats = await _context.VisitorStats
                .AsNoTracking()
                .Where(s => s.Date >= monthStart && s.Date <= dateKey)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var monthlyTotalVisits = monthlyStats.Sum(s => s.TotalVisits);
            var monthlyUniqueVisitors = monthlyStats.Sum(s => s.UniqueVisitors);

            // Merge top pages from all 30 days
            var monthlyTopPages = new Dictionary<string, int>();
            foreach (var stat in monthlyStats)
            {
                foreach (var page in stat.TopPages)
                {
                    if (monthlyTopPages.ContainsKey(page.Key))
                        monthlyTopPages[page.Key] += page.Value;
                    else
                        monthlyTopPages[page.Key] = page.Value;
                }
            }

            // Log or store weekly/monthly aggregates (optional: store in separate table)
            Console.WriteLine($"Daily ({dateKey}): {totalVisits} visits, {uniqueVisitors} unique");
            Console.WriteLine($"Weekly (last 7 days): {weeklyTotalVisits} visits, {weeklyUniqueVisitors} unique");
            Console.WriteLine($"Monthly (last 30 days): {monthlyTotalVisits} visits, {monthlyUniqueVisitors} unique");

            // TODO: Store weekly/monthly stats in a separate table if you need to query them later
            // Example: Create VisitorStatsWeekly and VisitorStatsMonthly tables
            // OR query them on-demand by summing daily stats in your controllers

            // Post-write cache invalidation: use CancellationToken.None so cleanup always completes
            // even if the background job is being cancelled.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (dateKey == today)
            {
                await _cacheService.RemoveAsync("visitorstats:today", CancellationToken.None).ConfigureAwait(false);
            }
            if (dateKey >= today.AddDays(-6))
            {
                await _cacheService.RemoveAsync("visitorstats:7days", CancellationToken.None).ConfigureAwait(false);
            }
            if (dateKey >= today.AddDays(-29))
            {
                await _cacheService.RemoveAsync("visitorstats:30days", CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async Task<VisitorStats?> GetStatsForDate(DateOnly date, CancellationToken cancellationToken = default)
        {
            // query by DateOnly and return the entity directly
            var result = await _context.VisitorStats
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Date == date, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }

        public async Task<IEnumerable<VisitorStats>> GetStatsRange(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        {
            // compare DateOnly to DateOnly (assumes VisitorStats.Date is DateOnly)
            var results = await _context.VisitorStats
                .AsNoTracking()
                .Where(s => s.Date >= start && s.Date <= end)
                .OrderByDescending(s => s.Date)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return results;
        }

        /// <summary>
        /// Get weekly aggregated stats (sum of last 7 days)
        /// </summary>
        public async Task<(int TotalVisits, int UniqueVisitors, Dictionary<string, int> TopPages)> GetWeeklyStats(DateOnly endDate, CancellationToken cancellationToken = default)
        {
            var weekStart = endDate.AddDays(-6);
            var weeklyStats = await _context.VisitorStats
                .AsNoTracking()
                .Where(s => s.Date >= weekStart && s.Date <= endDate)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var totalVisits = weeklyStats.Sum(s => s.TotalVisits);
            var uniqueVisitors = weeklyStats.Sum(s => s.UniqueVisitors);

            var topPages = new Dictionary<string, int>();
            foreach (var stat in weeklyStats)
            {
                foreach (var page in stat.TopPages ?? new Dictionary<string, int>())
                {
                    if (topPages.ContainsKey(page.Key))
                        topPages[page.Key] += page.Value;
                    else
                        topPages[page.Key] = page.Value;
                }
            }

            return (totalVisits, uniqueVisitors, topPages);
        }

        /// <summary>
        /// Get monthly aggregated stats (sum of last 30 days)
        /// </summary>
        public async Task<(int TotalVisits, int UniqueVisitors, Dictionary<string, int> TopPages)> GetMonthlyStats(DateOnly endDate, CancellationToken cancellationToken = default)
        {
            var monthStart = endDate.AddDays(-29);
            var monthlyStats = await _context.VisitorStats
                .AsNoTracking()
                .Where(s => s.Date >= monthStart && s.Date <= endDate)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var totalVisits = monthlyStats.Sum(s => s.TotalVisits);
            var uniqueVisitors = monthlyStats.Sum(s => s.UniqueVisitors);

            var topPages = new Dictionary<string, int>();
            foreach (var stat in monthlyStats)
            {
                foreach (var page in stat.TopPages ?? new Dictionary<string, int>())
                {
                    if (topPages.ContainsKey(page.Key))
                        topPages[page.Key] += page.Value;
                    else
                        topPages[page.Key] = page.Value;
                }
            }

            return (totalVisits, uniqueVisitors, topPages);
        }
    }
}
