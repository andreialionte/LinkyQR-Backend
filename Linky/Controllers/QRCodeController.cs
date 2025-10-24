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
        /// Creates a new QR code
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
        /// Gets a QR code by ID
        /// </summary>
        [HttpGet("GetById/{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (id == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid QR code ID" });

            try
            {
                var qrcode = await _qrcodeRepository.GetByIdAsync(id);
                if (qrcode == null)
                    return NotFound(new { success = false, message = "QR code not found" });

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
        /// Increments scan count for a QR code
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
                // Get client geolocation if not provided
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
        /// Generates a QR code image
        /// </summary>
        [HttpPost("GenerateQrCodeImage")]
        [Consumes("multipart/form-data")]
        [ApiExplorerSettings(IgnoreApi = true)]

        public async Task<IActionResult> GenerateQrCodeImage([FromForm] string text, [FromForm] IFormFile? logoFile = null, [FromForm] int pixelsPerModule = 20)
        {
            if (string.IsNullOrEmpty(text))
                return BadRequest(new { success = false, message = "Text is required to generate QR code" });

            if (pixelsPerModule < 1 || pixelsPerModule > 100)
                return BadRequest(new { success = false, message = "pixelsPerModule must be between 1 and 100" });

            // Validate logo file if provided
            if (logoFile != null && logoFile.Length > 5 * 1024 * 1024) // 5MB limit
                return BadRequest(new { success = false, message = "Logo file size must not exceed 5MB" });

            try
            {
                var imageBytes = await _qrcodeRepository.GenerateQrCodeImageAsync(text, logoFile, pixelsPerModule);
                return File(imageBytes, "image/png", $"qrcode-{DateTime.UtcNow:yyyyMMddHHmmss}.png");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    /// <summary>
    /// DTO for scan request - contains client IP and optional geolocation data
    /// </summary>
    public class ScanRequestDto
    {
        public string ClientIp { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? City { get; set; }
    }
}