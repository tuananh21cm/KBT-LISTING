using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Customer marketplace payment flag (from orderPaidTime), separate from shop settlement.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804110000_AddOrderIsCustomerPaid")]
public class AddOrderIsCustomerPaid : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsCustomerPaid",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "IsCustomerPaid" = TRUE
            WHERE "OrderPaidTime" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsCustomerPaid",
            table: "Orders");
    }
}
