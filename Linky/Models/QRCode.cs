namespace Linky.Models
{
    public class QRCode
    {
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;  // <-- add content
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public bool IsActive { get; set; }

        // OLD: public ICollection<QRCodeScanEvent> ScanEvents { get; set; } = new List<QRCodeScanEvent>();
        public DateTime LastScannedAt { get; set; }
        public int ScanCount { get; set; }
        public string? LastIp { get; set; }
        public string? LastCountry { get; set; }
        public string? LastCity { get; set; }
        //public QRCodeScanEvent? ScanEvent { get; set; }
    }
}