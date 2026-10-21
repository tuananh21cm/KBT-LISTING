using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260902140000_AddProductDiscountActivities")]
public class AddProductDiscountActivities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductDiscountActivities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                FourSellerActivityId = table.Column<long>(type: "bigint", nullable: false),
                FourSellerShopId = table.Column<long>(type: "bigint", nullable: false),
                ShopName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                PlatformActivityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ActivityName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                DiscountType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PromotionStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProductLevel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                BeginTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProductCount = table.Column<int>(type: "integer", nullable: false),
                ProductFailCount = table.Column<int>(type: "integer", nullable: false),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductDiscountActivities", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductDiscountActivities_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductDiscountActivities_BeginTimeUtc",
            table: "ProductDiscountActivities",
            column: "BeginTimeUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ProductDiscountActivities_OwnerUserId_FourSellerActivityId",
            table: "ProductDiscountActivities",
            columns: new[] { "OwnerUserId", "FourSellerActivityId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductDiscountActivities_PromotionStatus",
            table: "ProductDiscountActivities",
            column: "PromotionStatus");

        migrationBuilder.CreateIndex(
            name: "IX_ProductDiscountActivities_ShopId",
            table: "ProductDiscountActivities",
            column: "ShopId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductDiscountActivities");
    }
}
