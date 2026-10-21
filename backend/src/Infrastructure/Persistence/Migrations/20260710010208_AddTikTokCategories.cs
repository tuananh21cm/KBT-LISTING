using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTikTokCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TikTokCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ParentExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Site = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsLeaf = table.Column<bool>(type: "boolean", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TikTokCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TikTokCategories_Site_ExternalId",
                table: "TikTokCategories",
                columns: new[] { "Site", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TikTokCategories_Site_IsActive",
                table: "TikTokCategories",
                columns: new[] { "Site", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TikTokCategories_Site_ParentExternalId",
                table: "TikTokCategories",
                columns: new[] { "Site", "ParentExternalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TikTokCategories");
        }
    }
}
