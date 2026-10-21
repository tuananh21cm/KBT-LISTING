using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShopAvatarAndOptionalExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shops_OwnerId_ExternalShopId",
                table: "Shops");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalShopId",
                table: "Shops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.Sql("""
                UPDATE "Shops" SET "ExternalShopId" = NULL WHERE "ExternalShopId" = '';
                """);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Shops",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_OwnerId_ExternalShopId",
                table: "Shops",
                columns: new[] { "OwnerId", "ExternalShopId" },
                unique: true,
                filter: "\"ExternalShopId\" IS NOT NULL AND \"ExternalShopId\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shops_OwnerId_ExternalShopId",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Shops");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalShopId",
                table: "Shops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_OwnerId_ExternalShopId",
                table: "Shops",
                columns: new[] { "OwnerId", "ExternalShopId" },
                unique: true);
        }
    }
}
