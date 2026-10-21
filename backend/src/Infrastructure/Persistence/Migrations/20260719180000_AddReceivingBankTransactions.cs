using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719180000_AddReceivingBankTransactions")]
public class AddReceivingBankTransactions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsSettlementPaid",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "ReceivingBankTransactions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReceivingBankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReceivingBankTransactions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ReceivingBankTransactions_AspNetUsers_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ReceivingBankTransactions_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_ReceivingBankTransactions_ReceivingBankAccounts_ReceivingBan~",
                    column: x => x.ReceivingBankAccountId,
                    principalTable: "ReceivingBankAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ReceivingBankTransactions_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_CreatedByUserId",
            table: "ReceivingBankTransactions",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_Direction",
            table: "ReceivingBankTransactions",
            column: "Direction");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_OccurredAt",
            table: "ReceivingBankTransactions",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_OrderId",
            table: "ReceivingBankTransactions",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_OrderId_SettlementPaid",
            table: "ReceivingBankTransactions",
            column: "OrderId",
            unique: true,
            filter: "\"OrderId\" IS NOT NULL AND \"Source\" = 'SettlementPaid'");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_ReceivingBankAccountId",
            table: "ReceivingBankTransactions",
            column: "ReceivingBankAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_ShopId",
            table: "ReceivingBankTransactions",
            column: "ShopId");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankTransactions_Source",
            table: "ReceivingBankTransactions",
            column: "Source");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ReceivingBankTransactions");

        migrationBuilder.DropColumn(
            name: "IsSettlementPaid",
            table: "Orders");
    }
}
