using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260810120000_AddTikTokPayoutSyncFields")]
public class AddTikTokPayoutSyncFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TikTokPaymentId",
            table: "Orders",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_TikTokPaymentId",
            table: "Orders",
            column: "TikTokPaymentId",
            filter: "\"TikTokPaymentId\" IS NOT NULL AND \"TikTokPaymentId\" <> ''");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Orders_TikTokPaymentId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "TikTokPaymentId",
            table: "Orders");
    }
}
