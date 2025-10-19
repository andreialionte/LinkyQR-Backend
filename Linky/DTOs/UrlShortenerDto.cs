namespace Linky.DTOs
{
    public readonly record struct URLShortenerDto(
        string OriginalUrl,
        string? ClientIp = null,
        string? Country = null,
        string? City = null,
        string? UserAgent = null,
        string? Referrer = null
    );
}
