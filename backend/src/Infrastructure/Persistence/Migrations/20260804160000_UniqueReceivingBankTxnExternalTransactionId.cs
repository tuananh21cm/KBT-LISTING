using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Enforce unique ExternalTransactionId when present (platform payout id).</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804160000_UniqueReceivingBankTxnExternalTransactionId")]
public class UniqueReceivingBankTxnExternalTransactionId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReceivingBankTransactions_ExternalTransactionId",
            table: "ReceivingBankTransactions");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_ExternalTransactionId",
            table: "ReceivingBankTransactions",
            column: "ExternalTransactionId",
            unique: true,
            filter: "\"ExternalTransactionId\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReceivingBankTransactions_ExternalTransactionId",
            table: "ReceivingBankTransactions");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_ExternalTransactionId",
            table: "ReceivingBankTransactions",
            column: "ExternalTransactionId");
    }
}
