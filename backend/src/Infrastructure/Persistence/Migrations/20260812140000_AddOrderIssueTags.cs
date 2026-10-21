using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Issue tag library + per-order assignments + Orders.IssueStatus; backfill from Message.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260812140000_AddOrderIssueTags")]
public class AddOrderIssueTags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IssueStatus",
            table: "Orders",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "None");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_IssueStatus",
            table: "Orders",
            column: "IssueStatus");

        migrationBuilder.CreateTable(
            name: "IssueTags",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IssueTags", x => x.Id);
                table.ForeignKey(
                    name: "FK_IssueTags_AspNetUsers_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IssueTags_Label",
            table: "IssueTags",
            column: "Label",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IssueTags_CreatedByUserId",
            table: "IssueTags",
            column: "CreatedByUserId");

        migrationBuilder.CreateTable(
            name: "OrderIssueTags",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                IssueTagId = table.Column<Guid>(type: "uuid", nullable: false),
                AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderIssueTags", x => new { x.OrderId, x.IssueTagId });
                table.ForeignKey(
                    name: "FK_OrderIssueTags_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_OrderIssueTags_IssueTags_IssueTagId",
                    column: x => x.IssueTagId,
                    principalTable: "IssueTags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OrderIssueTags_AspNetUsers_AssignedByUserId",
                    column: x => x.AssignedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderIssueTags_IssueTagId",
            table: "OrderIssueTags",
            column: "IssueTagId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderIssueTags_AssignedByUserId",
            table: "OrderIssueTags",
            column: "AssignedByUserId");

        // Seed system presets via SQL (InsertData requires entity in model snapshot).
        migrationBuilder.Sql(
            """
            INSERT INTO "IssueTags" ("Id", "Label", "IsSystem", "CreatedByUserId", "CreatedAt", "UpdatedAt")
            VALUES
              ('a1000001-0000-4000-8000-000000000001', 'Hết size', TRUE, NULL, NOW() AT TIME ZONE 'utc', NULL),
              ('a1000001-0000-4000-8000-000000000002', 'Giá cao', TRUE, NULL, NOW() AT TIME ZONE 'utc', NULL),
              ('a1000001-0000-4000-8000-000000000003', 'Hết hàng', TRUE, NULL, NOW() AT TIME ZONE 'utc', NULL),
              ('a1000001-0000-4000-8000-000000000004', 'Sai màu', TRUE, NULL, NOW() AT TIME ZONE 'utc', NULL),
              ('a1000001-0000-4000-8000-000000000005', 'Shop không ship', TRUE, NULL, NOW() AT TIME ZONE 'utc', NULL),
              ('a1000001-0000-4000-8000-000000000006', 'Outstock', TRUE, NULL, NOW() AT TIME ZONE 'utc', NULL)
            ON CONFLICT ("Id") DO NOTHING;
            """);

        // Backfill: exact system preset match → assign; else create custom tag from Message.
        migrationBuilder.Sql(
            """
            INSERT INTO "OrderIssueTags" ("OrderId", "IssueTagId", "AssignedByUserId", "AssignedAt")
            SELECT o."Id", t."Id", NULL, NOW() AT TIME ZONE 'utc'
            FROM "Orders" o
            INNER JOIN "IssueTags" t ON t."IsSystem" = TRUE AND lower(t."Label") = lower(trim(o."Message"))
            WHERE o."Message" IS NOT NULL AND trim(o."Message") <> ''
            ON CONFLICT DO NOTHING;

            UPDATE "Orders" o
            SET "IssueStatus" = 'Processing'
            FROM "IssueTags" t
            WHERE o."Message" IS NOT NULL AND trim(o."Message") <> ''
              AND t."IsSystem" = TRUE AND lower(t."Label") = lower(trim(o."Message"));

            -- Custom messages: one tag per distinct trimmed message (max 50).
            INSERT INTO "IssueTags" ("Id", "Label", "IsSystem", "CreatedByUserId", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), left(trim(o."Message"), 50), FALSE, NULL, NOW() AT TIME ZONE 'utc', NULL
            FROM "Orders" o
            WHERE o."Message" IS NOT NULL AND trim(o."Message") <> ''
              AND NOT EXISTS (
                SELECT 1 FROM "IssueTags" t
                WHERE lower(t."Label") = lower(left(trim(o."Message"), 50))
              )
            GROUP BY left(trim(o."Message"), 50);

            INSERT INTO "OrderIssueTags" ("OrderId", "IssueTagId", "AssignedByUserId", "AssignedAt")
            SELECT o."Id", t."Id", NULL, NOW() AT TIME ZONE 'utc'
            FROM "Orders" o
            INNER JOIN "IssueTags" t ON lower(t."Label") = lower(left(trim(o."Message"), 50))
            WHERE o."Message" IS NOT NULL AND trim(o."Message") <> ''
              AND NOT EXISTS (
                SELECT 1 FROM "OrderIssueTags" x WHERE x."OrderId" = o."Id"
              )
            ON CONFLICT DO NOTHING;

            UPDATE "Orders"
            SET "IssueStatus" = 'Processing'
            WHERE "Message" IS NOT NULL AND trim("Message") <> ''
              AND "IssueStatus" = 'None';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OrderIssueTags");
        migrationBuilder.DropTable(name: "IssueTags");
        migrationBuilder.DropIndex(name: "IX_Orders_IssueStatus", table: "Orders");
        migrationBuilder.DropColumn(name: "IssueStatus", table: "Orders");
    }
}
