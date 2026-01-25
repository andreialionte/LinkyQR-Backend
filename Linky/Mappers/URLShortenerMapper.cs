using Linky.DTOs;
using Linky.Models;
using Riok.Mapperly.Abstractions;

namespace Linky.Mappers;

[Mapper]
public partial class URLShortenerMapper
{
    public URLShortener FromDtoToEntity(URLShortenerDto dto, string shortenedUrl)
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
            UserAgent = dto.UserAgent,
            Referrer = dto.Referrer,
            ClickedAt = DateTime.UtcNow,
            TotalClicks = 0
        };
    }
}
