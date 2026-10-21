using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Order handling fees: BuyTrack (USPS scan) + ThankCard amounts and ThankCard stamp.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260808000000_AddOrderBuyTrackThankCard")]
public class AddOrderBuyTrackThankCard : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "BuyTrackAmount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "ThankCardAmount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<DateTime>(
            name: "ThankCardProcessedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BuyTrackAmount",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "ThankCardAmount",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "ThankCardProcessedAt",
            table: "Orders");
    }
}
