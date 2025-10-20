using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using SkiaSharp;

namespace Linky.Repository
{
    public class QRCodeRepository : IQRCodeRepository
    {
        private readonly DataContextEf _context;
        private readonly ICacheService _cacheService;

        public QRCodeRepository(DataContextEf context, ICacheService cacheService)
        {
            _context = context;
            _cacheService = cacheService;
        }

        public async Task<Linky.Models.QRCode> CreateAsync(QRCodeDto dto)
        {
            var entity = new Linky.Models.QRCode
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                ExpirationDate = dto.ExpirationDate,
                IsActive = true,
                LastScannedAt = DateTime.MinValue,
                ScanCount = 0
            };

            _context.QRCodes.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<Linky.Models.QRCode?> GetByIdAsync(Guid id)
        {
            var cacheKey = $"QRCode:{id}";

            var cachedVal = await _cacheService.GetAsync<Linky.Models.QRCode>(cacheKey);
            if (cachedVal != null) return cachedVal;

            var entity = await _context.QRCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == id);

            if (entity != null)
                await _cacheService.SetAsync(cacheKey, entity, TimeSpan.FromMinutes(20));

            return entity;
        }

        public async Task<IEnumerable<Linky.Models.QRCode>> GetAllAsync()
        {
            const string cacheKey = "QRCode:All";

            var cachedVal = await _cacheService.GetAsync<IEnumerable<Linky.Models.QRCode>>(cacheKey);
            if (cachedVal != null) return cachedVal;

            var list = await _context.QRCodes
                .AsNoTracking()
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync();

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromMinutes(20));
            return list;
        }

        public async Task IncrementScanAsync(Guid id, string clientIp, string? country, string? city)
        {
            var qrCode = await _context.QRCodes.FirstOrDefaultAsync(q => q.Id == id);

            if (qrCode == null) throw new InvalidOperationException($"QR code with ID {id} not found");
            if (!qrCode.IsActive) throw new InvalidOperationException($"QR code with ID {id} is not active");
            if (qrCode.ExpirationDate.HasValue && qrCode.ExpirationDate < DateTime.UtcNow)
                throw new InvalidOperationException($"QR code with ID {id} has expired");

            qrCode.ScanCount++;
            qrCode.LastScannedAt = DateTime.UtcNow;
            qrCode.LastIp = clientIp;
            qrCode.LastCountry = country;
            qrCode.LastCity = city;

            await _context.SaveChangesAsync();

            // Invalidate cache
            var cacheKey = $"QRCode:{id}";
            await _cacheService.RemoveAsync(cacheKey);
        }

        public async Task<byte[]> GenerateQrCodeImageAsync(string text, IFormFile? logoFile = null, int pixelsPerModule = 20)
        {
            // 1️⃣ Check if QR code metadata exists
            var qrEntity = await _context.QRCodes
                .FirstOrDefaultAsync(q => q.Content == text);

            if (qrEntity == null)
            {
                qrEntity = new Linky.Models.QRCode
                {
                    Id = Guid.NewGuid(),
                    Content = text,
                    CreatedAt = DateTime.UtcNow,
                    ExpirationDate = null,
                    IsActive = true,
                    LastScannedAt = DateTime.MinValue,
                    ScanCount = 0
                };
                _context.QRCodes.Add(qrEntity);
                await _context.SaveChangesAsync();
            }

            // 2️⃣ Generate QR code using SkiaSharp (no System.Drawing)
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);

            // Use SkiaSharp renderer instead of System.Drawing
            var renderer = new SkiaSharpQRCodeRenderer();
            var qrCodeImage = renderer.RenderQrCode(qrData, pixelsPerModule);

            byte[] imageBytes = qrCodeImage;

            // If logo exists, overlay it
            if (logoFile != null)
            {
                imageBytes = await OverlayLogoOnQRAsync(imageBytes, logoFile, pixelsPerModule);
            }

            return imageBytes;
        }

        private async Task<byte[]> OverlayLogoOnQRAsync(byte[] qrImageBytes, IFormFile logoFile, int pixelsPerModule)
        {
            using var qrStream = new MemoryStream(qrImageBytes);
            using var qrImage = SKImage.FromEncodedData(qrStream);

            using var logoStream = logoFile.OpenReadStream();
            using var logoImage = SKImage.FromEncodedData(logoStream);

            if (qrImage == null || logoImage == null)
                throw new InvalidOperationException("Failed to load QR code or logo image");

            // Create a canvas the size of the QR code
            using var surface = SKSurface.Create(new SKImageInfo(qrImage.Width, qrImage.Height));
            using var canvas = surface.Canvas;

            // Draw QR code background
            canvas.Clear(SKColors.White);
            canvas.DrawImage(qrImage, 0, 0);

            // Calculate logo size (15% of QR code)
            int logoSize = (int)(qrImage.Width * 0.15);
            int logoPosX = (qrImage.Width - logoSize) / 2;
            int logoPosY = (qrImage.Height - logoSize) / 2;
            int borderWidth = 3;

            // Draw white background for logo (with border)
            var bgPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };
            canvas.DrawRect(
                new SKRect(
                    logoPosX - borderWidth,
                    logoPosY - borderWidth,
                    logoPosX + logoSize + borderWidth,
                    logoPosY + logoSize + borderWidth
                ),
                bgPaint
            );

            // Draw logo
            canvas.DrawImage(
                logoImage,
                new SKRect(logoPosX, logoPosY, logoPosX + logoSize, logoPosY + logoSize)
            );

            // Encode to PNG
            using var finalImage = surface.Snapshot();
            using var data = finalImage.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    /// <summary>
    /// SkiaSharp-based QR code renderer (cross-platform, no System.Drawing)
    /// </summary>
    public class SkiaSharpQRCodeRenderer
    {
        public byte[] RenderQrCode(QRCodeData qrCodeData, int pixelsPerModule = 20)
        {
            int moduleCount = qrCodeData.ModuleMatrix.Count;
            int imageSize = moduleCount * pixelsPerModule;

            using var surface = SKSurface.Create(new SKImageInfo(imageSize, imageSize));
            using var canvas = surface.Canvas;

            // Clear white background
            canvas.Clear(SKColors.White);

            using var blackPaint = new SKPaint { Color = SKColors.Black };

            // Draw QR code modules
            for (int y = 0; y < moduleCount; y++)
            {
                for (int x = 0; x < moduleCount; x++)
                {
                    if (qrCodeData.ModuleMatrix[y][x])
                    {
                        var rect = new SKRect(
                            x * pixelsPerModule,
                            y * pixelsPerModule,
                            (x + 1) * pixelsPerModule,
                            (y + 1) * pixelsPerModule
                        );
                        canvas.DrawRect(rect, blackPaint);
                    }
                }
            }

            // Encode to PNG
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}