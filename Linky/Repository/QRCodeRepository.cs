using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
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
        private readonly QRCodeMapper _mapper;

        public QRCodeRepository(DataContextEf context, ICacheService cacheService, QRCodeMapper mapper)
        {
            _context = context;
            _cacheService = cacheService;
            _mapper = mapper;
        }

        public async Task<Linky.Models.QRCode> CreateAsync(QRCodeDto dto)
        {
            var entity = _mapper.FromDto(dto);

            _context.QRCodes.Add(entity);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            // invalidate cached lists and content lookups so new QR appears in lists
            await _cacheService.RemoveAsync("QRCode:All").ConfigureAwait(false);
            if (!string.IsNullOrEmpty(entity.Content))
                await _cacheService.RemoveAsync($"QRCode:Content:{entity.Content}").ConfigureAwait(false);

            return entity;
        }

        public async Task<Linky.Models.QRCode?> GetByIdAsync(Guid id)
        {
            var cacheKey = $"QRCode:{id}";

            var cachedVal = await _cacheService.GetAsync<Linky.Models.QRCode>(cacheKey).ConfigureAwait(false);
            if (cachedVal != null) return cachedVal;

            var entity = await _context.QRCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == id).ConfigureAwait(false);

            if (entity != null)
                await _cacheService.SetAsync(cacheKey, entity, TimeSpan.FromMinutes(20)).ConfigureAwait(false);

            return entity;
        }

        public async Task<Linky.Models.QRCode?> GetByContentAsync(string content)
        {
            var cacheKey = $"QRCode:Content:{content}";

            var cachedVal = await _cacheService.GetAsync<Linky.Models.QRCode>(cacheKey).ConfigureAwait(false);
            if (cachedVal != null) return cachedVal;

            var entity = await _context.QRCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Content == content).ConfigureAwait(false);

            if (entity != null)
                await _cacheService.SetAsync(cacheKey, entity, TimeSpan.FromMinutes(20)).ConfigureAwait(false);

            return entity;
        }

        public async Task<IEnumerable<Linky.Models.QRCode>> GetAllAsync()
        {
            const string cacheKey = "QRCode:All";

            var cachedVal = await _cacheService.GetAsync<IEnumerable<Linky.Models.QRCode>>(cacheKey).ConfigureAwait(false);
            if (cachedVal != null) return cachedVal;

            var list = await _context.QRCodes
                .AsNoTracking()
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync().ConfigureAwait(false);

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromMinutes(20)).ConfigureAwait(false);
            return list;
        }

        public async Task IncrementScanAsync(Guid id, string clientIp, string? country, string? city)
        {
            var qrCode = await _context.QRCodes.FirstOrDefaultAsync(q => q.Id == id).ConfigureAwait(false);

            if (qrCode == null) throw new InvalidOperationException($"QR code with ID {id} not found");
            if (!qrCode.IsActive) throw new InvalidOperationException($"QR code with ID {id} is not active");
            if (qrCode.ExpirationDate.HasValue && qrCode.ExpirationDate < DateTime.UtcNow)
                throw new InvalidOperationException($"QR code with ID {id} has expired");

            qrCode.ScanCount++;
            qrCode.LastScannedAt = DateTime.UtcNow;
            qrCode.LastIp = clientIp;
            qrCode.LastCountry = country;
            qrCode.LastCity = city;

            await _context.SaveChangesAsync().ConfigureAwait(false);

            // Invalidate cache
            var cacheKey = $"QRCode:{id}";
            await _cacheService.RemoveAsync(cacheKey).ConfigureAwait(false);
        }

        public async Task UpdateContentAsync(Guid id, string content)
        {
            var qrCode = await _context.QRCodes.FirstOrDefaultAsync(q => q.Id == id).ConfigureAwait(false);
            if (qrCode == null) throw new InvalidOperationException($"QR code with ID {id} not found");

            var oldContent = qrCode.Content;
            qrCode.Content = content;
            await _context.SaveChangesAsync().ConfigureAwait(false);

            // invalidate cache entries for id, old content (if any), new content and list
            var cacheKey = $"QRCode:{id}";
            await _cacheService.RemoveAsync(cacheKey).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(oldContent))
                await _cacheService.RemoveAsync($"QRCode:Content:{oldContent}").ConfigureAwait(false);
            if (!string.IsNullOrEmpty(content))
                await _cacheService.RemoveAsync($"QRCode:Content:{content}").ConfigureAwait(false);
            await _cacheService.RemoveAsync("QRCode:All").ConfigureAwait(false);
        }

        public async Task<byte[]> GenerateQrCodeImageAsync(string text, IFormFile? logoFile = null, int pixelsPerModule = 20)
        {
            // Build deterministic cache key from text + logo content (if any) + pixel size
            byte[] logoBytes = Array.Empty<byte>();
            if (logoFile != null)
            {
                using var ms = new MemoryStream();
                await logoFile.CopyToAsync(ms).ConfigureAwait(false);
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

             var cached = await _cacheService.GetAsync<byte[]>(cacheKey).ConfigureAwait(false);
             if (cached != null && cached.Length > 0)
             {
                 return cached;
             }

            // Generate QR code as SVG (no native dependencies!)
            using var qrGenerator = new QRCodeGenerator();
            var plainText = text ?? string.Empty;
            using var qrData = qrGenerator.CreateQrCode(plainText, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new SvgQRCode(qrData);

            string svgString = qrCode.GetGraphic(
                pixelsPerModule,
                "#000000",
                "#ffffff",
                true
            );

            var bytes = Encoding.UTF8.GetBytes(svgString);

            // cache the produced SVG bytes (shared globally if same inputs)
             await _cacheService.SetAsync(cacheKey, bytes, TimeSpan.FromHours(1)).ConfigureAwait(false);

            return bytes;
        }
    }
}