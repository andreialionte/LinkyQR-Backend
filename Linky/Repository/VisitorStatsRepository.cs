using Linky.DataLayer;
using Linky.IRepository;
using Linky.Models;
using Microsoft.EntityFrameworkCore;

namespace Linky.Repository
{
    public class VisitorStatsRepository : IVisitorStatsRepository
    {
        private readonly DataContextEf _context;

        public VisitorStatsRepository(DataContextEf context)
        {
            _context = context;
        }

        public async Task AggregateStatsForDate(DateOnly date)
        {
            // build DateTime range for filtering visitor timestamps
            var startDateTime = date.ToDateTime(TimeOnly.MinValue);
            var endDateTime = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

            // query visitors for the day
            var visitorsQuery = _context.Visitors
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
                var newStats = new Models.VisitorStats
                {
                    Id = Guid.NewGuid(),
                    Date = date, // DateOnly
                    TotalVisits = totalVisits,
                    UniqueVisitors = uniqueVisitors,
                    TopPages = topPages,
                    TopCountries = topCountries
                };

                await _context.VisitorStats.AddAsync(newStats);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<VisitorStats?> GetStatsForDate(DateOnly date)
        {
            // Query by DateOnly and return the entity directly
            var result = await _context.VisitorStats
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Date == date);

            return result;
        }

        public async Task<IEnumerable<VisitorStats>> GetStatsRange(DateOnly start, DateOnly end)
        {
            // Compare DateOnly to DateOnly (assumes VisitorStats.Date is DateOnly)
            var results = await _context.VisitorStats
                .AsNoTracking()
                .Where(s => s.Date >= start && s.Date <= end)
                .OrderByDescending(s => s.Date)
                .ToListAsync();

            return results;
        }
    }
}
