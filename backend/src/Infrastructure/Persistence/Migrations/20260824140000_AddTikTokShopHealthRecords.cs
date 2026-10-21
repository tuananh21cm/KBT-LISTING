using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// TikTok Shop Growth Center violation/warning records synced from the browser extension.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824140000_AddTikTokShopHealthRecords")]
public class AddTikTokShopHealthRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TikTokShopHealthRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                RecordId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                RecordType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CategoryType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ActionTitlesJson = table.Column<string>(type: "jsonb", nullable: false),
                RecordStatus = table.Column<short>(type: "smallint", nullable: false),
                CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                FirstSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TikTokShopHealthRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TikTokShopHealthRecords_ShopId_RecordId",
            table: "TikTokShopHealthRecords",
            columns: new[] { "ShopId", "RecordId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TikTokShopHealthRecords_ShopId_RecordType_RecordStatus",
            table: "TikTokShopHealthRecords",
            columns: new[] { "ShopId", "RecordType", "RecordStatus" });

        migrationBuilder.CreateIndex(
            name: "IX_TikTokShopHealthRecords_ShopId_CreateTime",
            table: "TikTokShopHealthRecords",
            columns: new[] { "ShopId", "CreateTime" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TikTokShopHealthRecords");
    }
}
