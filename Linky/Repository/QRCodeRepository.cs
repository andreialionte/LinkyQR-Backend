using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Microsoft.EntityFrameworkCore;
using QRCoder;
// using System.Drawing;
// using System.Drawing.Imaging;

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

            // 2️⃣ Generate QR code image (without logo support for now)
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCoder.QRCode(qrData);

            // COMMENTED OUT - System.Drawing graphics code that causes Gdip error on Linux
            // Bitmap? logoBitmap = null;
            // if (logoFile != null)
            // {
            //     using var logoStream = logoFile.OpenReadStream();
            //     logoBitmap = new Bitmap(logoStream);
            // }

            // using var qrBitmap = qrCode.GetGraphic(
            //     pixelsPerModule,
            //     System.Drawing.Color.Black,
            //     System.Drawing.Color.White,
            //     logoBitmap,
            //     iconSizePercent: 15,
            //     iconBorderWidth: 3,
            //     drawQuietZones: true
            // );

            // using var ms = new MemoryStream();
            // qrBitmap.Save(ms, ImageFormat.Png);
            // logoBitmap?.Dispose();

            // TEMPORARY: Return basic QR code without logo (PNG format)
            // TODO: Replace with SkiaSharp for cross-platform logo support
            using var basicQrBitmap = qrCode.GetGraphic(pixelsPerModule);
            using var ms = new MemoryStream();
            // basicQrBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

            // For now, return empty bytes to test if Gdip is the issue
            return ms.ToArray();
        }
    }
}