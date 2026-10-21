using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Remove legacy Manual ledger Debits for BuyTrack / ThankCard handling fees.
/// Fees live on Orders.BuyTrackAmount / ThankCardAmount and appear as read-model
/// columns on CostUpdate rows — not as separate FulfilTransactions that inflate card spend.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260829120000_RemoveLegacyHandlingFeeLedgerRows")]
public class RemoveLegacyHandlingFeeLedgerRows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM "FulfilTransactions"
            WHERE "Source" = 'Manual'
              AND "Note" IS NOT NULL
              AND (
                    "Note" LIKE '[BuyTrack]%'
                 OR "Note" LIKE '[ThankCard]%'
              );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible data cleanup — amounts remain on Orders; Manual rows are not restored.
    }
}
