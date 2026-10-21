using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Permanent sync status log for TikTok Shop extension (profile, payout, keywords, analytics).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823100000_AddTikTokShopSyncLogs")]
public class AddTikTokShopSyncLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TikTokShopSyncLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RunId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                ExternalShopId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SyncType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TikTokShopSyncLogs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TikTokShopSyncLogs_RunId",
            table: "TikTokShopSyncLogs",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_TikTokShopSyncLogs_OwnerId_ShopId_SyncType_SyncedAt",
            table: "TikTokShopSyncLogs",
            columns: new[] { "OwnerId", "ShopId", "SyncType", "SyncedAt" },
            descending: new[] { false, false, false, true });

        migrationBuilder.CreateIndex(
            name: "IX_TikTokShopSyncLogs_ExternalShopId_SyncedAt",
            table: "TikTokShopSyncLogs",
            columns: new[] { "ExternalShopId", "SyncedAt" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TikTokShopSyncLogs");
    }
}
