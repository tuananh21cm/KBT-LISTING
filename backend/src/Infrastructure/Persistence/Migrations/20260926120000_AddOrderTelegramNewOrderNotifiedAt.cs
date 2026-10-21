using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Dedup / retry stamp for Telegram new-order notifications.
/// Backfills existing rows so the pending sweep does not re-fire historical orders.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260926120000_AddOrderTelegramNewOrderNotifiedAt")]
public class AddOrderTelegramNewOrderNotifiedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "TelegramNewOrderNotifiedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "TelegramNewOrderNotifiedAt" = "CreatedAt"
            WHERE "TelegramNewOrderNotifiedAt" IS NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_TelegramNewOrderNotifiedAt",
            table: "Orders",
            column: "TelegramNewOrderNotifiedAt",
            filter: "\"TelegramNewOrderNotifiedAt\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Orders_TelegramNewOrderNotifiedAt",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "TelegramNewOrderNotifiedAt",
            table: "Orders");
    }
}
