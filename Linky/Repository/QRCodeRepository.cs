using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

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

        public async Task<Linky.Models.QRCode?> GetByContentAsync(string content)
        {
            var cacheKey = $"QRCode:Content:{content}";

            var cachedVal = await _cacheService.GetAsync<Linky.Models.QRCode>(cacheKey);
            if (cachedVal != null) return cachedVal;

            var entity = await _context.QRCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Content == content);

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

        public async Task UpdateContentAsync(Guid id, string content)
        {
            var qrCode = await _context.QRCodes.FirstOrDefaultAsync(q => q.Id == id);
            if (qrCode == null) throw new InvalidOperationException($"QR code with ID {id} not found");

            qrCode.Content = content;
            await _context.SaveChangesAsync();

            // Invalidate cache
            var cacheKey = $"QRCode:{id}";
            await _cacheService.RemoveAsync(cacheKey);
        }

        public async Task<byte[]> GenerateQrCodeImageAsync(string text, IFormFile? logoFile = null, int pixelsPerModule = 20)
        {
            // Build deterministic cache key from text + logo content (if any) + pixel size
            byte[] logoBytes = Array.Empty<byte>();
            if (logoFile != null)
            {
                using var ms = new MemoryStream();
                await logoFile.CopyToAsync(ms);
                logoBytes = ms.ToArray();
            }

            // compute SHA256 over combined inputs
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                // text bytes
                var textBytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                sha.TransformBlock(textBytes, 0, textBytes.Length, null, 0);

                // pixels
                var pixelBytes = BitConverter.GetBytes(pixelsPerModule);
                sha.TransformBlock(pixelBytes, 0, pixelBytes.Length, null, 0);

                // logo
                if (logoBytes.Length > 0)
                {
                    sha.TransformBlock(logoBytes, 0, logoBytes.Length, null, 0);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = sha.Hash ?? Array.Empty<byte>();
            }

            string cacheKey = "qrgenerator:svg:" + Convert.ToHexString(hash);

            var cached = await _cacheService.GetAsync<byte[]>(cacheKey);
            if (cached != null && cached.Length > 0)
            {
                return cached;
            }

            // Generate QR code as SVG (no native dependencies!)
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new SvgQRCode(qrData);

            string svgString = qrCode.GetGraphic(
                pixelsPerModule,
                "#000000",
                "#ffffff",
                true
            );

            var bytes = Encoding.UTF8.GetBytes(svgString);

            // cache the produced SVG bytes (shared globally if same inputs)
            await _cacheService.SetAsync(cacheKey, bytes, TimeSpan.FromHours(1));

            return bytes;
        }
    }
}