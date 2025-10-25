using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Microsoft.AspNetCore.Mvc;

namespace Linky.Controllers
{
    public class QRCodeController : BaseController
    {
        private readonly IQRCodeRepository _qrcodeRepository;
        private readonly IGeoIPService _geolocationService;

        public QRCodeController(IQRCodeRepository qrcodeRepository, IGeoIPService geolocationService)
        {
            _qrcodeRepository = qrcodeRepository;
            _geolocationService = geolocationService;
        }

        /// <summary>
        /// Gets client IP from HttpContext, handling proxies and X-Forwarded-For headers
        /// </summary>
        private string GetClientIp()
        {
            if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
            {
                var ip = forwarded.ToString().Split(',').FirstOrDefault();
                if (!string.IsNullOrEmpty(ip))
                    return ip.Trim();
            }

            if (Request.Headers.TryGetValue("X-Real-IP", out var realIp))
            {
                if (!string.IsNullOrEmpty(realIp.ToString()))
                    return realIp.ToString();
            }

            return HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Creates a new QR code metadata entry
        /// </summary>
        [HttpPost("Create")]
        public async Task<IActionResult> Create([FromBody] QRCodeDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "QR Code data is required" });

            try
            {
                var qrcode = await _qrcodeRepository.CreateAsync(dto);
                return Ok(new { success = true, data = qrcode, message = "QR code created successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// This is the tracking endpoint - just like URL shortener's GetByCode
        /// When QR code is scanned, it hits this endpoint, tracks the scan, then redirects
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (id == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid QR code ID" });

            try
            {
                var qrcode = await _qrcodeRepository.GetByIdAsync(id);
                if (qrcode == null)
                    return NotFound(new { success = false, message = "QR code not found" });

                // Track scan - just like URL shortener tracks clicks
                var clientIp = GetClientIp();
                var location = _geolocationService.GetLocationByIp(clientIp);

                await _qrcodeRepository.IncrementScanAsync(id, clientIp, location.Country, location.City);

                // Redirect to the actual destination URL
                if (!string.IsNullOrEmpty(qrcode.Content))
                {
                    return Redirect(qrcode.Content);
                }

                return Ok(new { success = true, data = qrcode });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Gets all QR codes
        /// </summary>
        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var qrcodes = await _qrcodeRepository.GetAllAsync();
                return Ok(new { success = true, data = qrcodes });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Manual increment scan (for external tracking)
        /// </summary>
        [HttpPost("IncrementScan/{id}")]
        public async Task<IActionResult> IncrementScan(Guid id, [FromBody] ScanRequestDto request)
        {
            if (id == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid QR code ID" });

            if (request == null || string.IsNullOrEmpty(request.ClientIp))
                return BadRequest(new { success = false, message = "Client IP is required" });

            try
            {
                string? country = request.Country;
                string? city = request.City;

                if (string.IsNullOrEmpty(country) || string.IsNullOrEmpty(city))
                {
                    var geoInfo = _geolocationService.GetLocationByIp(request.ClientIp);
                    country = geoInfo?.Country ?? request.Country;
                    city = geoInfo?.City ?? request.City;
                }

                await _qrcodeRepository.IncrementScanAsync(id, request.ClientIp, country, city);
                return Ok(new { success = true, message = "Scan recorded successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Generates a QR code image with embedded tracking URL
        /// The QR code contains: yourapp.com/api/QRCode/{id}
        /// When scanned, it hits GetById which tracks + redirects
        /// </summary>
        [HttpPost("GenerateQrCodeImage")]
        [Consumes("multipart/form-data")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> GenerateQrCodeImage(
            [FromForm] string text,
            [FromForm] IFormFile? logoFile = null,
            [FromForm] int pixelsPerModule = 20)
        {
            if (string.IsNullOrEmpty(text))
                return BadRequest(new { success = false, message = "Text is required to generate QR code" });

            if (pixelsPerModule < 1 || pixelsPerModule > 100)
                return BadRequest(new { success = false, message = "pixelsPerModule must be between 1 and 100" });

            try
            {
                // The QR code now embeds the tracking URL instead of direct URL
                // So it will hit GetById endpoint which tracks the scan
                var svgBytes = await _qrcodeRepository.GenerateQrCodeImageAsync(text, logoFile, pixelsPerModule);

                return File(svgBytes, "image/svg+xml", $"qrcode-{DateTime.UtcNow:yyyyMMddHHmmss}.svg");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    public class ScanRequestDto
    {
        public string ClientIp { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? City { get; set; }
    }
}