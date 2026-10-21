using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierLinkRawPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawPayload",
                table: "OrderSupplierLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RawPayloadAt",
                table: "OrderSupplierLinks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawPayload",
                table: "OrderSupplierLinks");

            migrationBuilder.DropColumn(
                name: "RawPayloadAt",
                table: "OrderSupplierLinks");
        }
    }
}
