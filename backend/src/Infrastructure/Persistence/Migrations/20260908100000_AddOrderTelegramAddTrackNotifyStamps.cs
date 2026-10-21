using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Dedup / retry stamps for Telegram "Add track" after SHEIN detail sweep.
/// Pending Mark Ship used to drop the message forever because transition-only watchlist
/// does not re-fire once the snapshot is already carrier.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260908100000_AddOrderTelegramAddTrackNotifyStamps")]
public class AddOrderTelegramAddTrackNotifyStamps : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "TelegramAddTrackNotifyPendingAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "TelegramAddTrackNotifiedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TelegramAddTrackNotifyPendingAt",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "TelegramAddTrackNotifiedAt",
            table: "Orders");
    }
}
