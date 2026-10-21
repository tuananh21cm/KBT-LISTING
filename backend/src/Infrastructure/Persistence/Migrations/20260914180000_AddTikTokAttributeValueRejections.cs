using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Product attribute values TikTok rejects as custom value names, learned from publish failures,
/// scoped per category because one attribute id carries a different enum per category.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914180000_AddTikTokAttributeValueRejections")]
public class AddTikTokAttributeValueRejections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TikTokAttributeValueRejections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AttributeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AttributeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ValueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ValueName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                ValueNameNorm = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                RejectionCount = table.Column<int>(type: "integer", nullable: false),
                FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                SampleListingId = table.Column<Guid>(type: "uuid", nullable: true),
                SampleError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TikTokAttributeValueRejections", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TikTokAttributeValueRejections_CategoryId_AttributeId_ValueNameNorm",
            table: "TikTokAttributeValueRejections",
            columns: ["CategoryId", "AttributeId", "ValueNameNorm"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TikTokAttributeValueRejections_AttributeId",
            table: "TikTokAttributeValueRejections",
            column: "AttributeId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TikTokAttributeValueRejections");
    }
}
