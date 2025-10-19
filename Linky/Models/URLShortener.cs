namespace Linky.Models
{
    public class URLShortener
    {
        public Guid Id { get; set; }
        public required string OriginalUrl { get; set; }
        public required string ShortenedUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public required string LastIp { get; set; }
        public string? LastCountry { get; set; }
        public string? LastCity { get; set; }
        public DateTime ClickedAt { get; set; }
        public string? UserAgent { get; set; }
        public string? Referrer { get; set; }
        public int TotalClicks { get; set; }
        //public string? CampaignName { get; set; }
        //public string? UTMParameters { get; set; } // in the future with account login/register ( OTP ONE TIME CODE LOGIN/REGISTER, to not have passwords)
        // ADDED THIS LINE:
        // public URLClickEvent? ClickEvent { get; set; }
    }
}