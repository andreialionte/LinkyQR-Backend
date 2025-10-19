using System;
using FluentMigrator;

namespace Linky.Migrations
{
    [Migration(20251002002)]
    public class CreateQRCodeMigration : Migration
    {
        public override void Up()
        {
            // QRCodes table
            Create.Table("QRCodes")
                .WithColumn("Id").AsGuid().PrimaryKey().NotNullable()
                .WithColumn("CreatedAt").AsDateTime().NotNullable()
                .WithColumn("LastUsedAt").AsDateTime().Nullable()
                .WithColumn("ExpirationDate").AsDateTime().Nullable()
                .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true);

            // Scan events table (normalized)
            Create.Table("QRCodeScanEvents")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("QRCodeId").AsGuid().NotNullable()
                .WithColumn("ScannedAtUtc").AsDateTime().NotNullable()
                .WithColumn("Ip").AsString(45).Nullable()
                .WithColumn("UserAgent").AsString(1024).Nullable()
                .WithColumn("Referrer").AsString(2048).Nullable()
                .WithColumn("Country").AsString(100).Nullable()
                .WithColumn("City").AsString(100).Nullable()
                .WithColumn("Path").AsString(2048).Nullable()
                .WithColumn("IsBot").AsBoolean().NotNullable().WithDefaultValue(false);

            // Foreign key and indexes
            Create.ForeignKey("FK_QRCodeScanEvents_QRCodes")
                .FromTable("QRCodeScanEvents").ForeignColumn("QRCodeId")
                .ToTable("QRCodes").PrimaryColumn("Id")
                .OnDelete(System.Data.Rule.Cascade);

            Create.Index("IX_QRCodes_LastUsedAt")
                .OnTable("QRCodes").OnColumn("LastUsedAt").Ascending();

            Create.Index("IX_QRCodes_IsActive")
                .OnTable("QRCodes").OnColumn("IsActive").Ascending();

            Create.Index("IX_QRCodeScanEvents_QRCodeId")
                .OnTable("QRCodeScanEvents").OnColumn("QRCodeId").Ascending();

            Create.Index("IX_QRCodeScanEvents_ScannedAtUtc")
                .OnTable("QRCodeScanEvents").OnColumn("ScannedAtUtc").Ascending();
        }

        public override void Down()
        {
            Delete.Table("QRCodeScanEvents");
            Delete.Table("QRCodes");
        }
    }
}

/*
Notes:
- Scan events are split into a separate table for performance and to avoid unbounded collection sizes on the main QRCode row.
- If you prefer to keep scan metadata as JSON inside QRCodes, you can replace the ScanEvents table with a single column:
    .WithColumn("ScanEventsJson").AsCustom("jsonb").Nullable()
  (use AsCustom("jsonb") for PostgreSQL; for SQL Server use AsString(int.MaxValue) and store JSON text.)
- If you later add ShortUrl or CustomAlias fields, consider adding unique indexes on them.
- Adjust string lengths to match your DB and needs.
*/
