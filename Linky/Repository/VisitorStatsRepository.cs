using Dapper;
using Linky.DataLayer;
using Linky.IRepository;
using Linky.Models;
using Newtonsoft.Json;

namespace Linky.Repository
{
    public class VisitorStatsRepository : IVisitorStatsRepository
    {
        private readonly DapperDbContext _context;

        public VisitorStatsRepository(DapperDbContext context)
        {
            _context = context;
        }

        public async Task AggregateStatsForDate(DateOnly date)
        {
            // Convert DateOnly to DateTime for Dapper
            var dateTime = date.ToDateTime(TimeOnly.MinValue);
            var nextDay = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

            using var connection = _context.CreateConnection();

            // Calculate daily stats
            const string statsSql = @"
                SELECT 
                    COUNT(*) AS TotalVisits,
                    COUNT(DISTINCT ""Ip"") AS UniqueVisitors
                FROM ""Visitors""
                WHERE ""Timestamp"" >= @Date AND ""Timestamp"" < @NextDay";

            var stats = await connection.QuerySingleOrDefaultAsync<dynamic>(
                statsSql,
                new { Date = dateTime, NextDay = nextDay }
            ) ?? new { TotalVisits = 0, UniqueVisitors = 0 };

            // Get top pages
            const string pagesSql = @"
                SELECT ""Path"", COUNT(*) AS Count
                FROM ""Visitors""
                WHERE ""Timestamp"" >= @Date AND ""Timestamp"" < @NextDay
                GROUP BY ""Path""
                ORDER BY Count DESC
                LIMIT 10";

            var pages = await connection.QueryAsync<dynamic>(pagesSql, new { Date = dateTime, NextDay = nextDay });
            var topPages = pages.ToDictionary(p => (string)p.Path, p => (int)p.Count);

            // Get top countries
            const string countriesSql = @"
                SELECT ""Country"", COUNT(*) AS Count
                FROM ""Visitors""
                WHERE ""Timestamp"" >= @Date AND ""Timestamp"" < @NextDay AND ""Country"" IS NOT NULL
                GROUP BY ""Country""
                ORDER BY Count DESC
                LIMIT 10";

            var countries = await connection.QueryAsync<dynamic>(countriesSql, new { Date = dateTime, NextDay = nextDay });
            var topCountries = countries.ToDictionary(c => (string)c.Country, c => (int)c.Count);

            // Check if stats already exist
            const string checkSql = "SELECT COUNT(*) FROM \"VisitorStats\" WHERE \"Date\" = @Date";
            var exists = await connection.ExecuteScalarAsync<int>(checkSql, new { Date = dateTime }) > 0;

            if (exists)
            {
                // Update existing record
                const string updateSql = @"
                    UPDATE ""VisitorStats"" 
                    SET 
                        ""TotalVisits"" = @TotalVisits, 
                        ""UniqueVisitors"" = @UniqueVisitors,
                        ""TopPages"" = @TopPages,
                        ""TopCountries"" = @TopCountries
                    WHERE ""Date"" = @Date";

                await connection.ExecuteAsync(updateSql, new
                {
                    Date = dateTime,
                    TotalVisits = (int)stats.TotalVisits,
                    UniqueVisitors = (int)stats.UniqueVisitors,
                    TopPages = JsonConvert.SerializeObject(topPages),
                    TopCountries = JsonConvert.SerializeObject(topCountries)
                });
            }
            else
            {
                // Insert new record
                const string insertSql = @"
                    INSERT INTO ""VisitorStats"" (""Id"", ""Date"", ""TotalVisits"", ""UniqueVisitors"", ""TopPages"", ""TopCountries"")
                    VALUES (@Id, @Date, @TotalVisits, @UniqueVisitors, @TopPages, @TopCountries)";

                await connection.ExecuteAsync(insertSql, new
                {
                    Id = Guid.NewGuid(),
                    Date = dateTime,
                    TotalVisits = (int)stats.TotalVisits,
                    UniqueVisitors = (int)stats.UniqueVisitors,
                    TopPages = JsonConvert.SerializeObject(topPages),
                    TopCountries = JsonConvert.SerializeObject(topCountries)
                });
            }
        }

        public async Task<VisitorStats?> GetStatsForDate(DateOnly date)
        {
            using var connection = _context.CreateConnection();
            var dateTime = date.ToDateTime(TimeOnly.MinValue);

            const string sql = @"SELECT * FROM ""VisitorStats"" WHERE ""Date"" = @Date";
            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(sql, new { Date = dateTime });

            if (result == null) return null;

            return new VisitorStats
            {
                Id = result.Id,
                Date = DateOnly.FromDateTime(result.Date),
                TotalVisits = result.TotalVisits,
                UniqueVisitors = result.UniqueVisitors,
                TopPages = JsonConvert.DeserializeObject<Dictionary<string, int>>(result.TopPages ?? "{}")!,
                TopCountries = JsonConvert.DeserializeObject<Dictionary<string, int>>(result.TopCountries ?? "{}")!
            };
        }

        public async Task<IEnumerable<VisitorStats>> GetStatsRange(DateOnly start, DateOnly end)
        {
            using var connection = _context.CreateConnection();
            var startDateTime = start.ToDateTime(TimeOnly.MinValue);
            var endDateTime = end.ToDateTime(TimeOnly.MinValue);

            const string sql = @"
                SELECT * FROM ""VisitorStats"" 
                WHERE ""Date"" BETWEEN @Start AND @End
                ORDER BY ""Date"" DESC";

            var results = await connection.QueryAsync<dynamic>(sql, new { Start = startDateTime, End = endDateTime });

            return results.Select(r => new VisitorStats
            {
                Id = r.Id,
                Date = DateOnly.FromDateTime(r.Date),
                TotalVisits = r.TotalVisits,
                UniqueVisitors = r.UniqueVisitors,
                TopPages = JsonConvert.DeserializeObject<Dictionary<string, int>>(r.TopPages ?? "{}")!,
                TopCountries = JsonConvert.DeserializeObject<Dictionary<string, int>>(r.TopCountries ?? "{}")!
            });
        }
    }
}
