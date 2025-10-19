namespace Linky.DTOs
{
    public readonly record struct QRCodeDto(
        string Content, // The URL or text to encode
        DateTime? ExpirationDate = null
    );
}
