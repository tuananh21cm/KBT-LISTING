using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSourcesAndExternalIds : Migration
    {
        private static readonly Guid SheinSourceId = new("a1000000-0000-0000-0000-000000000001");
        private static readonly Guid TemuSourceId = new("a1000000-0000-0000-0000-000000000002");
        private static readonly Guid CjSourceId = new("a1000000-0000-0000-0000-000000000003");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSources_Code",
                table: "ProductSources",
                column: "Code",
                unique: true);

            var now = DateTime.UtcNow;
            migrationBuilder.InsertData(
                table: "ProductSources",
                columns: new[] { "Id", "Code", "Name", "LogoUrl", "Description", "IsActive", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { SheinSourceId, "SHEIN", "Shein", null, "Nguồn hàng Shein", true, now, null },
                    { TemuSourceId, "TEMU", "Temu", null, "Nguồn hàng Temu", true, now, null },
                    { CjSourceId, "CJ", "CJDropshipping", null, "Nguồn hàng CJDropshipping", true, now, null }
                });

            migrationBuilder.DropIndex(
                name: "IX_Products_OwnerId_SheinProductId",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "SheinSku",
                table: "Products",
                newName: "ExternalSku");

            migrationBuilder.RenameColumn(
                name: "SheinProductId",
                table: "Products",
                newName: "ExternalProductId");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValue: SheinSourceId);

            migrationBuilder.CreateIndex(
                name: "IX_Products_OwnerId_SourceId_ExternalProductId",
                table: "Products",
                columns: new[] { "OwnerId", "SourceId", "ExternalProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SourceId",
                table: "Products",
                column: "SourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductSources_SourceId",
                table: "Products",
                column: "SourceId",
                principalTable: "ProductSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductSources_SourceId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductSources");

            migrationBuilder.DropIndex(
                name: "IX_Products_OwnerId_SourceId_ExternalProductId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SourceId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "ExternalSku",
                table: "Products",
                newName: "SheinSku");

            migrationBuilder.RenameColumn(
                name: "ExternalProductId",
                table: "Products",
                newName: "SheinProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_OwnerId_SheinProductId",
                table: "Products",
                columns: new[] { "OwnerId", "SheinProductId" },
                unique: true);
        }
    }
}
