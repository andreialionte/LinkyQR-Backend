namespace Linky.DTOs
{
    // Moved out of QRCodeController.cs when the QR code endpoints were converted
    // to Minimal APIs (see Endpoints/QRCodeEndpoints.cs), so it can still be
    // referenced without needing the (now commented-out) controller file.
    public class ScanRequestDto
    {
        public string ClientIp { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? City { get; set; }
    }
}

