using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Adds Bunny CDN size chart capture URL on supplier product info.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260826140000_AddSupplierProductInfoSizeChartImageUrl")]
public class AddSupplierProductInfoSizeChartImageUrl : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SizeChartImageUrl",
            table: "SupplierProductInfos",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SizeChartImageUrl",
            table: "SupplierProductInfos");
    }
}
