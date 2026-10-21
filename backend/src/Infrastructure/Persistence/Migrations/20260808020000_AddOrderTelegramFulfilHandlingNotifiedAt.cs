using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Dedup stamps for realtime Telegram fulfil-handling (USPS / Shein warehouse track).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260808020000_AddOrderTelegramFulfilHandlingNotifiedAt")]
public class AddOrderTelegramFulfilHandlingNotifiedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "TelegramUspsNotifiedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "TelegramWarehouseNotifiedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TelegramUspsNotifiedAt",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "TelegramWarehouseNotifiedAt",
            table: "Orders");
    }
}
