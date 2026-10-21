using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260810110000_AddShopSettlementBankFields")]
public class AddShopSettlementBankFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SettlementBankAccountNumber",
            table: "Shops",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SettlementPaymentMethod",
            table: "Shops",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SettlementBeneficiaryName",
            table: "Shops",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SettlementAccountType",
            table: "Shops",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SettlementAccountStatus",
            table: "Shops",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SettlementBankAccountNumber", table: "Shops");
        migrationBuilder.DropColumn(name: "SettlementPaymentMethod", table: "Shops");
        migrationBuilder.DropColumn(name: "SettlementBeneficiaryName", table: "Shops");
        migrationBuilder.DropColumn(name: "SettlementAccountType", table: "Shops");
        migrationBuilder.DropColumn(name: "SettlementAccountStatus", table: "Shops");
    }
}
