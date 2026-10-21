using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Shop catalog product filters: min rating/sold, max variants, Shein Club.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814180000_AddShopProductFilterCriteria")]
public class AddShopProductFilterCriteria : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "MinRating",
            table: "Shops",
            type: "numeric(3,1)",
            precision: 3,
            scale: 1,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "MinSoldCount",
            table: "Shops",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "MaxVariantCount",
            table: "Shops",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<bool>(
            name: "PreferSheinClub",
            table: "Shops",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MinRating", table: "Shops");
        migrationBuilder.DropColumn(name: "MinSoldCount", table: "Shops");
        migrationBuilder.DropColumn(name: "MaxVariantCount", table: "Shops");
        migrationBuilder.DropColumn(name: "PreferSheinClub", table: "Shops");
    }
}
