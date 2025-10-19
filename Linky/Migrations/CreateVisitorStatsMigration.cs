using System;
using FluentMigrator;

namespace Linky.Migrations
{
    [Migration(20251002007)]
    public class CreateVisitorStatsMigration : Migration
    {
        public override void Up()
        {
            Create.Table("VisitorStats")
                .WithColumn("Id").AsGuid().PrimaryKey().NotNullable()
                .WithColumn("Date").AsDate().NotNullable()
                .WithColumn("TotalVisits").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("UniqueVisitors").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TopPages").AsCustom("jsonb").Nullable() // JSON (Postgres) for page counts
                .WithColumn("TopCountries").AsCustom("jsonb").Nullable(); // JSON (Postgres) for country counts

            Create.Index("IX_VisitorStats_Date")
                .OnTable("VisitorStats").OnColumn("Date").Ascending().WithOptions().Unique();
        }

        public override void Down()
        {
            Delete.Index("IX_VisitorStats_Date").OnTable("VisitorStats");
            Delete.Table("VisitorStats");
        }
    }
}

/* Notes:
 - `TopPages` and `TopCountries` are stored as JSON (`jsonb`) for Postgres. If you target SQL Server, switch to NVARCHAR(MAX).
 - Index on `Date` ensures fast daily lookup and uniqueness (1 row per day).
 - For large-scale analytics, you might prefer separate detail tables instead of JSON aggregation, but JSON is good for flexible schema.
 - If you want incremental aggregation, enforce `Date` + `TenantId` uniqueness if multi-tenant.
*/
