using Linky.Models;
using Microsoft.EntityFrameworkCore;

namespace Linky.DataLayer
{
    public class DataContextEf : DbContext
    {
        public DataContextEf(DbContextOptions<DataContextEf> options)
            : base(options)
        {
        }

        // ✅ Optional: fallback config for EF tools (so Add-Migration works even without full app startup)

        // DbSets
        public DbSet<URLShortener> URLShorteners { get; set; } = default!;
        public DbSet<Visitor> Visitors { get; set; } = default!;
        public DbSet<ActiveVisitor> ActiveVisitors { get; set; } = default!;
        public DbSet<QRCode> QRCodes { get; set; } = default!;
        public DbSet<VisitorStats> VisitorStats { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // URLShortener
            modelBuilder.Entity<URLShortener>(entity =>
            {
                entity.ToTable("UrlShorteners");
                entity.HasKey(u => u.Id);
                entity.Property(u => u.OriginalUrl).IsRequired();
                entity.Property(u => u.ShortenedUrl).IsRequired();
                entity.Property(u => u.CreatedAt).HasDefaultValueSql("NOW()");
                entity.Property(u => u.IsActive).HasDefaultValue(true);
                entity.Property(u => u.LastIp).IsRequired();
                entity.Property(u => u.TotalClicks).HasDefaultValue(0);
                entity.Property(u => u.LastCountry);
                entity.Property(u => u.LastCity);
                entity.Property(u => u.UserAgent);
                entity.Property(u => u.Referrer);
                entity.Property(u => u.ClickedAt).HasDefaultValueSql("NOW()");
            });

            // Visitor
            modelBuilder.Entity<Visitor>(entity =>
            {
                entity.ToTable("Visitors");
                entity.HasKey(v => v.Id);
                entity.Property(v => v.Path).IsRequired();
                entity.Property(v => v.Timestamp).HasDefaultValueSql("NOW()");
                entity.Property(v => v.Ip);
                entity.Property(v => v.Country);
                entity.Property(v => v.City);
                entity.Property(v => v.Referer);
                entity.Property(v => v.UserAgent);
                entity.Property(v => v.IsUnique).HasDefaultValue(true);

                //entity.Property(v => v.SessionId).IsRequired();

                //entity.HasOne(v => v.ActiveVisitor)
                //      .WithOne(a => a.Visitor)
                //      .HasForeignKey<Visitor>(v => v.SessionId)
                //      .OnDelete(DeleteBehavior.Cascade);
            });

            // ActiveVisitor
            modelBuilder.Entity<ActiveVisitor>(entity =>
            {
                entity.ToTable("ActiveVisitors");
                entity.HasKey(a => a.SessionId);
                entity.Property(a => a.Ip);
                entity.Property(a => a.CurrentPath);
                entity.Property(a => a.LastSeenUtc).HasDefaultValueSql("NOW()");
                entity.Property(a => a.Country);
                entity.Property(a => a.City);
                entity.Property(a => a.UserAgent);
            });

            // QRCode
            modelBuilder.Entity<QRCode>(entity =>
            {
                entity.ToTable("QRCodes");
                entity.HasKey(q => q.Id);
                entity.Property(q => q.CreatedAt).HasDefaultValueSql("NOW()");
                entity.Property(q => q.LastScannedAt).HasDefaultValueSql("NOW()");
                entity.Property(q => q.ScanCount).HasDefaultValue(0);
                entity.Property(q => q.IsActive).HasDefaultValue(true);
                entity.Property(q => q.ExpirationDate);
                entity.Property(q => q.LastIp);
                entity.Property(q => q.LastCountry);
                entity.Property(q => q.LastCity);
                entity.Property(q => q.LastUsedAt);
            });

            // VisitorStats
            modelBuilder.Entity<VisitorStats>(entity =>
            {
                entity.ToTable("VisitorStats");
                entity.HasKey(vs => vs.Id);
                entity.Property(vs => vs.Date).IsRequired();
                entity.Property(vs => vs.TotalVisits).HasDefaultValue(0);
                entity.Property(vs => vs.UniqueVisitors).HasDefaultValue(0);

                entity.Property(vs => vs.TopPages)
                      .HasColumnType("jsonb")
                      .HasDefaultValueSql("'{}'::jsonb");

                entity.Property(vs => vs.TopCountries)
                      .HasColumnType("jsonb")
                      .HasDefaultValueSql("'{}'::jsonb");
            });
        }
    }
}
