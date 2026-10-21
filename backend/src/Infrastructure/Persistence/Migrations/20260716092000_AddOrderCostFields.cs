using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716092000_AddOrderCostFields")]
public class AddOrderCostFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "Cost",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ShippingFee",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "Tax",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "Ads",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Cost", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingFee", table: "Orders");
        migrationBuilder.DropColumn(name: "Tax", table: "Orders");
        migrationBuilder.DropColumn(name: "Ads", table: "Orders");
    }
}
