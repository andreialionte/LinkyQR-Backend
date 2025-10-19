using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Microsoft.AspNetCore.Mvc;

namespace Linky.Controllers
{
    [ApiController]
    public sealed class UrlShortenerControllers : ControllerBase
    {
        private readonly IURLShortenerRepository _urlRepo;
        private readonly IGeoIPService _geoIpService;

        public UrlShortenerControllers(IURLShortenerRepository urlRepo, IGeoIPService geoIpService)
        {
            _urlRepo = urlRepo;
            _geoIpService = geoIpService;
        }

        [HttpPost("ShortUrl")]
        public async Task<IActionResult> ShortUrl([FromBody] URLShortenerDto urlDto, [FromQuery] string? customAlias = null)
        {
            // If custom alias provided, check if it exists
            if (!string.IsNullOrWhiteSpace(customAlias))
            {
                var existingAlias = await _urlRepo.GetByCode(customAlias);
                if (existingAlias != null)
                {
                    // Alias exists, return 200 OK
                    return Ok(existingAlias);
                }
            }

            // Create or return existing URL
            var url = await _urlRepo.CreateShortUrlAsync(urlDto, customAlias);
            
            // If TotalClicks > 0, it already existed in DB
            if (url.TotalClicks > 0)
            {
                // URL already exists, return 200 OK
                return Ok(url);
            }
            else
            {
                // New URL created, return 201 Created
                return CreatedAtAction(nameof(GetByCode), new { code = url.ShortenedUrl }, url);
            }
        }

        [HttpGet("{code}")]
        public async Task<IActionResult> GetByCode([FromRoute] string code)
        {
            var url = await _urlRepo.GetByCode(code);
            if (url == null)
                return NotFound();

            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = Request.Headers["User-Agent"].ToString();
            var referrer = Request.Headers["Referer"].ToString();

            var location = _geoIpService.GetLocationByIp(clientIp);

            await _urlRepo.IncrementClickAsync(code, clientIp, location.Country, location.City, userAgent, referrer);

            return Redirect(url.OriginalUrl);
        }
    }
}