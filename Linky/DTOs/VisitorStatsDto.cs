namespace Linky.DTOs
{  // https://vitorafgomes.medium.com/automapper-vs-manual-mapping-in-net-c7b2e81a199c
    public readonly record struct VisitorStatsDto(
        DateOnly Date,
        int TotalVisits,
        int UniqueVisitors,
        Dictionary<string, int> TopPages,
        Dictionary<string, int> TopCountries
    );
}
