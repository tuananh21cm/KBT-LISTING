using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Replace PlusSize/MidSize bools with a single BodySizeCode Select value.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814020000_ReplaceShopPlusMidWithBodySizeCode")]
public class ReplaceShopPlusMidWithBodySizeCode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BodySizeCode",
            table: "Shops",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Shops"
            SET "BodySizeCode" = CASE
                WHEN "PlusSize" = TRUE AND "MidSize" = FALSE THEN 'plus'
                WHEN "MidSize" = TRUE AND "PlusSize" = FALSE THEN 'midsize'
                WHEN "PlusSize" = TRUE AND "MidSize" = TRUE THEN 'plus'
                ELSE NULL
            END
            """);

        migrationBuilder.DropColumn(name: "PlusSize", table: "Shops");
        migrationBuilder.DropColumn(name: "MidSize", table: "Shops");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
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

        migrationBuilder.Sql(
            """
            UPDATE "Shops"
            SET
                "PlusSize" = CASE WHEN "BodySizeCode" = 'plus' THEN TRUE ELSE FALSE END,
                "MidSize" = CASE WHEN "BodySizeCode" = 'midsize' THEN TRUE ELSE FALSE END
            """);

        migrationBuilder.DropColumn(name: "BodySizeCode", table: "Shops");
    }
}
