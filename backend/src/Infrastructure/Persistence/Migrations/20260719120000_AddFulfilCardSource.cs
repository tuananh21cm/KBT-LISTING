using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719120000_AddFulfilCardSource")]
public class AddFulfilCardSource : Migration
{
    private static readonly Guid OtherSourceId = new("b2000000-0000-0000-0000-000000000003");

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SourceId",
            table: "FulfilCards",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "SourceAccountId",
            table: "FulfilCards",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql($@"
            UPDATE ""FulfilCards""
            SET ""SourceId"" = '{OtherSourceId}'
            WHERE ""SourceId"" IS NULL;
        ");

        migrationBuilder.AlterColumn<Guid>(
            name: "SourceId",
            table: "FulfilCards",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_SourceId",
            table: "FulfilCards",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_SourceAccountId",
            table: "FulfilCards",
            column: "SourceAccountId");

        migrationBuilder.AddForeignKey(
            name: "FK_FulfilCards_ReceivingCardSources_SourceId",
            table: "FulfilCards",
            column: "SourceId",
            principalTable: "ReceivingCardSources",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_FulfilCards_ReceivingSourceAccounts_SourceAccountId",
            table: "FulfilCards",
            column: "SourceAccountId",
            principalTable: "ReceivingSourceAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_FulfilCards_ReceivingCardSources_SourceId",
            table: "FulfilCards");

        migrationBuilder.DropForeignKey(
            name: "FK_FulfilCards_ReceivingSourceAccounts_SourceAccountId",
            table: "FulfilCards");

        migrationBuilder.DropIndex(
            name: "IX_FulfilCards_SourceId",
            table: "FulfilCards");

        migrationBuilder.DropIndex(
            name: "IX_FulfilCards_SourceAccountId",
            table: "FulfilCards");

        migrationBuilder.DropColumn(
            name: "SourceAccountId",
            table: "FulfilCards");

        migrationBuilder.DropColumn(
            name: "SourceId",
            table: "FulfilCards");
    }
}
