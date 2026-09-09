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
    /// Minimal API equivalent of Controllers/QRCodeController.cs.
    /// Same routes ("api/QRCode/..."), same rate limiting policies, same logic.
    /// </summary>
    public static class QRCodeEndpoints
    {
        public static IEndpointRouteBuilder MapQRCodeEndpoints(this IEndpointRouteBuilder app)
        {
            // Controller used [Route("api/[controller]")] (inherited from BaseController) with
            // "QRCode" as the [controller] token, plus [EnableRateLimiting("spam-api")] as the
            // default policy for the whole controller.
            var group = app.MapGroup("api/QRCode")
                .RequireRateLimiting("spam-api")
                .WithTags("QRCode");

            // Creates a new QR code metadata entry
            group.MapPost("/Create", async (
                [FromBody] QRCodeDto dto,
                IQRCodeRepository qrcodeRepository,
                CancellationToken cancellationToken) =>
            {
                // Note: QRCodeDto is a readonly record struct, so the original controller's
                // "if (dto == null)" check was always false (dead code, CS0472) - a struct
                // can never be null. Preserved behavior by omitting that no-op check here.
                try
                {
                    var qrcode = await qrcodeRepository.CreateAsync(dto, cancellationToken);
                    return Results.Ok(new { success = true, data = qrcode, message = "QR code created successfully" });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                }
            });

            // Tracking endpoint - just like URL shortener's GetByCode.
            // When QR code is scanned, it hits this endpoint, tracks the scan, then redirects.
            group.MapGet("/{id:guid}", async (
                Guid id,
                IQRCodeRepository qrcodeRepository,
                IGeoIPService geolocationService,
                IClientIp clientIpAccessor,
                CancellationToken cancellationToken) =>
            {
                if (id == Guid.Empty)
                    return Results.BadRequest(new { success = false, message = "Invalid QR code ID" });

                try
                {
                    var qrcode = await qrcodeRepository.GetByIdAsync(id, cancellationToken);
                    if (qrcode == null)
                        return Results.NotFound(new { success = false, message = "QR code not found" });

                    // Track scan - just like URL shortener tracks clicks
                    var clientIp = clientIpAccessor.GetClientIp();
                    var location = geolocationService.GetLocationByIp(clientIp);

                    await qrcodeRepository.IncrementScanAsync(id, clientIp, location.Country, location.City, cancellationToken);

                    // Redirect to the actual destination URL
                    if (!string.IsNullOrEmpty(qrcode.Content))
                    {
                        return Results.Redirect(qrcode.Content);
                    }

                    return Results.Ok(new { success = true, data = qrcode });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                }
            });

            // Gets all QR codes
            group.MapGet("/GetAll", async (
                IQRCodeRepository qrcodeRepository,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var qrcodes = await qrcodeRepository.GetAllAsync(cancellationToken);
                    return Results.Ok(new { success = true, data = qrcodes });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                }
            });

            // Manual increment scan (for external tracking)
            group.MapPost("/IncrementScan/{id:guid}", async (
                Guid id,
                [FromBody] ScanRequestDto request,
                IQRCodeRepository qrcodeRepository,
                IGeoIPService geolocationService,
                CancellationToken cancellationToken) =>
            {
                if (id == Guid.Empty)
                    return Results.BadRequest(new { success = false, message = "Invalid QR code ID" });

                if (request == null || string.IsNullOrEmpty(request.ClientIp))
                    return Results.BadRequest(new { success = false, message = "Client IP is required" });

                try
                {
                    string? country = request.Country;
                    string? city = request.City;

                    if (string.IsNullOrEmpty(country) || string.IsNullOrEmpty(city))
                    {
                        var geoInfo = geolocationService.GetLocationByIp(request.ClientIp);
                        country = geoInfo?.Country ?? request.Country;
                        city = geoInfo?.City ?? request.City;
                    }

                    await qrcodeRepository.IncrementScanAsync(id, request.ClientIp, country, city, cancellationToken);
                    return Results.Ok(new { success = true, message = "Scan recorded successfully" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new { success = false, message = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                }
            });

            // Generates a QR code image with embedded tracking URL.
            // Privacy-safe behavior: embeds the original text directly into the QR code.
            group.MapPost("/GenerateQrCodeImage", async (
                [FromForm] string text,
                [FromForm] IFormFile? logoFile,
                [FromForm] int pixelsPerModule,
                IQRCodeRepository qrcodeRepository,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrEmpty(text))
                    return Results.BadRequest(new { success = false, message = "Text is required to generate QR code" });

                if (pixelsPerModule < 1 || pixelsPerModule > 100)
                    return Results.BadRequest(new { success = false, message = "pixelsPerModule must be between 1 and 100" });

                try
                {
                    var svgBytes = await qrcodeRepository.GenerateQrCodeImageAsync(text, logoFile, pixelsPerModule, cancellationToken);

                    return Results.File(svgBytes, "image/svg+xml", $"qrcode-{DateTime.UtcNow:yyyyMMddHHmmss}.svg");
                }
                catch (Exception ex)
                {
                    return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                }
            })
            .RequireRateLimiting("qr-image-gen") // overrides the group's "spam-api" policy for this endpoint
            .DisableAntiforgery()
            .ExcludeFromDescription(); // matches [ApiExplorerSettings(IgnoreApi = true)]

            return app;
        }
    }
}


