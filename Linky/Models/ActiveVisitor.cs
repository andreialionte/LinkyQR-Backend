namespace Linky.Models
{
    public class ActiveVisitor
    {
        public Guid SessionId { get; set; } = default!;
        public string? Ip { get; set; } = default!;
        public string? CurrentPath { get; set; }
        //public List<string?> PathHistory { get; set; } = new List<string?>();
        public DateTime LastSeenUtc { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? UserAgent { get; set; }
        // DELETED: public bool IsBot { get; set; }

        //NAV

        public Visitor? Visitor { get; set; }
    }
}