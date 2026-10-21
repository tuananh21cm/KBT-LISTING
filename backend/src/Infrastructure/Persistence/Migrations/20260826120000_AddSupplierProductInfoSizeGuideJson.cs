using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Adds Shein size guide snapshot jsonb on supplier product info.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260826120000_AddSupplierProductInfoSizeGuideJson")]
public class AddSupplierProductInfoSizeGuideJson : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SizeGuideJson",
            table: "SupplierProductInfos",
            type: "jsonb",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SizeGuideJson",
            table: "SupplierProductInfos");
    }
}
