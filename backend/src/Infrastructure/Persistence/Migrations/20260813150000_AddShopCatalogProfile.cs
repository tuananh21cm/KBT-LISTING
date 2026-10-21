using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Operational catalog profile per shop (TikTok leaf category + age/price + filter flags).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260813150000_AddShopCatalogProfile")]
public class AddShopCatalogProfile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CategoryId",
            table: "Shops",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CategoryName",
            table: "Shops",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AgeMin",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AgeMax",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PriceMinUsd",
            table: "Shops",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PriceMaxUsd",
            table: "Shops",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "PreferQuickShip",
            table: "Shops",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "PreferLocal",
            table: "Shops",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "PlusSize",
            table: "Shops",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "MidSize",
            table: "Shops",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CategoryId", table: "Shops");
        migrationBuilder.DropColumn(name: "CategoryName", table: "Shops");
        migrationBuilder.DropColumn(name: "AgeMin", table: "Shops");
        migrationBuilder.DropColumn(name: "AgeMax", table: "Shops");
        migrationBuilder.DropColumn(name: "PriceMinUsd", table: "Shops");
        migrationBuilder.DropColumn(name: "PriceMaxUsd", table: "Shops");
        migrationBuilder.DropColumn(name: "PreferQuickShip", table: "Shops");
        migrationBuilder.DropColumn(name: "PreferLocal", table: "Shops");
        migrationBuilder.DropColumn(name: "PlusSize", table: "Shops");
        migrationBuilder.DropColumn(name: "MidSize", table: "Shops");
    }
}
