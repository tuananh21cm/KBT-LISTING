using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// TikTok Finance On hold snapshot columns on Shops (statement/stat/info).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260813100000_AddShopOnHoldAmounts")]
public class AddShopOnHoldAmounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "OnHoldAmount",
            table: "Shops",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "OnHoldInTransitAmount",
            table: "Shops",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "OnHoldAwaitingSettlementAmount",
            table: "Shops",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "OnHoldReserveAmount",
            table: "Shops",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OnHoldAmount", table: "Shops");
        migrationBuilder.DropColumn(name: "OnHoldInTransitAmount", table: "Shops");
        migrationBuilder.DropColumn(name: "OnHoldAwaitingSettlementAmount", table: "Shops");
        migrationBuilder.DropColumn(name: "OnHoldReserveAmount", table: "Shops");
    }
}
