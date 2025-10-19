using System;
using FluentMigrator;

namespace Linky.Migrations
{
    [Migration(20251002005)]
    public class CreateURLShortenerMigration : Migration
    {
        public override void Up()
        {
            Create.Table("URLShorteners")
                .WithColumn("Id").AsGuid().PrimaryKey().NotNullable()
                .WithColumn("OriginalUrl").AsString(2048).NotNullable()
                .WithColumn("ShortenedUrl").AsString(255).NotNullable()
                .WithColumn("CreatedAt").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
                .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("CampaignName").AsString(200).Nullable()
                .WithColumn("UTMParameters").AsString(1024).Nullable();

            // Unique index on ShortenedUrl to ensure no duplicates
            Create.Index("IX_URLShorteners_ShortenedUrl")
                .OnTable("URLShorteners").OnColumn("ShortenedUrl").Ascending().WithOptions().Unique();

            Create.Index("IX_URLShorteners_CreatedAt")
                .OnTable("URLShorteners").OnColumn("CreatedAt").Descending();

            Create.Index("IX_URLShorteners_IsActive")
                .OnTable("URLShorteners").OnColumn("IsActive").Ascending();
        }

        public override void Down()
        {
            Delete.Table("URLShorteners");
        }
    }
}

/*
Notes and considerations:
- `ShortenedUrl` is enforced UNIQUE here; if you plan to store the full short URL (including domain), consider normalizing domain vs path.
- `OriginalUrl` uses 2048 length to be tolerant of long URLs; adjust if you target a DB with different limits.
- `CreatedAt` defaults to DB current UTC time; change if you prefer application-side timestamps.
- If you anticipate multi-tenant data, add a `TenantId` column and include it in relevant indexes.
- If you want an integer identity PK instead of GUID, replace `AsGuid()` with `AsInt32().PrimaryKey().Identity()`.
*/
