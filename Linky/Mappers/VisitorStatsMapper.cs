using Linky.Models;
using Linky.DTOs;

namespace Linky.Mappers
{
    public static class VisitorStatsMapper
    {
        public static VisitorStatsDto ToDto(VisitorStats stats) =>
            new(
                stats.Date,
                stats.TotalVisits,
                stats.UniqueVisitors,
                new Dictionary<string, int>(stats.TopPages),
                new Dictionary<string, int>(stats.TopCountries)
            );

        public static VisitorStats ToModel(VisitorStatsDto dto) =>
            new VisitorStats
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
