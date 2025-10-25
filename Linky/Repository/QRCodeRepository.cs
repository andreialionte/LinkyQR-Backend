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

            // 2️⃣ Generate QR code using SkiaSharp
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);

            // Get QR code matrix
            var qrMatrix = qrData.ModuleMatrix;
            int moduleCount = qrMatrix.Count;
            int imageSize = moduleCount * pixelsPerModule;

            // Create bitmap with SkiaSharp
            using var surface = SKSurface.Create(new SKImageInfo(imageSize, imageSize));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            // Draw QR code modules
            using var blackPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };

            for (int y = 0; y < moduleCount; y++)
            {
                for (int x = 0; x < moduleCount; x++)
                {
                    if (qrMatrix[y][x])
                    {
                        canvas.DrawRect(
                            x * pixelsPerModule,
                            y * pixelsPerModule,
                            pixelsPerModule,
                            pixelsPerModule,
                            blackPaint
                        );
                    }
                }
            }

            // 3️⃣ Add logo if provided
            if (logoFile != null && logoFile.Length > 0)
            {
                using var logoStream = logoFile.OpenReadStream();
                using var logoBitmap = SKBitmap.Decode(logoStream);

                if (logoBitmap != null)
                {
                    // Calculate logo size (15% of QR code)
                    int logoSize = (int)(imageSize * 0.15);
                    int logoX = (imageSize - logoSize) / 2;
                    int logoY = (imageSize - logoSize) / 2;

                    // Draw white background for logo
                    using var whitePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
                    int logoBgSize = logoSize + (pixelsPerModule * 2);
                    int logoBgX = (imageSize - logoBgSize) / 2;
                    int logoBgY = (imageSize - logoBgSize) / 2;
                    canvas.DrawRect(logoBgX, logoBgY, logoBgSize, logoBgSize, whitePaint);

                    // Draw logo
                    var destRect = new SKRect(logoX, logoY, logoX + logoSize, logoY + logoSize);
                    canvas.DrawBitmap(logoBitmap, destRect);
                }
            }

            // 4️⃣ Convert to PNG bytes
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}