using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Allow manual FulfilTransactions without a payment card (FulfilCardId nullable).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260724120000_FulfilTransactionOptionalCard")]
public class FulfilTransactionOptionalCard : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "FulfilCardId",
            table: "FulfilTransactions",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM "FulfilTransactions" WHERE "FulfilCardId" IS NULL;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "FulfilCardId",
            table: "FulfilTransactions",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
