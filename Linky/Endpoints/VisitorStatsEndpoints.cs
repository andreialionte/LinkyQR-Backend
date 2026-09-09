using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Linky.Endpoints
{
    /// <summary>
    /// Minimal API equivalent of Controllers/VisitorStatsController.cs.
    /// Same routes ("api/VisitorStats/..."), same rate limiting policy, same logic.
    /// </summary>
    public static class VisitorStatsEndpoints
    {
        public static IEndpointRouteBuilder MapVisitorStatsEndpoints(this IEndpointRouteBuilder app)
        {
            // Controller used [Route("api/[controller]")] (inherited from BaseController) with
            // "VisitorStats" as the [controller] token, plus [EnableRateLimiting("spam-api")].
            var group = app.MapGroup("api/VisitorStats")
                .RequireRateLimiting("spam-api")
                .WithTags("VisitorStats");

            group.MapGet("/Today", async (
                IVisitorStatsRepository statsRepo,
                ICacheService cacheService,
                IVisitorRepository visitorRepo,
                VisitorStatsMapper mapper,
                CancellationToken cancellationToken) =>
            {
                var cacheKey = "visitorstats:today";
                var cached = await cacheService.GetAsync<object>(cacheKey, cancellationToken);
                if (cached != null)
                    return Results.Ok(cached);

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var stats = await statsRepo.GetStatsForDate(today, cancellationToken);

                if (stats == null)
                {
                    // Fallback: compute live from Visitors table for today
                    var start = DateTime.UtcNow.Date;
                    var totalVisits = await visitorRepo.GetTotalVisits(start, cancellationToken);
                    var uniqueVisitors = await visitorRepo.GetUniqueVisitors(start, cancellationToken);

                    var responseLive = new
                    {
                        totalVisits,
                        uniqueVisitors,
                        stats = (object?)null
                    };

                    await cacheService.SetAsync(cacheKey, responseLive, TimeSpan.FromMinutes(5), cancellationToken);
                    return Results.Ok(responseLive);
                }

                var statsDto = mapper.ToDto(stats);

                var response = new
                {
                    totalVisits = stats.TotalVisits,
                    uniqueVisitors = stats.UniqueVisitors,
                    stats = statsDto
                };

                await cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(30), cancellationToken);
                return Results.Ok(response);
            });

            group.MapGet("/7days", async (
                IVisitorStatsRepository statsRepo,
                ICacheService cacheService,
                VisitorStatsMapper mapper,
                CancellationToken cancellationToken) =>
            {
                var cacheKey = "visitorstats:7days";
                var cached = await cacheService.GetAsync<object>(cacheKey, cancellationToken);
                if (cached != null)
                    return Results.Ok(cached);

                var end = DateOnly.FromDateTime(DateTime.UtcNow);
                var start = end.AddDays(-6);

                var stats = (await statsRepo.GetStatsRange(start, end, cancellationToken)).ToList();
                var dtoList = stats.Select(mapper.ToDto).ToList();
                var totalVisits = dtoList.Sum(s => s.TotalVisits);
                var uniqueVisitors = dtoList.Sum(s => s.UniqueVisitors);

                var response = new { totalVisits, uniqueVisitors, stats = dtoList };
                await cacheService.SetAsync(cacheKey, response, TimeSpan.FromHours(1), cancellationToken);
                return Results.Ok(response);
            });

            group.MapGet("/30days", async (
                IVisitorStatsRepository statsRepo,
                ICacheService cacheService,
                VisitorStatsMapper mapper,
                CancellationToken cancellationToken) =>
            {
                var cacheKey = "visitorstats:30days";
                var cached = await cacheService.GetAsync<object>(cacheKey, cancellationToken);
                if (cached != null)
                    return Results.Ok(cached);

                var end = DateOnly.FromDateTime(DateTime.UtcNow);
                var start = end.AddDays(-29);

                var stats = (await statsRepo.GetStatsRange(start, end, cancellationToken)).ToList();
                var dtoList = stats.Select(mapper.ToDto).ToList();
                var totalVisits = dtoList.Sum(s => s.TotalVisits);
                var uniqueVisitors = dtoList.Sum(s => s.UniqueVisitors);

                var response = new { totalVisits, uniqueVisitors, stats = dtoList };
                await cacheService.SetAsync(cacheKey, response, TimeSpan.FromHours(2), cancellationToken);
                return Results.Ok(response);
            });

            return app;
        }
    }
}

