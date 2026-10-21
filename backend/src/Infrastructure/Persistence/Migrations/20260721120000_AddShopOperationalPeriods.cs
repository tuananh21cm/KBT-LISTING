using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721120000_AddShopOperationalPeriods")]
public class AddShopOperationalPeriods : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShopOperationalPeriods",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                StartedOn = table.Column<DateOnly>(type: "date", nullable: false),
                EndedOn = table.Column<DateOnly>(type: "date", nullable: true),
                OpenSource = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CloseSource = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShopOperationalPeriods", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShopOperationalPeriods_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShopOperationalPeriods_ShopId",
            table: "ShopOperationalPeriods",
            column: "ShopId");

        migrationBuilder.CreateIndex(
            name: "IX_ShopOperationalPeriods_OwnerId",
            table: "ShopOperationalPeriods",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_ShopOperationalPeriods_OwnerId_StartedOn_EndedOn",
            table: "ShopOperationalPeriods",
            columns: new[] { "OwnerId", "StartedOn", "EndedOn" });

        migrationBuilder.CreateIndex(
            name: "IX_ShopOperationalPeriods_ShopId_EndedOn",
            table: "ShopOperationalPeriods",
            columns: new[] { "ShopId", "EndedOn" });

        migrationBuilder.CreateIndex(
            name: "IX_ShopOperationalPeriods_ShopId_Open",
            table: "ShopOperationalPeriods",
            column: "ShopId",
            unique: true,
            filter: "\"EndedOn\" IS NULL");

        // Backfill from current shop status + order history (VN calendar).
        // Active: open period from LEAST(first order day, today VN); no orders → today.
        // Inactive/Suspended with orders: closed period from first→last non-canceled order day.
        migrationBuilder.Sql(
            """
            WITH order_bounds AS (
                SELECT
                    o."ShopId",
                    MIN((COALESCE(o."OrderCreateTime", o."CreatedAt") AT TIME ZONE 'Asia/Ho_Chi_Minh')::date) AS first_day,
                    MAX((COALESCE(o."OrderCreateTime", o."CreatedAt") AT TIME ZONE 'Asia/Ho_Chi_Minh')::date) AS last_day
                FROM "Orders" o
                WHERE o."ShopId" IS NOT NULL
                  AND o."OrderStatus" NOT IN ('canceled', 'cancelled')
                GROUP BY o."ShopId"
            ),
            today_vn AS (
                SELECT (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS d
            )
            INSERT INTO "ShopOperationalPeriods"
                ("Id", "ShopId", "OwnerId", "StartedOn", "EndedOn", "OpenSource", "CloseSource", "CreatedAt", "UpdatedAt")
            SELECT
                gen_random_uuid(),
                s."Id",
                s."OwnerId",
                CASE
                    WHEN s."Status" = 'Active' THEN LEAST(COALESCE(ob.first_day, t.d), t.d)
                    ELSE ob.first_day
                END AS started_on,
                CASE
                    WHEN s."Status" = 'Active' THEN NULL
                    ELSE GREATEST(ob.first_day, ob.last_day)
                END AS ended_on,
                'Backfill',
                CASE
                    WHEN s."Status" = 'Active' THEN NULL
                    WHEN s."Status" = 'Suspended' THEN 'Suspended'
                    ELSE 'Inactive'
                END,
                NOW() AT TIME ZONE 'UTC',
                NULL
            FROM "Shops" s
            CROSS JOIN today_vn t
            LEFT JOIN order_bounds ob ON ob."ShopId" = s."Id"
            WHERE
                s."Status" = 'Active'
                OR (s."Status" IN ('Inactive', 'Suspended') AND ob.first_day IS NOT NULL);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ShopOperationalPeriods");
    }
}
