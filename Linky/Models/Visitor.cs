namespace Linky.Models
{
    public class Visitor
    {
        public Guid Id { get; set; }
        public string Path { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Ip { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Referer { get; set; }
        public bool IsUnique { get; set; }
        public string? UserAgent { get; set; }

        //FK
        //public Guid SessionId { get; set; } = default!;

        //// Navigation property
        //public ActiveVisitor? ActiveVisitor { get; set; }
    }
}
