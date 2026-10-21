using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Extension online-session history (seller-us tab open windows) keyed by ExternalShopId.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260912100000_AddShopOnlineSessions")]
public class AddShopOnlineSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShopOnlineSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalShopId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ClientSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CloseReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShopOnlineSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShopOnlineSessions_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShopOnlineSessions_ShopId_ClientSessionId",
            table: "ShopOnlineSessions",
            columns: ["ShopId", "ClientSessionId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShopOnlineSessions_ShopId_StartedAt",
            table: "ShopOnlineSessions",
            columns: ["ShopId", "StartedAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_ShopOnlineSessions_ExternalShopId_StartedAt",
            table: "ShopOnlineSessions",
            columns: ["ExternalShopId", "StartedAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_ShopOnlineSessions_ShopId_Open",
            table: "ShopOnlineSessions",
            column: "ShopId",
            unique: true,
            filter: "\"EndedAt\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ShopOnlineSessions");
    }
}
