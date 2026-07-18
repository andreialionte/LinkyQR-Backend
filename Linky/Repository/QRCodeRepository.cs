using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Mappers;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.Buffers;
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

        public async Task<Linky.Models.QRCode> CreateAsync(QRCodeDto dto, CancellationToken cancellationToken = default)
        {
            var entity = _mapper.FromDto(dto);

            _context.QRCodes.Add(entity);
            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // invalidate cached lists and content lookups so new QR appears in lists
            await _cacheService.RemoveAsync("QRCode:All", CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(entity.Content))
                await _cacheService.RemoveAsync($"QRCode:Content:{entity.Content}", CancellationToken.None).ConfigureAwait(false);

            return entity;
        }

        public async Task<Linky.Models.QRCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"QRCode:{id}";

            var cachedVal = await _cacheService.GetAsync<Linky.Models.QRCode>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cachedVal != null) return cachedVal;

            var entity = await _context.QRCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken).ConfigureAwait(false);

            if (entity != null)
                await _cacheService.SetAsync(cacheKey, entity, TimeSpan.FromMinutes(20), cancellationToken).ConfigureAwait(false);

            return entity;
        }

        public async Task<Linky.Models.QRCode?> GetByContentAsync(string content, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"QRCode:Content:{content}";

            var cachedVal = await _cacheService.GetAsync<Linky.Models.QRCode>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cachedVal != null) return cachedVal;

            var entity = await _context.QRCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Content == content, cancellationToken).ConfigureAwait(false);

            if (entity != null)
                await _cacheService.SetAsync(cacheKey, entity, TimeSpan.FromMinutes(20), cancellationToken).ConfigureAwait(false);

            return entity;
        }

        public async Task<IEnumerable<Linky.Models.QRCode>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "QRCode:All";

            var cachedVal = await _cacheService.GetAsync<IEnumerable<Linky.Models.QRCode>>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cachedVal != null) return cachedVal;

            var list = await _context.QRCodes
                .AsNoTracking()
                .OrderByDescending(q => q.CreatedAt)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            await _cacheService.SetAsync(cacheKey, list, TimeSpan.FromMinutes(20), cancellationToken).ConfigureAwait(false);
            return list;
        }

        public async Task IncrementScanAsync(Guid id, string clientIp, string? country, string? city, CancellationToken cancellationToken = default)
        {
            var qrCode = await _context.QRCodes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken).ConfigureAwait(false);

            if (qrCode == null) throw new InvalidOperationException($"QR code with ID {id} not found");
            if (!qrCode.IsActive) throw new InvalidOperationException($"QR code with ID {id} is not active");
            if (qrCode.ExpirationDate.HasValue && qrCode.ExpirationDate < DateTime.UtcNow)
                throw new InvalidOperationException($"QR code with ID {id} has expired");

            qrCode.ScanCount++;
            qrCode.LastScannedAt = DateTime.UtcNow;
            qrCode.LastIp = clientIp;
            qrCode.LastCountry = country;
            qrCode.LastCity = city;

            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // Invalidate cache
            var cacheKey = $"QRCode:{id}";
            await _cacheService.RemoveAsync(cacheKey, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task UpdateContentAsync(Guid id, string content, CancellationToken cancellationToken = default)
        {
            var qrCode = await _context.QRCodes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken).ConfigureAwait(false);
            if (qrCode == null) throw new InvalidOperationException($"QR code with ID {id} not found");

            var oldContent = qrCode.Content;
            qrCode.Content = content;
            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // invalidate cache entries for id, old content (if any), new content and list
            var cacheKey = $"QRCode:{id}";
            await _cacheService.RemoveAsync(cacheKey, CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(oldContent))
                await _cacheService.RemoveAsync($"QRCode:Content:{oldContent}", CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(content))
                await _cacheService.RemoveAsync($"QRCode:Content:{content}", CancellationToken.None).ConfigureAwait(false);
            await _cacheService.RemoveAsync("QRCode:All", CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<byte[]> GenerateQrCodeImageAsync(string text, IFormFile? logoFile = null, int pixelsPerModule = 20, CancellationToken cancellationToken = default)
        {
            // --- 1. READ LOGO BYTES VIA ARRAYPOOL (avoids MemoryStream + ToArray double-alloc) ---
            byte[]? logoBuffer = null;
            int logoLength = 0;

            if (logoFile != null && logoFile.Length > 0)
            {
                logoLength = (int)logoFile.Length;
                logoBuffer = ArrayPool<byte>.Shared.Rent(logoLength);
                using var stream = logoFile.OpenReadStream();
                // ReadExactlyAsync guarantees the full buffer is filled; plain ReadAsync can return fewer bytes.
                await stream.ReadExactlyAsync(logoBuffer.AsMemory(0, logoLength), cancellationToken).ConfigureAwait(false);
            }

            try
            {
                // --- 2. HASH INPUTS WITH ZERO HEAP ALLOC (Span + stackalloc + IncrementalHash) ---
                var plainText = text ?? string.Empty;
                int textByteCount = Encoding.UTF8.GetByteCount(plainText);

                // stackalloc for small strings; fallback to heap for large ones
                Span<byte> textBytes = textByteCount <= 512
                    ? stackalloc byte[textByteCount]
                    : new byte[textByteCount];
                Encoding.UTF8.GetBytes(plainText, textBytes);

                // SHA-256 output is always exactly 32 bytes — stack-allocate it
                Span<byte> hashBytes = stackalloc byte[32];

                using (var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    ih.AppendData(textBytes);

                    Span<byte> pixelBytes = stackalloc byte[sizeof(int)];
                    BitConverter.TryWriteBytes(pixelBytes, pixelsPerModule);
                    ih.AppendData(pixelBytes);

                    if (logoLength > 0)
                        ih.AppendData(logoBuffer!.AsSpan(0, logoLength));

                    ih.GetHashAndReset(hashBytes);
                }

                string cacheKey = "qrgenerator:svg:" + Convert.ToHexString(hashBytes);

                // --- 3. CACHE LOOKUP ---
                var cached = await _cacheService.GetAsync<byte[]>(cacheKey, cancellationToken).ConfigureAwait(false);
                if (cached != null && cached.Length > 0)
                    return cached;

                // Bail out early if the client already disconnected before doing CPU work
                cancellationToken.ThrowIfCancellationRequested();

                // --- 4. GENERATE SVG ---
                using var qrGenerator = new QRCodeGenerator();
                using var qrData = qrGenerator.CreateQrCode(plainText, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new SvgQRCode(qrData);

                string svgString = qrCode.GetGraphic(pixelsPerModule, "#000000", "#ffffff", true);
                var bytes = Encoding.UTF8.GetBytes(svgString);

                // Cache with CancellationToken.None: CPU work is already done — persist the result
                // even if the client disconnected so the next caller benefits from the cache.
                await _cacheService.SetAsync(cacheKey, bytes, TimeSpan.FromHours(1), CancellationToken.None).ConfigureAwait(false);

                return bytes;
            }
            finally
            {
                // --- 5. ALWAYS RETURN THE RENTED BUFFER ---
                if (logoBuffer != null)
                    ArrayPool<byte>.Shared.Return(logoBuffer);
            }
        }
    }
}