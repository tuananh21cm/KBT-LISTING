using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Shop-level auto publish cooldown when TikTok Day_Limit is hit.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260829100000_AddShopAutoPublishDayLimitBlock")]
public class AddShopAutoPublishDayLimitBlock : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "AutoPublishBlockedUntil",
            table: "Shops",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AutoPublishBlockReason",
            table: "Shops",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AutoPublishBlockedUntil",
            table: "Shops");

        migrationBuilder.DropColumn(
            name: "AutoPublishBlockReason",
            table: "Shops");
    }
}
