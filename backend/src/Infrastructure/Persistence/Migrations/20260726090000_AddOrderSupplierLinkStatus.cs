using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Persist SHEIN lifecycle status per supplier link (for hybrid sync + timeline).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726090000_AddOrderSupplierLinkStatus")]
public class AddOrderSupplierLinkStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SupplierStatus",
            table: "OrderSupplierLinks",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SupplierStatusRaw",
            table: "OrderSupplierLinks",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "SupplierStatusAt",
            table: "OrderSupplierLinks",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_SupplierStatus",
            table: "OrderSupplierLinks",
            column: "SupplierStatus");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrderSupplierLinks_SupplierStatus",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "SupplierStatusAt",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "SupplierStatusRaw",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "SupplierStatus",
            table: "OrderSupplierLinks");
    }
}
