// Converted to Minimal API Endpoints - see Linky/Endpoints/QRCodeEndpoints.cs
// and app.MapQRCodeEndpoints() in Program.cs. Kept here (commented out) for reference/rollback.
// ScanRequestDto (previously defined at the bottom of this file) moved to DTOs/ScanRequestDto.cs.
/*
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Linky.Controllers
{
    public class QRCodeController : BaseController
    {
        private readonly IQRCodeRepository _qrcodeRepository;
        private readonly IGeoIPService _geolocationService;
        private readonly IClientIp _clientIp;

        public QRCodeController(IQRCodeRepository qrcodeRepository, IGeoIPService geolocationService, IClientIp clientIp)
        {
            _qrcodeRepository = qrcodeRepository;
            _geolocationService = geolocationService;
            _clientIp = clientIp;
        }



        /// <summary>
        /// Creates a new QR code metadata entry
        /// </summary>
        [HttpPost("Create")]
        public async Task<IActionResult> Create([FromBody] QRCodeDto dto, CancellationToken cancellationToken = default)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "QR Code data is required" });

            try
            {
                var qrcode = await _qrcodeRepository.CreateAsync(dto, cancellationToken);
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
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty)
                return BadRequest(new { success = false, message = "Invalid QR code ID" });

            try
            {
                var qrcode = await _qrcodeRepository.GetByIdAsync(id, cancellationToken);
                if (qrcode == null)
                    return NotFound(new { success = false, message = "QR code not found" });

                // Track scan - just like URL shortener tracks clicks
                var clientIp = _clientIp.GetClientIp();
                var location = _geolocationService.GetLocationByIp(clientIp);

                await _qrcodeRepository.IncrementScanAsync(id, clientIp, location.Country, location.City, cancellationToken);

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
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            try
            {
                var qrcodes = await _qrcodeRepository.GetAllAsync(cancellationToken);
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
        public async Task<IActionResult> IncrementScan(Guid id, [FromBody] ScanRequestDto request, CancellationToken cancellationToken = default)
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

                await _qrcodeRepository.IncrementScanAsync(id, request.ClientIp, country, city, cancellationToken);
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
        [EnableRateLimiting("qr-image-gen")]
        [Consumes("multipart/form-data")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> GenerateQrCodeImage(
    [FromForm] string text,
    [FromForm] IFormFile? logoFile = null,
    [FromForm] int pixelsPerModule = 20,
    CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text))
                return BadRequest(new { success = false, message = "Text is required to generate QR code" });

            if (pixelsPerModule < 1 || pixelsPerModule > 100)
                return BadRequest(new { success = false, message = "pixelsPerModule must be between 1 and 100" });

            try
            {
                // Privacy-safe behavior: embed the original text directly into the QR code
                var svgBytes = await _qrcodeRepository.GenerateQrCodeImageAsync(text, logoFile, pixelsPerModule, cancellationToken);

                return File(svgBytes, "image/svg+xml", $"qrcode-{DateTime.UtcNow:yyyyMMddHHmmss}.svg");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
*/
