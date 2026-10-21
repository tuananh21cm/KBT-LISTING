using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Removes the in-app notification inbox and order/direct chat tables
/// (<c>UserNotifications</c>, <c>DirectMessages</c>, <c>OrderMessages</c>).
/// <c>UserNotifications</c> is dropped first because it references the other two.
/// Issue tags, <c>Orders.Message</c> and Telegram notifications are unaffected.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001120000_DropChatAndNotifications")]
public class DropChatAndNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserNotifications");
        migrationBuilder.DropTable(name: "DirectMessages");
        migrationBuilder.DropTable(name: "OrderMessages");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
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
            name: "DirectMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DirectMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_DirectMessages_AspNetUsers_SenderUserId",
                    column: x => x.SenderUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DirectMessages_AspNetUsers_RecipientUserId",
                    column: x => x.RecipientUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
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
                PeerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                DirectMessageId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    name: "FK_UserNotifications_AspNetUsers_PeerUserId",
                    column: x => x.PeerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_UserNotifications_DirectMessages_DirectMessageId",
                    column: x => x.DirectMessageId,
                    principalTable: "DirectMessages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
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
            name: "IX_DirectMessages_SenderUserId_RecipientUserId_CreatedAt",
            table: "DirectMessages",
            columns: new[] { "SenderUserId", "RecipientUserId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_DirectMessages_RecipientUserId_SenderUserId_CreatedAt",
            table: "DirectMessages",
            columns: new[] { "RecipientUserId", "SenderUserId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_OrderId",
            table: "UserNotifications",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_OrderMessageId",
            table: "UserNotifications",
            column: "OrderMessageId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_PeerUserId",
            table: "UserNotifications",
            column: "PeerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_DirectMessageId",
            table: "UserNotifications",
            column: "DirectMessageId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_UserId_IsRead_CreatedAt",
            table: "UserNotifications",
            columns: new[] { "UserId", "IsRead", "CreatedAt" });
    }
}
