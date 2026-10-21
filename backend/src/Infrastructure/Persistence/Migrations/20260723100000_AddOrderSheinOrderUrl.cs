using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723100000_AddOrderSheinOrderUrl")]
public class AddOrderSheinOrderUrl : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SheinOrderUrl",
            table: "Orders",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);

        // Backfill from existing billno (default US host).
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "SheinOrderUrl" = 'https://us.shein.com/user/orders/detail/' || "SheinBillNo"
            WHERE "SheinBillNo" IS NOT NULL
              AND TRIM("SheinBillNo") <> ''
              AND ("SheinOrderUrl" IS NULL OR TRIM("SheinOrderUrl") = '');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SheinOrderUrl",
            table: "Orders");
    }
}
