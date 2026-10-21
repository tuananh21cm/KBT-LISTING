using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260710034000_AddProductCreatedBy")]
public class AddProductCreatedBy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CreatedById",
            table: "Products",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        // Backfill: existing products were created by their owner (seller).
        migrationBuilder.Sql("""
            UPDATE "Products" SET "CreatedById" = "OwnerId"
            WHERE "CreatedById" = '00000000-0000-0000-0000-000000000000';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Products_CreatedById",
            table: "Products",
            column: "CreatedById");

        migrationBuilder.AddForeignKey(
            name: "FK_Products_AspNetUsers_CreatedById",
            table: "Products",
            column: "CreatedById",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Products_AspNetUsers_CreatedById",
            table: "Products");

        migrationBuilder.DropIndex(
            name: "IX_Products_CreatedById",
            table: "Products");

        migrationBuilder.DropColumn(
            name: "CreatedById",
            table: "Products");
    }
}
