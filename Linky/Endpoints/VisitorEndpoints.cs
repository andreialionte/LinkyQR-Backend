using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Linky.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Linky.Endpoints
{
    /// <summary>
    /// Minimal API equivalent of Controllers/VisitorController.cs.
    /// Same routes ("api/Visitor/..."), same rate limiting policy, same logic.
    /// </summary>
    public static class VisitorEndpoints
    {
        public static IEndpointRouteBuilder MapVisitorEndpoints(this IEndpointRouteBuilder app)
        {
            // Controller used [Route("api/[controller]")] (inherited from BaseController) with
            // "Visitor" as the [controller] token, plus [EnableRateLimiting("spam-api")].
            var group = app.MapGroup("api/Visitor")
                .RequireRateLimiting("spam-api")
                .WithTags("Visitor");

            group.MapPost("/AddVisitor", async (
                HttpContext httpContext,
                [FromBody] VisitorDto visitorDto,
                IVisitorRepository visitorRepo,
                IGeoIPService geoIpService,
                IClientIp clientIp,
                CancellationToken cancellationToken) =>
            {
                Guid sessionId = Guid.Empty;
                if (httpContext.Request.Cookies.TryGetValue("VisitorId", out var cookieValue) && Guid.TryParse(cookieValue, out var parsed))
                {
                    sessionId = parsed;
                }

                if (sessionId != Guid.Empty)
                {
                    var existing = await visitorRepo.GetVisitorBySessionId(sessionId, cancellationToken);
                    if (existing != null && existing.Timestamp >= DateTime.UtcNow.AddMinutes(-30))
                        return Results.StatusCode(StatusCodes.Status204NoContent);
                }

                var ip = clientIp.GetClientIp();
                var geoLoc = geoIpService.GetLocationByIp(ip);

                var visitorWithSession = visitorDto with
                {
                    Id = sessionId == Guid.Empty ? Guid.NewGuid() : sessionId,
                    Ip = ip,
                    Timestamp = DateTime.UtcNow,
                    Country = geoLoc?.Country,
                    City = geoLoc?.City,
                    IsUnique = true
                };

                await visitorRepo.AddVisitor(visitorWithSession, cancellationToken);

                return Results.Ok(new { message = "Visitor added successfully", id = visitorWithSession.Id });
            });

            group.MapGet("/visitor/{visitorId:guid}", async (
                Guid visitorId,
                IVisitorRepository visitorRepo,
                VisitorMapper mapper,
                CancellationToken cancellationToken) =>
            {
                var visitor = await visitorRepo.GetVisitorBySessionId(visitorId, cancellationToken);
                if (visitor == null) return Results.NotFound();
                var dto = mapper.ToDto(visitor);
                return Results.Ok(dto);
            });

            group.MapGet("/RecentVisitors", async (
                IVisitorRepository visitorRepo,
                VisitorMapper mapper,
                [FromQuery] int limit = 100,
                CancellationToken cancellationToken = default) =>
            {
                var visitors = await visitorRepo.GetRecentVisitors(limit, cancellationToken);
                if (visitors == null || !visitors.Any())
                    return Results.NotFound("No visitors found.");
                var dtos = visitors.Select(mapper.ToDto);
                return Results.Ok(dtos);
            });

            return app;
        }
    }
}


