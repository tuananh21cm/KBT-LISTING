using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Stores the SHEIN storefront session (security headers + cookie) captured by the fulfil
/// extension on the owning purchase account, plus a per-account egress proxy, so the backend
/// can call SHEIN itself from a distinct IP per account.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260803130000_AddFulfilAccountSheinSession")]
public class AddFulfilAccountSheinSession : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SheinProxy",
            table: "FulfilAccounts",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SheinHeadersJson",
            table: "FulfilAccounts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SheinCookie",
            table: "FulfilAccounts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SheinOrigin",
            table: "FulfilAccounts",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "SheinSessionCapturedAt",
            table: "FulfilAccounts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "SheinSessionLastCheckedAt",
            table: "FulfilAccounts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SheinSessionLastError",
            table: "FulfilAccounts",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SheinProxy", table: "FulfilAccounts");
        migrationBuilder.DropColumn(name: "SheinHeadersJson", table: "FulfilAccounts");
        migrationBuilder.DropColumn(name: "SheinCookie", table: "FulfilAccounts");
        migrationBuilder.DropColumn(name: "SheinOrigin", table: "FulfilAccounts");
        migrationBuilder.DropColumn(name: "SheinSessionCapturedAt", table: "FulfilAccounts");
        migrationBuilder.DropColumn(name: "SheinSessionLastCheckedAt", table: "FulfilAccounts");
        migrationBuilder.DropColumn(name: "SheinSessionLastError", table: "FulfilAccounts");
    }
}
