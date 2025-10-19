using Linky.DTOs;
using Linky.Models;

namespace Linky.Mappers
{
    public static class URLShortenerMapper
    {
        public static URLShortener FromDtoToEntity(URLShortenerDto dto, string shortenedUrl)
        {
            return new URLShortener
            {
                Id = Guid.NewGuid(),
                OriginalUrl = dto.OriginalUrl,
                ShortenedUrl = shortenedUrl,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                LastIp = dto.ClientIp ?? "unknown",
                LastCountry = dto.Country,
                LastCity = dto.City,
                ClickedAt = DateTime.UtcNow,
                UserAgent = dto.UserAgent,
                Referrer = dto.Referrer,
                TotalClicks = 0
            };
        }
    }
}
