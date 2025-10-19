using System;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentMigrator;

namespace Linky.Migrations
{
    [Migration(20251002003)]
    public class CreateQRCodeScanEventMigration : Migration
    {
        public override void Up()
        {
            // Aggregated scan summary per QR code
            Create.Table("QRCodeScanEvents")
                .WithColumn("QRCodeId").AsGuid().PrimaryKey().NotNullable()
                .WithColumn("LastScannedAt").AsDateTime().NotNullable()
                .WithColumn("ScanCount").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("LastIp").AsString(45).Nullable()
                .WithColumn("LastCountry").AsString(100).Nullable()
                .WithColumn("LastCity").AsString(100).Nullable();

            Create.ForeignKey("FK_QRCodeScanEvents_QRCodes")
                .FromTable("QRCodeScanEvents").ForeignColumn("QRCodeId")
                .ToTable("QRCodes").PrimaryColumn("Id")
                .OnDelete(System.Data.Rule.Cascade);

            Create.Index("IX_QRCodeScanEvents_LastScannedAt")
                .OnTable("QRCodeScanEvents").OnColumn("LastScannedAt").Descending();

            Create.Index("IX_QRCodeScanEvents_ScanCount")
                .OnTable("QRCodeScanEvents").OnColumn("ScanCount").Descending();
        }

        public override void Down()
        {
            Delete.Table("QRCodeScanEvents");
        }
    }
}

/*
Notes / Migration considerations:
- This migration creates an aggregated table (one row per QR code) matching your model that stores LastScannedAt and ScanCount.
- If you previously had a detailed events table (also named QRCodeScanEvents) created by an earlier migration, this migration will fail when applied because the table already exists. Two approaches:
  1) If you already have the detailed table and want to preserve its data, run a manual migration step first to copy/aggregate data into the new schema (examples below), then drop or rename the old table. After that, apply this migration.
  2) If you don't have the old table or want to replace it, drop the old table before applying this migration.

SQL example to aggregate from a detailed table named QRCodeScanEvents (Id, QRCodeId, ScannedAt, Ip, Country, City):

INSERT INTO QRCodeScanEvents (QRCodeId, LastScannedAt, ScanCount, LastIp, LastCountry, LastCity)
SELECT
  QRCodeId,
  MAX(ScannedAt) AS LastScannedAt,
  COUNT(*) AS ScanCount,
  MAX(Ip) AS LastIp,
  MAX(Country) AS LastCountry,
  MAX(City) AS LastCity
FROM QRCodeScanEvents_Detailed /* replace with your old table name */