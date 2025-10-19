namespace Linky.DTOs
{
    public readonly record struct ActiveVisitorDto(
        Guid SessionId,
        string Ip,
        string? CurrentPath,
        //IReadOnlyList<string> PathHistory,
        DateTime LastSeenUtc,
        string? Country,
        string? City,
        string? UserAgent
    );
}
