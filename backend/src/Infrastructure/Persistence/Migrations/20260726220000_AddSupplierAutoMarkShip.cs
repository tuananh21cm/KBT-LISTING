using System;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Persists idempotency and retry state for SHEIN automatic Mark Ship.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726220000_AddSupplierAutoMarkShip")]
public class AddSupplierAutoMarkShip : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AutoMarkShipTrackingNo",
            table: "OrderSupplierLinks",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AutoMarkShipStatus",
            table: "OrderSupplierLinks",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "AutoMarkShipAttemptedAt",
            table: "OrderSupplierLinks",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AutoMarkShipError",
            table: "OrderSupplierLinks",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_AutoMarkShipStatus",
            table: "OrderSupplierLinks",
            column: "AutoMarkShipStatus");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrderSupplierLinks_AutoMarkShipStatus",
            table: "OrderSupplierLinks");
        migrationBuilder.DropColumn(name: "AutoMarkShipTrackingNo", table: "OrderSupplierLinks");
        migrationBuilder.DropColumn(name: "AutoMarkShipStatus", table: "OrderSupplierLinks");
        migrationBuilder.DropColumn(name: "AutoMarkShipAttemptedAt", table: "OrderSupplierLinks");
        migrationBuilder.DropColumn(name: "AutoMarkShipError", table: "OrderSupplierLinks");
    }
}
