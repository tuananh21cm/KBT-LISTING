using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformsAndShopPlatformFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Platforms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Platforms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Platforms_Code",
                table: "Platforms",
                column: "Code",
                unique: true);

            var tiktokId = Guid.NewGuid();
            var amazonId = Guid.NewGuid();
            var shopifyId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            migrationBuilder.InsertData(
                table: "Platforms",
                columns: new[] { "Id", "Code", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { tiktokId, "TikTok", "TikTok Shop", null, true, now, null },
                    { amazonId, "Amazon", "Amazon", null, true, now, null },
                    { shopifyId, "Shopify", "Shopify", null, true, now, null }
                });

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformId",
                table: "Shops",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql($"""
                UPDATE "Shops"
                SET "PlatformId" = '{tiktokId}'
                WHERE "PlatformId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "PlatformId",
                table: "Shops",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Shops");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_PlatformId",
                table: "Shops",
                column: "PlatformId");

            migrationBuilder.AddForeignKey(
                name: "FK_Shops_Platforms_PlatformId",
                table: "Shops",
                column: "PlatformId",
                principalTable: "Platforms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shops_Platforms_PlatformId",
                table: "Shops");

            migrationBuilder.DropIndex(
                name: "IX_Shops_PlatformId",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "PlatformId",
                table: "Shops");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Shops",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "TikTokShopUS");

            migrationBuilder.DropTable(
                name: "Platforms");
        }
    }
}
