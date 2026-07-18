using Linky.DataLayer;
using Linky.DTOs;
using Linky.IRepository;
using Linky.IService;
using Linky.Models;
using Linky.Mappers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Linky.Repository
{
    public class URLShortenerRepository : IURLShortenerRepository
    {
        // Compiled EF Core queries for hot paths
        private static readonly Func<DataContextEf, string, CancellationToken, Task<URLShortener?>> _getByCodeCompiled
            = EF.CompileAsyncQuery((DataContextEf ctx, string code) =>
                ctx.URLShorteners.AsNoTracking().FirstOrDefault(u => u.ShortenedUrl == code));

        private readonly DataContextEf _context;
        private readonly ICacheService _cacheService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly URLShortenerMapper _mapper;

        public URLShortenerRepository(DataContextEf context, ICacheService cacheService, IHttpContextAccessor httpContextAccessor, URLShortenerMapper mapper)
        {
            _context = context;
            _cacheService = cacheService;
            _httpContextAccessor = httpContextAccessor;
            _mapper = mapper;
        }

        public async Task<URLShortener> CreateShortUrlAsync(URLShortenerDto dto, string? customAlias = null, CancellationToken cancellationToken = default)
        {
            //if the URL looks like a real web address
            if (!Uri.TryCreate(dto.OriginalUrl, UriKind.Absolute, out var validatedUri))
            {
                throw new ArgumentException("Please provide a full web address, like https://example.com.", nameof(dto.OriginalUrl));
            }

            // if it starts with http:// or https://
            if (validatedUri.Scheme != Uri.UriSchemeHttp && validatedUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("The address must start with http:// or https://.", nameof(dto.OriginalUrl));
            }

            // If custom alias provided, check if it exists first
            if (!string.IsNullOrWhiteSpace(customAlias))
            {
                var existingAlias = await _context.URLShorteners
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ShortenedUrl == customAlias, cancellationToken)
                    .ConfigureAwait(false);

                if (existingAlias != null)
                    return existingAlias;  // Alias already taken, return existing
            }

            // Only check for existing URL if NO custom alias is provided
            // If alias is provided, always create new entry
            if (string.IsNullOrWhiteSpace(customAlias))
            {
                var existing = await _context.URLShorteners
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.OriginalUrl == dto.OriginalUrl, cancellationToken)
                    .ConfigureAwait(false);

                if (existing != null)
                    return existing;  // Return existing URL
            }

            // Determine short code
            string shortCode = !string.IsNullOrWhiteSpace(customAlias) 
                ? customAlias 
                : GenerateShortCode(dto.OriginalUrl);

            // Get client IP from HttpContext
            var clientIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var entity = _mapper.FromDtoToEntity(dto, shortCode);
            entity.LastIp = clientIp;

            _context.URLShorteners.Add(entity);
            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // invalidate cache for lookup by code so next GetByCode returns fresh entity
            await _cacheService.RemoveAsync($"urlshortener:GetByCode:{entity.ShortenedUrl}", CancellationToken.None).ConfigureAwait(false);

            return entity; 
        }

        public async Task<URLShortener?> GetByCode(string code, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"urlshortener:GetByCode:{code}";
            var cached = await _cacheService.GetAsync<URLShortener>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached != null)
                return cached;

            var entity = await _getByCodeCompiled(_context, code, cancellationToken).ConfigureAwait(false);

            if (entity != null)
                await _cacheService.SetAsync(cacheKey, entity, TimeSpan.FromMinutes(30), cancellationToken).ConfigureAwait(false);

            return entity;
        }

        public async Task IncrementClickAsync(
            string code,
            string clientIp,
            string? country,
            string? city,
            string? userAgent,
            string? referrer,
            CancellationToken cancellationToken = default)
        {
            var entity = await _context.URLShorteners
                .FirstOrDefaultAsync(u => u.ShortenedUrl == code, cancellationToken)
                .ConfigureAwait(false);

            if (entity == null)
                return;

            entity.TotalClicks += 1;
            entity.LastIp = clientIp;
            entity.LastCountry = country;
            entity.LastCity = city;
            entity.UserAgent = userAgent;
            entity.Referrer = referrer;
            entity.ClickedAt = DateTime.UtcNow;

            // Non-idempotent write: do not cancel the actual DB save once we have started.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // invalidate cache
            await _cacheService.RemoveAsync($"urlshortener:GetByCode:{code}", CancellationToken.None).ConfigureAwait(false);
        }

        private static string GenerateShortCode(string input)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input + Guid.NewGuid()));
            ulong numericHash = BitConverter.ToUInt64(hashBytes, 0);
            return Base62Encode(numericHash);
        }

        private static string Base62Encode(ulong value)
        {
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var sb = new StringBuilder();
            do
            {
                sb.Insert(0, alphabet[(int)(value % 62)]);
                value /= 62;
            } while (value > 0);
            return sb.ToString();
        }
    }
}