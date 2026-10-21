using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260712083000_AddScrapeBatchesAndDrafts")]
public class AddScrapeBatchesAndDrafts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ScrapeBatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SubmittedUrlCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScrapeBatches", x => x.Id);
                table.ForeignKey(
                    name: "FK_ScrapeBatches_AspNetUsers_CreatedById",
                    column: x => x.CreatedById,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ScrapeBatchUrls",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                SourceUrlKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                MatchedMoveDraftId = table.Column<long>(type: "bigint", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScrapeBatchUrls", x => x.Id);
                table.ForeignKey(
                    name: "FK_ScrapeBatchUrls_ScrapeBatches_BatchId",
                    column: x => x.BatchId,
                    principalTable: "ScrapeBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ScrapeDrafts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                FourSellerMoveDraftId = table.Column<long>(type: "bigint", nullable: false),
                BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SourceUrlKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                VariantCount = table.Column<int>(type: "integer", nullable: false),
                LowPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                HighPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                Creator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                FourSellerCreateTime = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScrapeDrafts", x => x.Id);
                table.ForeignKey(
                    name: "FK_ScrapeDrafts_AspNetUsers_CreatedById",
                    column: x => x.CreatedById,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ScrapeDrafts_ScrapeBatches_BatchId",
                    column: x => x.BatchId,
                    principalTable: "ScrapeBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeBatches_CreatedAt",
            table: "ScrapeBatches",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeBatches_CreatedById",
            table: "ScrapeBatches",
            column: "CreatedById");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeBatchUrls_BatchId",
            table: "ScrapeBatchUrls",
            column: "BatchId");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeBatchUrls_MatchedMoveDraftId",
            table: "ScrapeBatchUrls",
            column: "MatchedMoveDraftId");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeBatchUrls_SourceUrlKey",
            table: "ScrapeBatchUrls",
            column: "SourceUrlKey");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeDrafts_BatchId",
            table: "ScrapeDrafts",
            column: "BatchId");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeDrafts_CreatedById",
            table: "ScrapeDrafts",
            column: "CreatedById");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeDrafts_CreatedById_LastSeenAt",
            table: "ScrapeDrafts",
            columns: new[] { "CreatedById", "LastSeenAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeDrafts_FourSellerMoveDraftId",
            table: "ScrapeDrafts",
            column: "FourSellerMoveDraftId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeDrafts_SourceUrlKey",
            table: "ScrapeDrafts",
            column: "SourceUrlKey");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ScrapeBatchUrls");
        migrationBuilder.DropTable(name: "ScrapeDrafts");
        migrationBuilder.DropTable(name: "ScrapeBatches");
    }
}
