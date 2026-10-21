using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719040000_AddReceivingBankAccounts")]
public class AddReceivingBankAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReceivingBankAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                BankLocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                AccountHolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AccountType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                SwiftBic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                RoutingNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AccountNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                BankAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReceivingBankAccounts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankAccounts_AccountNumber",
            table: "ReceivingBankAccounts",
            column: "AccountNumber");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankAccounts_IsActive",
            table: "ReceivingBankAccounts",
            column: "IsActive");

        migrationBuilder.AddColumn<Guid>(
            name: "ReceivingBankAccountId",
            table: "Shops",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shops_ReceivingBankAccountId",
            table: "Shops",
            column: "ReceivingBankAccountId");

        migrationBuilder.AddForeignKey(
            name: "FK_Shops_ReceivingBankAccounts_ReceivingBankAccountId",
            table: "Shops",
            column: "ReceivingBankAccountId",
            principalTable: "ReceivingBankAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Shops_ReceivingBankAccounts_ReceivingBankAccountId",
            table: "Shops");

        migrationBuilder.DropIndex(
            name: "IX_Shops_ReceivingBankAccountId",
            table: "Shops");

        migrationBuilder.DropColumn(
            name: "ReceivingBankAccountId",
            table: "Shops");

        migrationBuilder.DropTable(
            name: "ReceivingBankAccounts");
    }
}
