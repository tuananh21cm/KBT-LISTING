using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719050000_AddReceivingCardSources")]
public class AddReceivingCardSources : Migration
{
    private static readonly Guid PingPongXId = new("b2000000-0000-0000-0000-000000000001");
    private static readonly Guid WorldFirstId = new("b2000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherId = new("b2000000-0000-0000-0000-000000000003");

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReceivingCardSources",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SupportsAccounts = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReceivingCardSources", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingCardSources_Code",
            table: "ReceivingCardSources",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingCardSources_IsActive",
            table: "ReceivingCardSources",
            column: "IsActive");

        migrationBuilder.CreateTable(
            name: "ReceivingSourceAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ExternalAccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReceivingSourceAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_ReceivingSourceAccounts_ReceivingCardSources_SourceId",
                    column: x => x.SourceId,
                    principalTable: "ReceivingCardSources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingSourceAccounts_IsActive",
            table: "ReceivingSourceAccounts",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingSourceAccounts_SourceId",
            table: "ReceivingSourceAccounts",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingSourceAccounts_SourceId_Name",
            table: "ReceivingSourceAccounts",
            columns: new[] { "SourceId", "Name" });

        // Raw SQL — InsertData needs model mappings that hand-written migrations don't always provide.
        migrationBuilder.Sql(
            $"""
            INSERT INTO "ReceivingCardSources" ("Id", "Code", "Name", "Description", "SupportsAccounts", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES
              ('{PingPongXId}', 'PINGPONGX', 'PingPongX', 'Nguồn thẻ PingPongX (nhiều tài khoản)', TRUE, TRUE, TIMESTAMPTZ '2026-07-19 00:00:00+00', NULL),
              ('{WorldFirstId}', 'WORLDFIRST', 'WorldFirst', 'Nguồn thẻ WorldFirst (nhiều tài khoản)', TRUE, TRUE, TIMESTAMPTZ '2026-07-19 00:00:00+00', NULL),
              ('{OtherId}', 'OTHER', 'Khác', 'Nguồn thẻ / ngân hàng khác', FALSE, TRUE, TIMESTAMPTZ '2026-07-19 00:00:00+00', NULL)
            ON CONFLICT ("Id") DO NOTHING;
            """);

        migrationBuilder.AddColumn<Guid>(
            name: "SourceId",
            table: "ReceivingBankAccounts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "SourceAccountId",
            table: "ReceivingBankAccounts",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            $"""
            UPDATE "ReceivingBankAccounts"
            SET "SourceId" = '{OtherId}'
            WHERE "SourceId" IS NULL;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "SourceId",
            table: "ReceivingBankAccounts",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankAccounts_SourceId",
            table: "ReceivingBankAccounts",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_ReceivingBankAccounts_SourceAccountId",
            table: "ReceivingBankAccounts",
            column: "SourceAccountId");

        migrationBuilder.AddForeignKey(
            name: "FK_ReceivingBankAccounts_ReceivingCardSources_SourceId",
            table: "ReceivingBankAccounts",
            column: "SourceId",
            principalTable: "ReceivingCardSources",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ReceivingBankAccounts_ReceivingSourceAccounts_SourceAccountId",
            table: "ReceivingBankAccounts",
            column: "SourceAccountId",
            principalTable: "ReceivingSourceAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ReceivingBankAccounts_ReceivingCardSources_SourceId",
            table: "ReceivingBankAccounts");

        migrationBuilder.DropForeignKey(
            name: "FK_ReceivingBankAccounts_ReceivingSourceAccounts_SourceAccountId",
            table: "ReceivingBankAccounts");

        migrationBuilder.DropIndex(
            name: "IX_ReceivingBankAccounts_SourceId",
            table: "ReceivingBankAccounts");

        migrationBuilder.DropIndex(
            name: "IX_ReceivingBankAccounts_SourceAccountId",
            table: "ReceivingBankAccounts");

        migrationBuilder.DropColumn(
            name: "SourceId",
            table: "ReceivingBankAccounts");

        migrationBuilder.DropColumn(
            name: "SourceAccountId",
            table: "ReceivingBankAccounts");

        migrationBuilder.DropTable(
            name: "ReceivingSourceAccounts");

        migrationBuilder.DropTable(
            name: "ReceivingCardSources");
    }
}
