using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFulfilClaimAndAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FulfilClaimExpiresAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FulfilClaimSessionCount",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FulfilClaimedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FulfilClaimedByUserId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderFulfilAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionLabel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRenewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFulfilAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderFulfilAttempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderFulfilAttempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FulfilClaimedByUserId",
                table: "Orders",
                column: "FulfilClaimedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FulfilClaimExpiresAt",
                table: "Orders",
                column: "FulfilClaimExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFulfilAttempts_BatchId",
                table: "OrderFulfilAttempts",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFulfilAttempts_ExpiresAt",
                table: "OrderFulfilAttempts",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFulfilAttempts_OrderId_ClosedAt",
                table: "OrderFulfilAttempts",
                columns: new[] { "OrderId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFulfilAttempts_OrderId_SessionId",
                table: "OrderFulfilAttempts",
                columns: new[] { "OrderId", "SessionId" },
                unique: true,
                filter: "\"ClosedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFulfilAttempts_UserId_StartedAt",
                table: "OrderFulfilAttempts",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_AspNetUsers_FulfilClaimedByUserId",
                table: "Orders",
                column: "FulfilClaimedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_AspNetUsers_FulfilClaimedByUserId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "OrderFulfilAttempts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_FulfilClaimedByUserId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_FulfilClaimExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilClaimExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilClaimSessionCount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilClaimedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilClaimedByUserId",
                table: "Orders");
        }
    }
}
