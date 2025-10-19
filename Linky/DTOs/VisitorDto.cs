namespace Linky.DTOs
{ // https://vitorafgomes.medium.com/automapper-vs-manual-mapping-in-net-c7b2e81a199c
    public readonly record struct VisitorDto(
        Guid Id,
        string Path,
        DateTime Timestamp,
        string? Ip,
        string? Country,
        string? City,
        string? Referer,
        bool IsUnique,
        string? UserAgent,
        Guid SessionId
    );
}
