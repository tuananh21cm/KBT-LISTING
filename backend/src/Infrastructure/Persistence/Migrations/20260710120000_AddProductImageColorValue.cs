using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260710120000_AddProductImageColorValue")]
public class AddProductImageColorValue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ColorValue",
            table: "ProductImages",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductImages_ProductId_ColorValue",
            table: "ProductImages",
            columns: new[] { "ProductId", "ColorValue" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ProductImages_ProductId_ColorValue",
            table: "ProductImages");

        migrationBuilder.DropColumn(
            name: "ColorValue",
            table: "ProductImages");
    }
}
