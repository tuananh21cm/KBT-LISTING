using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719070000_AddFulfilManagement")]
public class AddFulfilManagement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FulfilAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Password = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                PassMail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                TwoFactorAuth = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                OperatorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                ActiveCardId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FulfilAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_FulfilAccounts_AspNetUsers_OperatorUserId",
                    column: x => x.OperatorUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "FulfilCards",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CardNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CardBrand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ExpiryMonth = table.Column<int>(type: "integer", nullable: true),
                ExpiryYear = table.Column<int>(type: "integer", nullable: true),
                HolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Cvv = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                FulfilAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActiveOnAccount = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FulfilCards", x => x.Id);
                table.ForeignKey(
                    name: "FK_FulfilCards_AspNetUsers_AssignedToUserId",
                    column: x => x.AssignedToUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_FulfilCards_FulfilAccounts_FulfilAccountId",
                    column: x => x.FulfilAccountId,
                    principalTable: "FulfilAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddForeignKey(
            name: "FK_FulfilAccounts_FulfilCards_ActiveCardId",
            table: "FulfilAccounts",
            column: "ActiveCardId",
            principalTable: "FulfilCards",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddColumn<Guid>(
            name: "FulfilAccountId",
            table: "Orders",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "FulfilCardId",
            table: "Orders",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "PaidByUserId",
            table: "Orders",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "FulfilTransactions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FulfilAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                FulfilCardId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                PaidByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FulfilTransactions", x => x.Id);
                table.ForeignKey(
                    name: "FK_FulfilTransactions_AspNetUsers_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_FulfilTransactions_AspNetUsers_PaidByUserId",
                    column: x => x.PaidByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_FulfilTransactions_FulfilAccounts_FulfilAccountId",
                    column: x => x.FulfilAccountId,
                    principalTable: "FulfilAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_FulfilTransactions_FulfilCards_FulfilCardId",
                    column: x => x.FulfilCardId,
                    principalTable: "FulfilCards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_FulfilTransactions_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_ActiveCardId",
            table: "FulfilAccounts",
            column: "ActiveCardId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_IsActive",
            table: "FulfilAccounts",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_Name",
            table: "FulfilAccounts",
            column: "Name");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_OperatorUserId",
            table: "FulfilAccounts",
            column: "OperatorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_AssignedToUserId",
            table: "FulfilCards",
            column: "AssignedToUserId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_FulfilAccountId",
            table: "FulfilCards",
            column: "FulfilAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_FulfilAccountId_IsActiveOnAccount",
            table: "FulfilCards",
            columns: new[] { "FulfilAccountId", "IsActiveOnAccount" });

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_FulfilAccountId",
            table: "FulfilTransactions",
            column: "FulfilAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_FulfilCardId",
            table: "FulfilTransactions",
            column: "FulfilCardId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_OccurredAt",
            table: "FulfilTransactions",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_OrderId",
            table: "FulfilTransactions",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_OrderId_CostUpdate",
            table: "FulfilTransactions",
            column: "OrderId",
            unique: true,
            filter: "\"OrderId\" IS NOT NULL AND \"Source\" = 'CostUpdate'");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_Source",
            table: "FulfilTransactions",
            column: "Source");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_FulfilAccountId",
            table: "Orders",
            column: "FulfilAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_FulfilCardId",
            table: "Orders",
            column: "FulfilCardId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_PaidByUserId",
            table: "Orders",
            column: "PaidByUserId");

        migrationBuilder.AddForeignKey(
            name: "FK_Orders_AspNetUsers_PaidByUserId",
            table: "Orders",
            column: "PaidByUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Orders_FulfilAccounts_FulfilAccountId",
            table: "Orders",
            column: "FulfilAccountId",
            principalTable: "FulfilAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Orders_FulfilCards_FulfilCardId",
            table: "Orders",
            column: "FulfilCardId",
            principalTable: "FulfilCards",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Orders_AspNetUsers_PaidByUserId",
            table: "Orders");

        migrationBuilder.DropForeignKey(
            name: "FK_Orders_FulfilAccounts_FulfilAccountId",
            table: "Orders");

        migrationBuilder.DropForeignKey(
            name: "FK_Orders_FulfilCards_FulfilCardId",
            table: "Orders");

        migrationBuilder.DropTable(
            name: "FulfilTransactions");

        migrationBuilder.DropForeignKey(
            name: "FK_FulfilAccounts_FulfilCards_ActiveCardId",
            table: "FulfilAccounts");

        migrationBuilder.DropTable(
            name: "FulfilCards");

        migrationBuilder.DropTable(
            name: "FulfilAccounts");

        migrationBuilder.DropIndex(
            name: "IX_Orders_FulfilAccountId",
            table: "Orders");

        migrationBuilder.DropIndex(
            name: "IX_Orders_FulfilCardId",
            table: "Orders");

        migrationBuilder.DropIndex(
            name: "IX_Orders_PaidByUserId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "FulfilAccountId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "FulfilCardId",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "PaidByUserId",
            table: "Orders");
    }
}
