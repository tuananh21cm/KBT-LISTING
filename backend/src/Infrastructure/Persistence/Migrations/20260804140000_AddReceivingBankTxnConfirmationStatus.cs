using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Manual seller-submitted incoming-money rows await admin confirmation before counting in stats.
/// Existing rows + SettlementPaid default to Confirmed.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804140000_AddReceivingBankTxnConfirmationStatus")]
public class AddReceivingBankTxnConfirmationStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ConfirmationStatus",
            table: "ReceivingBankTransactions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Confirmed");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_ConfirmationStatus",
            table: "ReceivingBankTransactions",
            column: "ConfirmationStatus");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReceivingBankTransactions_ConfirmationStatus",
            table: "ReceivingBankTransactions");

        migrationBuilder.DropColumn(
            name: "ConfirmationStatus",
            table: "ReceivingBankTransactions");
    }
}
