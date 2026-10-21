using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Nhiệm vụ hệ thống theo shop: định nghĩa nhiệm vụ truy cập URL (admin) + kết quả theo ngày VN.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823120000_AddShopTasks")]
public class AddShopTasks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShopTaskDefinitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                UrlMatchPattern = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShopTaskDefinitions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ShopTaskCompletions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalShopId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TaskDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                VisitedUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                DurationMs = table.Column<int>(type: "integer", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                RunId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShopTaskCompletions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShopTaskCompletions_ShopTaskDefinitions_DefinitionId",
                    column: x => x.DefinitionId,
                    principalTable: "ShopTaskDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ShopTaskCompletions_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShopTaskDefinitions_IsActive_SortOrder",
            table: "ShopTaskDefinitions",
            columns: new[] { "IsActive", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_ShopTaskCompletions_ShopId_DefinitionId_TaskDate",
            table: "ShopTaskCompletions",
            columns: new[] { "ShopId", "DefinitionId", "TaskDate" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShopTaskCompletions_TaskDate_OwnerId",
            table: "ShopTaskCompletions",
            columns: new[] { "TaskDate", "OwnerId" });

        migrationBuilder.CreateIndex(
            name: "IX_ShopTaskCompletions_DefinitionId",
            table: "ShopTaskCompletions",
            column: "DefinitionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ShopTaskCompletions");
        migrationBuilder.DropTable(name: "ShopTaskDefinitions");
    }
}
