using Linky.DTOs;
using Linky.Models;

namespace Linky.IRepository
{
    public interface IURLShortenerRepository
    {

        Task<URLShortener> CreateShortUrlAsync(URLShortenerDto dto, string? customAlias = null);
        Task<URLShortener?> GetByCode(string code);
        //Task<URLShortener?> GetByOriginalUrl(string originalUrl);
        // Total Link Created
        //Task<int> TotalLinksCreated();
        // Total clicks
        //Task<int> TotalClicksOnAllLinks();
        Task IncrementClickAsync(
            string code,
            string clientIp,
            string? country,
            string? city,
            string? userAgent,
            string? referrer);
    }
}
