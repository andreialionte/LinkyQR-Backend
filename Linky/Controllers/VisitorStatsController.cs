using Linky.IRepository;
using Linky.IService;
using Linky.Models;
using Microsoft.AspNetCore.Mvc;

namespace Linky.Controllers
{
    public class VisitorStatsController : BaseController
    {
        private readonly IVisitorStatsRepository _statsRepo;
        private readonly ICacheService _cacheService;

        public VisitorStatsController(IVisitorStatsRepository statsRepo, ICacheService cacheService)
        {
            _statsRepo = statsRepo;
            _cacheService = cacheService;
        }

        [HttpGet("Today")]
        public async Task<IActionResult> GetTodayStats()
        {
            var cacheKey = "visitorstats:today";
            var cached = await _cacheService.GetAsync<VisitorStats>(cacheKey);
            if (cached != null)
                return Ok(cached);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var stats = await _statsRepo.GetStatsForDate(today);

            if (stats == null)
            {
                //or exception idk 
                return NotFound(new { message = "Stats not available yet" });
            }

            await _cacheService.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(10));
            return Ok(stats);
        }

        [HttpGet("7days")]
        public async Task<IActionResult> GetLast7Days()
        {
            var cacheKey = "visitorstats:7days";
            var cached = await _cacheService.GetAsync<object>(cacheKey);
            if (cached != null)
                return Ok(cached);

            var end = DateOnly.FromDateTime(DateTime.UtcNow);
            var start = end.AddDays(-6);

            var stats = (await _statsRepo.GetStatsRange(start, end)).ToList();
            var totalVisits = stats.Sum(s => s.TotalVisits);
            var uniqueVisitors = stats.Sum(s => s.UniqueVisitors);

            var response = new { totalVisits, uniqueVisitors, stats };
            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(10));

            return Ok(response);
        }

        [HttpGet("30days")]
        public async Task<IActionResult> GetLast30Days()
        {
            var cacheKey = "visitorstats:30days";
            var cached = await _cacheService.GetAsync<object>(cacheKey);
            if (cached != null)
                return Ok(cached);

            var end = DateOnly.FromDateTime(DateTime.UtcNow);
            var start = end.AddDays(-29);

            var stats = (await _statsRepo.GetStatsRange(start, end)).ToList();
            var totalVisits = stats.Sum(s => s.TotalVisits);
            var uniqueVisitors = stats.Sum(s => s.UniqueVisitors);

            var response = new { totalVisits, uniqueVisitors, stats };
            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(10));

            return Ok(response);
        }
    }
}
