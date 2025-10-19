using System;
using System.Data;
using FluentMigrator;

namespace Linky.Migrations
{
    [Migration(20251002001)]
    public class CreateActiveVisitorMigration : Migration
    {
        public override void Up()
        {
            // ActiveVisitors table
            Create.Table("ActiveVisitors")
                .WithColumn("SessionId").AsString(128).PrimaryKey().NotNullable()
                .WithColumn("Ip").AsString(45).NotNullable()
                .WithColumn("CurrentPath").AsString(2048).Nullable()
                .WithColumn("LastSeenUtc").AsDateTime().NotNullable()
                .WithColumn("Country").AsString(100).Nullable()
                .WithColumn("City").AsString(100).Nullable()
                .WithColumn("UserAgent").AsString(1024).Nullable()
                .WithColumn("IsBot").AsBoolean().NotNullable().WithDefaultValue(false);

            // Path history normalized into a separate table
            Create.Table("ActiveVisitorPaths")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("SessionId").AsString(128).NotNullable()
                .WithColumn("Sequence").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Path").AsString(2048).NotNullable()
                .WithColumn("AddedAtUtc").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

            // Foreign key and indexes
            Create.ForeignKey("FK_ActiveVisitorPaths_ActiveVisitors")
                .FromTable("ActiveVisitorPaths").ForeignColumn("SessionId")
                .ToTable("ActiveVisitors").PrimaryColumn("SessionId")
                .OnDelete(Rule.Cascade);

            Create.Index("IX_ActiveVisitors_LastSeenUtc")
                .OnTable("ActiveVisitors").OnColumn("LastSeenUtc").Ascending();

            Create.Index("IX_ActiveVisitors_Ip")
                .OnTable("ActiveVisitors").OnColumn("Ip").Ascending();

            Create.Index("IX_ActiveVisitorPaths_SessionId")
                .OnTable("ActiveVisitorPaths").OnColumn("SessionId").Ascending();
        }

        public override void Down()
        {
            Delete.Table("ActiveVisitorPaths");
            Delete.Table("ActiveVisitors");
        }
    }
}

/*
Notes:
- I normalized PathHistory into a separate table (ActiveVisitorPaths) which allows efficient queries, limits row size issues, and supports indexing.
- Alternative: if you prefer to keep PathHistory as JSON inside ActiveVisitors, replace the PathHistory table with a single column:
    .WithColumn("PathHistoryJson").AsCustom("jsonb").Nullable()
  (use AsCustom("jsonb") for PostgreSQL; for SQL Server use AsString(int.MaxValue) and store JSON text.)
- Adjust column lengths to match your needs.
*/
