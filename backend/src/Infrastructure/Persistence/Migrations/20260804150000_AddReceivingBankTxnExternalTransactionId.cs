using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Optional payout/transfer id from the platform that sent money.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804150000_AddReceivingBankTxnExternalTransactionId")]
public class AddReceivingBankTxnExternalTransactionId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExternalTransactionId",
            table: "ReceivingBankTransactions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_ExternalTransactionId",
            table: "ReceivingBankTransactions",
            column: "ExternalTransactionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReceivingBankTransactions_ExternalTransactionId",
            table: "ReceivingBankTransactions");

        migrationBuilder.DropColumn(
            name: "ExternalTransactionId",
            table: "ReceivingBankTransactions");
    }
}
