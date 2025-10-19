using System;
using FluentMigrator;

namespace Linky.Migrations
{
    [Migration(20251002004)]
    public class CreateURLClickEventMigration : Migration
    {
        public override void Up()
        {
            // Detailed click events table
            Create.Table("URLClickEvents")
                .WithColumn("Id").AsGuid().PrimaryKey().NotNullable()
                .WithColumn("URLShortenerId").AsGuid().NotNullable()
                .WithColumn("LastIp").AsString(45).NotNullable()
                .WithColumn("LastCountry").AsString(100).Nullable()
                .WithColumn("LastCity").AsString(100).Nullable()
                .WithColumn("ClickedAt").AsDateTime().NotNullable()
                .WithColumn("UserAgent").AsString(1024).Nullable()
                .WithColumn("Referrer").AsString(2048).Nullable()
                // TotalClicks on a per-event row is unusual; default to 1 to represent a single click if you choose to keep it.
                .WithColumn("TotalClicks").AsInt32().NotNullable().WithDefaultValue(1);

            // FK to URLShorteners (assumes table exists). If it doesn't, remove or adapt this FK.
            Create.ForeignKey("FK_URLClickEvents_URLShorteners")
                .FromTable("URLClickEvents").ForeignColumn("URLShortenerId")
                .ToTable("URLShorteners").PrimaryColumn("Id")
                .OnDelete(System.Data.Rule.Cascade);

            // Indexes for common queries
            Create.Index("IX_URLClickEvents_URLShortenerId")
                .OnTable("URLClickEvents").OnColumn("URLShortenerId").Ascending();

            Create.Index("IX_URLClickEvents_ClickedAt")
                .OnTable("URLClickEvents").OnColumn("ClickedAt").Descending();

            Create.Index("IX_URLClickEvents_TotalClicks")
                .OnTable("URLClickEvents").OnColumn("TotalClicks").Descending();
        }

        public override void Down()
        {
            Delete.Table("URLClickEvents");
        }
    }
}
