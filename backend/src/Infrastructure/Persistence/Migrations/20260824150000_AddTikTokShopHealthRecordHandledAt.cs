using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// User-acknowledged handled timestamp for TikTok Shop Health Center records.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824150000_AddTikTokShopHealthRecordHandledAt")]
public class AddTikTokShopHealthRecordHandledAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "HandledAt",
            table: "TikTokShopHealthRecords",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TikTokShopHealthRecords_ShopId_HandledAt",
            table: "TikTokShopHealthRecords",
            columns: new[] { "ShopId", "HandledAt" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TikTokShopHealthRecords_ShopId_HandledAt",
            table: "TikTokShopHealthRecords");

        migrationBuilder.DropColumn(
            name: "HandledAt",
            table: "TikTokShopHealthRecords");
    }
}
