using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Shop operational catalog: many TikTok categories (jsonb) + wider joined display name.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260813210000_AddShopCatalogCategoriesJson")]
public class AddShopCatalogCategoriesJson : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "CategoryName",
            table: "Shops",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(500)",
            oldMaxLength: 500,
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CatalogCategories",
            table: "Shops",
            type: "jsonb",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Shops"
            SET "CatalogCategories" = jsonb_build_array(
                jsonb_build_object('id', "CategoryId", 'name', COALESCE(NULLIF("CategoryName", ''), "CategoryId"))
            )
            WHERE "CategoryId" IS NOT NULL AND TRIM("CategoryId") <> '';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CatalogCategories", table: "Shops");

        migrationBuilder.AlterColumn<string>(
            name: "CategoryName",
            table: "Shops",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(2000)",
            oldMaxLength: 2000,
            oldNullable: true);
    }
}
