namespace Linky.Models
{
    public class VisitorStats
    {
        public Guid Id { get; set; }
        public DateOnly Date { get; set; }
        public int TotalVisits { get; set; }
        public int UniqueVisitors { get; set; }
        public Dictionary<string, int> TopPages { get; set; }
        public Dictionary<string, int> TopCountries { get; set; }
    }
}
