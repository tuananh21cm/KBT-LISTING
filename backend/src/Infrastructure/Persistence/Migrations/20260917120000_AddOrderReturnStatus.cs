using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Human-readable 4Seller returnStatus on Orders for the refund OrderCard line.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260917120000_AddOrderReturnStatus")]
public class AddOrderReturnStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReturnStatus",
            table: "Orders",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReturnStatus",
            table: "Orders");
    }
}
