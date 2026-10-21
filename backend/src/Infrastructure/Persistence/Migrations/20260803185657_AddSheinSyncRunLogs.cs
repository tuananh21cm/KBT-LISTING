using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <summary>Bảng nhật ký từng lô Sync Order SHEIN (giữ 7 ngày).</summary>
    public partial class AddSheinSyncRunLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SheinSyncRunLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BatchIndex = table.Column<int>(type: "integer", nullable: true),
                    UpdateMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LookbackDays = table.Column<int>(type: "integer", nullable: false),
                    PoolSize = table.Column<int>(type: "integer", nullable: false),
                    Fetched = table.Column<int>(type: "integer", nullable: false),
                    Matched = table.Column<int>(type: "integer", nullable: false),
                    Unmatched = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    UpdatedTracking = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCost = table.Column<int>(type: "integer", nullable: false),
                    SkipCountsJson = table.Column<string>(type: "text", nullable: true),
                    SkipOutcomesJson = table.Column<string>(type: "text", nullable: true),
                    NeedsDetailReasonsJson = table.Column<string>(type: "text", nullable: true),
                    FulfilAccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SheinSyncRunLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SheinSyncRunLogs_CreatedAt",
                table: "SheinSyncRunLogs",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SheinSyncRunLogs_RunId",
                table: "SheinSyncRunLogs",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SheinSyncRunLogs");
        }
    }
}
