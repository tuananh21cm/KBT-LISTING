using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Lookup table for shop target-audience age brackets (Select presets + marketing copy).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814010000_AddAgeRanges")]
public class AddAgeRanges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AgeRanges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AgeMin = table.Column<int>(type: "integer", nullable: false),
                AgeMax = table.Column<int>(type: "integer", nullable: false),
                PainPoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Desire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ProductGroups = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgeRanges", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AgeRanges_Code",
            table: "AgeRanges",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AgeRanges_SortOrder",
            table: "AgeRanges",
            column: "SortOrder");

        migrationBuilder.Sql(
            """
            INSERT INTO "AgeRanges" ("Id", "Code", "Label", "AgeMin", "AgeMax", "PainPoint", "Desire", "ProductGroups", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES
              (
                'b1000001-0000-4000-8000-000000000001',
                '18-24',
                '18–24',
                18,
                24,
                'Muốn đẹp, bắt trend, giá hợp lý; sợ outfit trông “basic”',
                'Trendy + nổi bật + dễ phối',
                'Y2K, casual tops, mini/maxi dress, matching sets, denim',
                1,
                TRUE,
                NOW() AT TIME ZONE 'utc',
                NULL
              ),
              (
                'b1000001-0000-4000-8000-000000000002',
                '25-34',
                '25–34',
                25,
                34,
                'Thiếu thời gian, cần mặc đẹp nhưng vẫn thoải mái; muốn outfit phù hợp nhiều hoàn cảnh',
                'Cute + practical + versatile',
                'Casual tops, dresses, wide-leg pants, sets, work-to-casual',
                2,
                TRUE,
                NOW() AT TIME ZONE 'utc',
                NULL
              ),
              (
                'b1000001-0000-4000-8000-000000000003',
                '35-44',
                '35–44',
                35,
                44,
                'Body change, fit khó, bụng/eo/hông, plus size; muốn đẹp nhưng không muốn mặc quá trẻ',
                'Flattering + comfortable + confidence',
                'Plus-size tops, dresses, wide-leg pants, tunics, stretchy jeans',
                3,
                TRUE,
                NOW() AT TIME ZONE 'utc',
                NULL
              ),
              (
                'b1000001-0000-4000-8000-000000000004',
                '45-54',
                '45–54',
                45,
                54,
                'Fit + comfort trở thành vấn đề lớn; không muốn quần áo quá bó hoặc quá “old-fashioned”',
                'Comfortable nhưng vẫn stylish',
                'Relaxed tops, tunics, cardigans, wide-leg pants, midi/maxi dresses',
                4,
                TRUE,
                NOW() AT TIME ZONE 'utc',
                NULL
              ),
              (
                'b1000001-0000-4000-8000-000000000005',
                '55-64',
                '55–64',
                55,
                64,
                'Comfort, chất liệu, độ dễ mặc, che khuyết điểm; ít chấp nhận sản phẩm chỉ đẹp trên ảnh',
                'Easy wear + flattering + quality',
                'Soft tops, loose dresses, pull-on pants, cardigans, lightweight jackets',
                5,
                TRUE,
                NOW() AT TIME ZONE 'utc',
                NULL
              ),
              (
                'b1000001-0000-4000-8000-000000000006',
                '65-plus',
                '65+',
                65,
                100,
                'Dễ mặc, dễ phối, thoải mái, không phức tạp; fit và chất liệu quan trọng',
                'Comfort + simplicity + practicality',
                'Pull-on pants, loose tops, dresses, cardigans, comfortable sets',
                6,
                TRUE,
                NOW() AT TIME ZONE 'utc',
                NULL
              )
            ON CONFLICT ("Id") DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AgeRanges");
    }
}
