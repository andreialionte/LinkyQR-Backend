using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Linky.Endpoints
{
    /// <summary>
    /// Minimal API equivalent of Controllers/UrlShortenerControllers.cs.
    /// Same routes (no "api/" prefix - short links live at the root), same
    /// rate limiting policy, same logic.
    /// </summary>
    public static class UrlShortenerEndpoints
    {
        public static IEndpointRouteBuilder MapUrlShortenerEndpoints(this IEndpointRouteBuilder app)
        {
            // Original controller had no [Route] attribute of its own, so its action
            // routes ("ShortUrl", "{code}") are relative to the app root, not "api/...".
            var group = app.MapGroup(string.Empty)
                .RequireRateLimiting("public-high-volume-api")
                .WithTags("UrlShortener");

            group.MapPost("/ShortUrl", async (
                HttpContext httpContext,
                [FromBody] URLShortenerDto urlDto,
                [FromQuery] string? customAlias,
                IURLShortenerRepository urlRepo,
                CancellationToken cancellationToken) =>
            {
                // If custom alias provided, check if it exists
                if (!string.IsNullOrWhiteSpace(customAlias))
                {
                    var existingAlias = await urlRepo.GetByCode(customAlias, cancellationToken);
                    if (existingAlias != null)
                    {
                        // Alias exists, return 200 OK
                        return Results.Ok(existingAlias);
                    }
                }

                // Create or return existing URL
                var url = await urlRepo.CreateShortUrlAsync(urlDto, customAlias, cancellationToken);

                // If TotalClicks > 0, it already existed in DB
                if (url.TotalClicks > 0)
                {
                    // URL already exists, return 200 OK
                    return Results.Ok(url);
                }
                else
                {
                    // New URL created, return 201 Created (location points at the GetByCode route)
                    return Results.Created($"/{url.ShortenedUrl}", url);
                }
            });

            group.MapGet("/{code}", async (
                [FromRoute] string code,
                IURLShortenerRepository urlRepo,
                IGeoIPService geoIpService,
                IClientIp clientIp,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var url = await urlRepo.GetByCode(code, cancellationToken);
                if (url == null)
                    return Results.NotFound();

                var ip = clientIp.GetClientIp();
                var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
                var referrer = httpContext.Request.Headers["Referer"].ToString();

                var location = geoIpService.GetLocationByIp(ip);

                await urlRepo.IncrementClickAsync(code, ip, location.Country, location.City, userAgent, referrer, cancellationToken);

                return Results.Redirect(url.OriginalUrl);
            })
            .RequireRateLimiting("public-high-volume-api"); // matches the extra [EnableRateLimiting] on GetByCode (no-op since group already sets it, kept for parity)

            return app;
        }
    }
}

