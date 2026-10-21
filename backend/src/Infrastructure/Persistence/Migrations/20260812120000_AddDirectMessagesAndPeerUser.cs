using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Direct messages + PeerUserId / DirectMessageId on UserNotifications.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260812120000_AddDirectMessagesAndPeerUser")]
public class AddDirectMessagesAndPeerUser : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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

        migrationBuilder.AddColumn<Guid>(
            name: "PeerUserId",
            table: "UserNotifications",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DirectMessageId",
            table: "UserNotifications",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_DirectMessages_SenderUserId_RecipientUserId_CreatedAt",
            table: "DirectMessages",
            columns: new[] { "SenderUserId", "RecipientUserId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_DirectMessages_RecipientUserId_SenderUserId_CreatedAt",
            table: "DirectMessages",
            columns: new[] { "RecipientUserId", "SenderUserId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_PeerUserId",
            table: "UserNotifications",
            column: "PeerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserNotifications_DirectMessageId",
            table: "UserNotifications",
            column: "DirectMessageId");

        migrationBuilder.AddForeignKey(
            name: "FK_UserNotifications_AspNetUsers_PeerUserId",
            table: "UserNotifications",
            column: "PeerUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_UserNotifications_DirectMessages_DirectMessageId",
            table: "UserNotifications",
            column: "DirectMessageId",
            principalTable: "DirectMessages",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_UserNotifications_AspNetUsers_PeerUserId",
            table: "UserNotifications");

        migrationBuilder.DropForeignKey(
            name: "FK_UserNotifications_DirectMessages_DirectMessageId",
            table: "UserNotifications");

        migrationBuilder.DropIndex(
            name: "IX_UserNotifications_PeerUserId",
            table: "UserNotifications");

        migrationBuilder.DropIndex(
            name: "IX_UserNotifications_DirectMessageId",
            table: "UserNotifications");

        migrationBuilder.DropColumn(
            name: "PeerUserId",
            table: "UserNotifications");

        migrationBuilder.DropColumn(
            name: "DirectMessageId",
            table: "UserNotifications");

        migrationBuilder.DropTable(name: "DirectMessages");
    }
}
