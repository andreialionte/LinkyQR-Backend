// OLD MANUAL MAPPERS - KEPT FOR REFERENCE
// These were replaced with Mapperly source-generated mappers
// DO NOT USE - For reference only

/*
using Linky.DTOs;
using Linky.Models;

namespace Linky.Mappers;

public static class VisitorMapper
{
    public static VisitorDto ToDto(Visitor visitor)
    {
        return new VisitorDto(
            visitor.Id,
            visitor.Path,
            visitor.Timestamp,
            visitor.Ip,
            visitor.UserAgent,
            visitor.Referrer,
            visitor.Country,
            visitor.City
        );
    }

    public static Visitor ToModel(VisitorDto dto)
    {
        return new Visitor
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            Path = dto.Path,
            Timestamp = dto.Timestamp,
            Ip = dto.Ip,
            UserAgent = dto.UserAgent,
            Referrer = dto.Referrer,
            Country = dto.Country,
            City = dto.City
        };
    }
}

public static class VisitorStatsMapper
{
    public static VisitorStatsDto ToDto(VisitorStats stats)
    {
        return new VisitorStatsDto(
            stats.Date,
            stats.TotalVisits,
            stats.UniqueVisitors,
            new Dictionary<string, int>(stats.TopPages),
            new Dictionary<string, int>(stats.TopCountries)
        );
    }

    public static VisitorStats ToModel(VisitorStatsDto dto)
    {
        return new VisitorStats
        {
            Id = Guid.NewGuid(),
            Date = dto.Date,
            TotalVisits = dto.TotalVisits,
            UniqueVisitors = dto.UniqueVisitors,
            TopPages = new Dictionary<string, int>(dto.TopPages),
            TopCountries = new Dictionary<string, int>(dto.TopCountries)
        };
    }
}

public static class QRCodeMapper
{
    public static QRCode FromDto(QRCodeDto dto)
    {
        return new QRCode
        {
            Id = Guid.NewGuid(),
            Content = dto.Content,
            ExpirationDate = dto.ExpirationDate,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            LastScannedAt = DateTime.MinValue,
            ScanCount = 0
        };
    }

    public static QRCodeDto ToDto(QRCode model)
    {
        return new QRCodeDto(
            model.Content,
            model.ExpirationDate
        );
    }
}

public static class URLShortenerMapper
{
    public static URLShortener FromDtoToEntity(URLShortenerDto dto, string shortCode)
    {
        return new URLShortener
        {
            Id = Guid.NewGuid(),
            OriginalUrl = dto.OriginalUrl,
            ShortenedUrl = shortCode,
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

public static class ActiveVisitorMapping
{
    public static ActiveVisitorDto ToDto(ActiveVisitor model)
    {
        return new ActiveVisitorDto(
            model.Id,
            model.VisitorId,
            model.CurrentPath,
            model.LastSeenUtc,
            model.UserAgent,
            model.Ip
        );
    }

    public static ActiveVisitor ToModel(ActiveVisitorDto dto)
    {
        return new ActiveVisitor
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            VisitorId = dto.VisitorId,
            CurrentPath = dto.CurrentPath,
            LastSeenUtc = dto.LastSeenUtc,
            UserAgent = dto.UserAgent,
            Ip = dto.Ip
        };
    }
}
*/
