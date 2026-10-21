using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260715093000_AddOrdersAndOrderItems")]
public class AddOrdersAndOrderItems : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Orders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FourSellerOrderId = table.Column<long>(type: "bigint", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                FourSellerShopId = table.Column<long>(type: "bigint", nullable: true),
                PlatformOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                OrderStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PlatformOrderStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                TotalPaidAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                ShipmentNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                TrackingNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ShippingMethod = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ShippingService = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                BuyerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                BuyerPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                BuyerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                Address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                State = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PostalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                CountryCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                BuyerMemo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SellerMemo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                InternalMemo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                OrderPaidTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                OrderCreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LatestShipDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                OrderShippedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LatestDeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RawPayload = table.Column<string>(type: "text", nullable: true),
                LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
                table.ForeignKey(
                    name: "FK_Orders_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Orders_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "OrderItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                FourSellerOrderItemId = table.Column<long>(type: "bigint", nullable: false),
                SellerSku = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ImgUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ListingUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                VariantAttr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                ItemPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrderItems_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Orders_FourSellerOrderId",
            table: "Orders",
            column: "FourSellerOrderId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OwnerId",
            table: "Orders",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OrderStatus",
            table: "Orders",
            column: "OrderStatus");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_PlatformOrderId",
            table: "Orders",
            column: "PlatformOrderId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_ShopId",
            table: "Orders",
            column: "ShopId");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_LastSyncedAt",
            table: "Orders",
            column: "LastSyncedAt");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_OrderId",
            table: "OrderItems",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_OrderId_FourSellerOrderItemId",
            table: "OrderItems",
            columns: new[] { "OrderId", "FourSellerOrderItemId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OrderItems");
        migrationBuilder.DropTable(name: "Orders");
    }
}
