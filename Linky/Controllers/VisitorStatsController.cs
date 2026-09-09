// Converted to Minimal API Endpoints - see Linky/Endpoints/VisitorStatsEndpoints.cs
// and app.MapVisitorStatsEndpoints() in Program.cs. Kept here (commented out) for reference/rollback.
/*
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Microsoft.AspNetCore.Mvc;

namespace Linky.Controllers
{
    public class VisitorStatsController : BaseController
    {
        private readonly IVisitorStatsRepository _statsRepo;
        private readonly ICacheService _cacheService;
        private readonly IVisitorRepository _visitorRepo;
        private readonly VisitorStatsMapper _mapper;

        public VisitorStatsController(IVisitorStatsRepository statsRepo, ICacheService cacheService, IVisitorRepository visitorRepo, VisitorStatsMapper mapper)
        {
            _statsRepo = statsRepo;
            _cacheService = cacheService;
            _visitorRepo = visitorRepo;
            _mapper = mapper;
        }

        [HttpGet("Today")]
        public async Task<IActionResult> GetTodayStats(CancellationToken cancellationToken = default)
        {
            var cacheKey = "visitorstats:today";
            var cached = await _cacheService.GetAsync<object>(cacheKey, cancellationToken);
            if (cached != null)
                return Ok(cached);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var stats = await _statsRepo.GetStatsForDate(today, cancellationToken);

            if (stats == null)
            {
                // Fallback: compute live from Visitors table for today
                var start = DateTime.UtcNow.Date;
                var totalVisits = await _visitorRepo.GetTotalVisits(start, cancellationToken);
                var uniqueVisitors = await _visitorRepo.GetUniqueVisitors(start, cancellationToken);

                var responseLive = new
                {
                    totalVisits,
                    uniqueVisitors,
                    stats = (object?)null
                };

                await _cacheService.SetAsync(cacheKey, responseLive, TimeSpan.FromMinutes(5), cancellationToken);
                return Ok(responseLive);
            }

            var statsDto = _mapper.ToDto(stats);

            var response = new
            {
                totalVisits = stats.TotalVisits,
                uniqueVisitors = stats.UniqueVisitors,
                stats = statsDto
            };

            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(30), cancellationToken);
            return Ok(response);
        }


        [HttpGet("7days")]
        public async Task<IActionResult> GetLast7Days(CancellationToken cancellationToken = default)
        {
            var cacheKey = "visitorstats:7days";
            var cached = await _cacheService.GetAsync<object>(cacheKey, cancellationToken);
            if (cached != null)
                return Ok(cached);

            var end = DateOnly.FromDateTime(DateTime.UtcNow);
            var start = end.AddDays(-6);

            var stats = (await _statsRepo.GetStatsRange(start, end, cancellationToken)).ToList();
            var dtoList = stats.Select(_mapper.ToDto).ToList();
            var totalVisits = dtoList.Sum(s => s.TotalVisits);
            var uniqueVisitors = dtoList.Sum(s => s.UniqueVisitors);

            var response = new { totalVisits, uniqueVisitors, stats = dtoList };
            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromHours(1), cancellationToken);
            return Ok(response);
        }

        [HttpGet("30days")]
        public async Task<IActionResult> GetLast30Days(CancellationToken cancellationToken = default)
        {
            var cacheKey = "visitorstats:30days";
            var cached = await _cacheService.GetAsync<object>(cacheKey, cancellationToken);
            if (cached != null)
                return Ok(cached);

            var end = DateOnly.FromDateTime(DateTime.UtcNow);
            var start = end.AddDays(-29);

            var stats = (await _statsRepo.GetStatsRange(start, end, cancellationToken)).ToList();
            var dtoList = stats.Select(_mapper.ToDto).ToList();
            var totalVisits = dtoList.Sum(s => s.TotalVisits);
            var uniqueVisitors = dtoList.Sum(s => s.UniqueVisitors);

            var response = new { totalVisits, uniqueVisitors, stats = dtoList };
            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromHours(2), cancellationToken);
            return Ok(response);
        }
    }
}
*/
