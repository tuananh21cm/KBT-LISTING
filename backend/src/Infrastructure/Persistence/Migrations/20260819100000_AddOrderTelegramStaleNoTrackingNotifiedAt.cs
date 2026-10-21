using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Dedup stamp for Telegram alert: purchased order still has no last-mile tracking after N hours.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260819100000_AddOrderTelegramStaleNoTrackingNotifiedAt")]
public class AddOrderTelegramStaleNoTrackingNotifiedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "TelegramStaleNoTrackingNotifiedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TelegramStaleNoTrackingNotifiedAt",
            table: "Orders");
    }
}
