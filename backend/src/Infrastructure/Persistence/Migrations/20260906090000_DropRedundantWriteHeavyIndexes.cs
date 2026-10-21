using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Bỏ sáu index chỉ còn là chi phí ghi, và chừa chỗ trong trang cho HOT update.
///
/// Số đo: <c>Listings</c> 630 MB = 40 MB heap + <b>590 MB index</b>; <c>SupplierPurchases</c> 47 MB
/// = 1,6 MB heap + <b>46 MB index</b>. Nguyên nhân không phải cột <c>RawPayload</c> (nằm trong phần
/// heap/TOAST nhỏ kia) mà là MVCC: mỗi UPDATE không HOT được ghi thêm một entry vào <b>mọi</b> index
/// của bảng, kể cả index có cột không hề đổi. 12 index trên <c>Listings</c> nghĩa là mỗi lượt sync
/// đổi một listing phải ghi 12 chỗ.
///
/// Năm index một cột trên <c>Listings</c> bị bỏ đều đã được một composite mà query thật sự dùng che
/// phần cột dẫn đầu (<c>(OwnerId, PageType, SiteStatus)</c>, <c>(PageType, SiteStatus)</c>,
/// <c>(ShopId, PageType, AutoPublishSkipped, CreatedAt)</c>), và ba trong số đó phân biệt cực thấp
/// (một bool, hai cột vài giá trị chuỗi) nên planner chọn seq scan chứ không dùng.
///
/// <c>IX_SupplierPurchases_TrackingNo</c> chết hẳn: mọi query trên cột đó là <c>Contains</c>
/// (<c>LIKE '%…%'</c> — btree không phục vụ được, GIN <c>pg_trgm</c> mới phục vụ) hoặc
/// <c>TrackingNo IS NULL</c> trong bộ lọc hàng đợi mark-ship.
///
/// <c>fillfactor = 85</c> chừa 15% mỗi trang: sau khi bước ghi nhận SHEIN thôi stamp mốc mỗi lượt,
/// phần UPDATE còn lại chỉ chạm <c>LastSeenAt</c> / <c>LastRunId</c> — hai cột <b>không index</b> —
/// nên Postgres làm được HOT update ngay trong trang và không chạm index nào. Không có chỗ trống thì
/// bản mới nhảy sang trang khác và lại ghi cả 12 index. Chỉ có tác dụng với trang được ghi mới hoặc
/// sau REINDEX/VACUUM FULL, nên chạy kèm phần ops trong <c>docs/architecture.md</c>.
///
/// Giữ lại có chủ đích: <c>IX_Listings_Platform</c> (gần như một giá trị nên gần vô dụng, nhưng cần
/// số <c>idx_scan</c> từ <c>GET /admin/settings/storage-stats</c> trước khi bỏ) và toàn bộ GIN
/// <c>pg_trgm</c> của <c>Orders</c> (ô tìm kiếm đơn phụ thuộc trực tiếp).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260906090000_DropRedundantWriteHeavyIndexes")]
public class DropRedundantWriteHeavyIndexes : Migration
{
    private static readonly (string Table, string Index, string Column)[] Dropped =
    [
        ("Listings", "IX_Listings_OwnerId", "OwnerId"),
        ("Listings", "IX_Listings_PageType", "PageType"),
        ("Listings", "IX_Listings_SiteStatus", "SiteStatus"),
        ("Listings", "IX_Listings_ShopId", "ShopId"),
        ("Listings", "IX_Listings_AutoPublishSkipped", "AutoPublishSkipped"),
        ("SupplierPurchases", "IX_SupplierPurchases_TrackingNo", "TrackingNo")
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var (_, index, _) in Dropped)
        {
            migrationBuilder.Sql($"DROP INDEX IF EXISTS \"{index}\";");
        }

        migrationBuilder.Sql("ALTER TABLE \"SupplierPurchases\" SET (fillfactor = 85);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"SupplierPurchases\" RESET (fillfactor);");

        foreach (var (table, index, column) in Dropped)
        {
            migrationBuilder.Sql(
                $"CREATE INDEX IF NOT EXISTS \"{index}\" ON \"{table}\" (\"{column}\");");
        }
    }
}
