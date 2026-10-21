using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260715094500_AddOrderPackageFields")]
public class AddOrderPackageFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PackageWeight",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PackageWeightUnit",
            table: "Orders",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PackageLength",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PackageWidth",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PackageHeight",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PackageDimensionUnit",
            table: "Orders",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PackageWeight", table: "Orders");
        migrationBuilder.DropColumn(name: "PackageWeightUnit", table: "Orders");
        migrationBuilder.DropColumn(name: "PackageLength", table: "Orders");
        migrationBuilder.DropColumn(name: "PackageWidth", table: "Orders");
        migrationBuilder.DropColumn(name: "PackageHeight", table: "Orders");
        migrationBuilder.DropColumn(name: "PackageDimensionUnit", table: "Orders");
    }
}
