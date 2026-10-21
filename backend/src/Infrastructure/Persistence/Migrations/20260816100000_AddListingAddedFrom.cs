using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Local-only listing origin (extension Search vs PDP thủ công) + staging until draft sync.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260816100000_AddListingAddedFrom")]
public class AddListingAddedFrom : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AddedFrom",
            table: "Listings",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ListingAddOrigins",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceUrlKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                FourSellerShopId = table.Column<long>(type: "bigint", nullable: false),
                AddedFrom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingAddOrigins", x => x.Id);
                table.ForeignKey(
                    name: "FK_ListingAddOrigins_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListingAddOrigins_OwnerId",
            table: "ListingAddOrigins",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_ListingAddOrigins_OwnerId_SourceUrlKey_FourSellerShopId",
            table: "ListingAddOrigins",
            columns: new[] { "OwnerId", "SourceUrlKey", "FourSellerShopId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ListingAddOrigins");

        migrationBuilder.DropColumn(
            name: "AddedFrom",
            table: "Listings");
    }
}
