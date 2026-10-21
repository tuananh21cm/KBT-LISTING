using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260710110000_AddProductShippingFields")]
public class AddProductShippingFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PackageWeight",
            table: "Products",
            type: "numeric(10,3)",
            precision: 10,
            scale: 3,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PackageLength",
            table: "Products",
            type: "numeric(10,2)",
            precision: 10,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PackageWidth",
            table: "Products",
            type: "numeric(10,2)",
            precision: 10,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PackageHeight",
            table: "Products",
            type: "numeric(10,2)",
            precision: 10,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "WeightUnit",
            table: "Products",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DimensionUnit",
            table: "Products",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PackageWeight", table: "Products");
        migrationBuilder.DropColumn(name: "PackageLength", table: "Products");
        migrationBuilder.DropColumn(name: "PackageWidth", table: "Products");
        migrationBuilder.DropColumn(name: "PackageHeight", table: "Products");
        migrationBuilder.DropColumn(name: "WeightUnit", table: "Products");
        migrationBuilder.DropColumn(name: "DimensionUnit", table: "Products");
    }
}
