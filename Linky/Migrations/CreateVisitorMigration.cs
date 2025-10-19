using System;
using FluentMigrator;

namespace Linky.Migrations
{
    [Migration(20251002006)]
    public class CreateVisitorMigration : Migration
    {
        public override void Up()
        {
            Create.Table("Visitors")
                .WithColumn("Id").AsGuid().PrimaryKey().NotNullable()
                .WithColumn("Path").AsString(2048).NotNullable()
                .WithColumn("Timestamp").AsDateTime().NotNullable()
                .WithColumn("Ip").AsString(45).NotNullable()
                .WithColumn("Country").AsString(100).Nullable()
                .WithColumn("City").AsString(100).Nullable()
                .WithColumn("Referer").AsString(2048).Nullable()
                .WithColumn("IsUnique").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("UserAgent").AsString(1024).Nullable();

            // Indexes for common queries
            Create.Index("IX_Visitors_Timestamp")
                .OnTable("Visitors").OnColumn("Timestamp").Descending();

            Create.Index("IX_Visitors_Ip")
                .OnTable("Visitors").OnColumn("Ip").Ascending();

            Create.Index("IX_Visitors_Path")
                .OnTable("Visitors").OnColumn("Path").Ascending();

            Create.Index("IX_Visitors_IsUnique")
                .OnTable("Visitors").OnColumn("IsUnique").Ascending();

            // Composite index to speed up queries like "unique visitors per IP in time window"
            Create.Index("IX_Visitors_Ip_Timestamp")
    .OnTable("Visitors")
    .OnColumn("Ip").Ascending()
    .OnColumn("Timestamp").Descending();

            ;
        }

        public override void Down()
        {
            Delete.Index("IX_Visitors_Ip_Timestamp").OnTable("Visitors");
            Delete.Index("IX_Visitors_IsUnique").OnTable("Visitors");
            Delete.Index("IX_Visitors_Path").OnTable("Visitors");
            Delete.Index("IX_Visitors_Ip").OnTable("Visitors");
            Delete.Index("IX_Visitors_Timestamp").OnTable("Visitors");
            Delete.Table("Visitors");
        }
    }
}

/* Notes:
 - `Path` and `Referer` use generous lengths (2048) to safely store long URLs; adjust to taste.
 - `IsUnique` is a boolean marker; you likely want to compute uniqueness at ingestion (e.g., via dedupe logic using SessionId or IP+fingerprint) rather than rely solely on DB constraints.
 - If you expect extremely high write volume, consider partitioning by date, using a separate summary table for aggregates, or writing events to a log store before batching into the DB.
 - If you prefer `Id` as int identity or storing `Timestamp` as UTC-specific DB type, I can change that.
*/
