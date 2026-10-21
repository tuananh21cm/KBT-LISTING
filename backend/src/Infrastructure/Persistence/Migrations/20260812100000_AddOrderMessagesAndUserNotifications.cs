using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Order chat thread + per-user notification inbox.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260812100000_AddOrderMessagesAndUserNotifications")]
public class AddOrderMessagesAndUserNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrderMessages_AspNetUsers_SenderUserId",
                    column: x => x.SenderUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OrderMessages_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserNotifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                OrderMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                IsRead = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserNotifications", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserNotifications_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UserNotifications_OrderMessages_OrderMessageId",
                    column: x => x.OrderMessageId,
                    principalTable: "OrderMessages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_UserNotifications_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderMessages_OrderId_CreatedAt",
            table: "OrderMessages",
            columns: new[] { "OrderId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_OrderMessages_SenderUserId",
            table: "OrderMessages",
            column: "SenderUserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_OrderId",
            table: "UserNotifications",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_OrderMessageId",
            table: "UserNotifications",
            column: "OrderMessageId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_UserId_IsRead_CreatedAt",
            table: "UserNotifications",
            columns: new[] { "UserId", "IsRead", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserNotifications");
        migrationBuilder.DropTable(name: "OrderMessages");
    }
}
