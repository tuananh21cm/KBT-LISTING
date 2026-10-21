-- Dọn dung lượng dbDropship.
--
-- Số đo thật (06-09-2026, qua GET /admin/settings/storage-stats, DB live ~0,98 GB):
--   Listings          629 MB = ~40 MB main heap + 579 MB TOAST + 10 MB index (9 index)
--   ListingVariations 181 MB = 131 MB heap + 50 MB index
--   SupplierPurchases  47 MB = 1,6 MB heap + 43,7 MB TOAST + 1,7 MB index (15 index)
--
-- Dung lượng nằm ở TOAST, KHÔNG ở index. Bộ ba total/table/index của pgAdmin từng bị đọc thành
-- "40 MB heap + 590 MB index" và dẫn tới kết luận sai là index bloat; 590 MB đó là TOAST.
--
-- Vì sao TOAST phình: Postgres không sửa tại chỗ giá trị TOAST. Mỗi UPDATE chạm một cột blob
-- (Listings.RawPayload/DescriptionHtml/SupplierDescriptionHtml, SupplierPurchases.RawPayload)
-- ghi một bộ chunk mới và bỏ bộ cũ thành dead. Dung lượng là hàm của SỐ LẦN GHI LẠI blob, không
-- phải số listing. Lúc đo, last_vacuum/last_autovacuum của cả hai bảng đều NULL và n_live_tup = 0
-- (thống kê vừa reset), nên CHƯA biết bao nhiêu trong 579 MB là chunk dead và bao nhiêu là payload
-- thật. VACUUM (ANALYZE) ở phần 2 là thứ trả lời câu đó: nó nạp lại n_live_tup.
--
-- Cũng đo được: Listings có 0/875 UPDATE dùng được HOT, SupplierPurchases 203/222 (91%). HOT đòi
-- không cột nào ĐƯỢC INDEX bị đổi, mà Listings.LastSyncedAt có index — nên mọi lần stamp mốc ghi
-- lại cả 9 index của bảng.
--
-- Đã xác nhận trên DB live: 6 index thừa (5 trên Listings + IX_SupplierPurchases_TrackingNo) đã
-- được migration drop; Listings còn 9 index / 9,6 MB, SupplierPurchases còn 15 index / 1,7 MB.
--
-- Thứ tự:
--   1. Deploy bản đã sửa (ApplyRawPayload thôi ghi lại payload y nguyên; bước ghi nhận SHEIN thôi
--      stamp mốc mỗi lượt) + migration 20260906090000_DropRedundantWriteHeavyIndexes.
--   2. Phần 1: đo. Xem TOAST chiếm bao nhiêu và dead_rows còn bao nhiêu.
--   3. Phần 2: VACUUM (ANALYZE) — việc quan trọng nhất ở đây. Đo lại sau đó.
--   4. Chỉ khi vẫn không giảm: VACUUM FULL trong cửa sổ bảo trì (cuối phần 2).
--   5. Phần 3 (REINDEX) là tùy chọn: index chỉ 10 MB nên lợi ích nhỏ.
--
-- Cách chạy: pgAdmin (Query Tool, autocommit) hoặc psql. REINDEX ... CONCURRENTLY KHÔNG chạy được
-- trong transaction block, nên đừng bọc BEGIN/COMMIT. API không bao giờ tự chạy các lệnh ở đây.


-- ===========================================================================
-- Phần 1 — Đo. Cùng số liệu với GET /api/v1/admin/settings/storage-stats.
-- ===========================================================================

-- 1a. Dung lượng theo bảng: tách main heap / TOAST / index, kèm dấu hiệu write amplification.
--     hot_ratio thấp = mỗi UPDATE ghi lại toàn bộ index của bảng.
SELECT c.relname                                                  AS "table",
       pg_size_pretty(pg_total_relation_size(c.oid))               AS total,
       pg_size_pretty(pg_relation_size(c.oid))                     AS main_heap,
       pg_size_pretty(CASE WHEN c.reltoastrelid = 0 THEN 0
                           ELSE pg_total_relation_size(c.reltoastrelid) END) AS toast,
       pg_size_pretty(pg_indexes_size(c.oid))                      AS indexes,
       (SELECT count(*) FROM pg_index i WHERE i.indrelid = c.oid)  AS index_count,
       s.n_live_tup                                                AS live_rows,
       s.n_dead_tup                                                AS dead_rows,
       s.n_tup_upd                                                 AS updates,
       CASE WHEN s.n_tup_upd > 0
            THEN round(100.0 * s.n_tup_hot_upd / s.n_tup_upd, 1)
       END                                                         AS hot_ratio_pct,
       s.last_autovacuum
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
LEFT JOIN pg_stat_user_tables s ON s.relid = c.oid
WHERE c.relkind = 'r' AND n.nspname = current_schema()
ORDER BY pg_total_relation_size(c.oid) DESC
LIMIT 25;

-- 1b. Cột blob nào chiếm TOAST. Lấy mẫu 2% để không phải scan 629 MB; nhân lên theo n_live_tup
--     nếu cần con số tuyệt đối. Đây là câu trả lời cho "bỏ/nén cột nào thì giảm được bao nhiêu".
SELECT count(*)                                              AS sampled_rows,
       pg_size_pretty(sum(pg_column_size("RawPayload"))::bigint)               AS raw_payload,
       pg_size_pretty(sum(pg_column_size("DescriptionHtml"))::bigint)          AS description_html,
       pg_size_pretty(sum(pg_column_size("SupplierDescriptionHtml"))::bigint)  AS supplier_desc_html,
       pg_size_pretty(sum(pg_column_size("MainImage"))::bigint)                AS main_image,
       pg_size_pretty(sum(pg_column_size("AppliedPricingJson"))::bigint)       AS applied_pricing
FROM "Listings" TABLESAMPLE SYSTEM (2);

-- 1c. Từng index của bốn bảng nặng nhất. idx_scan = 0 và không unique/PK là ứng viên DROP —
--     nhưng thống kê reset khi restart Postgres. Lượt đo 06-09-2026 báo scans = 0 cho cả hai GIN
--     pg_trgm mà ô tìm kiếm chắc chắn dùng, nên đừng tin số 0 ngay sau reset: chờ vài ngày rồi đo
--     lại, và đối chiếu query trước khi xoá.
SELECT s.relname                                    AS "table",
       s.indexrelname                               AS index_name,
       am.amname                                    AS method,
       pg_size_pretty(pg_relation_size(s.indexrelid)) AS size,
       s.idx_scan                                   AS scans,
       i.indisunique                                AS is_unique,
       i.indisprimary                               AS is_primary,
       regexp_replace(pg_get_indexdef(s.indexrelid), '^.* USING [a-z_]+ ', '') AS columns
FROM pg_stat_user_indexes s
JOIN pg_index i ON i.indexrelid = s.indexrelid
JOIN pg_class ic ON ic.oid = s.indexrelid
JOIN pg_am am ON am.oid = ic.relam
WHERE s.schemaname = current_schema()
  AND s.relname IN ('Listings', 'ListingVariations', 'SupplierPurchases', 'Orders')
ORDER BY pg_relation_size(s.indexrelid) DESC;


-- ===========================================================================
-- Phần 2 — VACUUM. Việc quan trọng nhất: đây là thứ thu hồi chunk TOAST dead.
-- ===========================================================================

-- VACUUM thường KHÔNG khoá ghi. Nó đánh dấu chunk dead là dùng lại được cho lần ghi sau, nên
-- pg_total_relation_size có thể không đổi ngay — nhưng bảng thôi lớn thêm. Chạy phần 1 lại sau
-- đó và so dead_rows.
VACUUM (ANALYZE) "Listings";
VACUUM (ANALYZE) "SupplierPurchases";
VACUUM (ANALYZE) "Orders";
VACUUM (ANALYZE) "ListingVariations";

-- Chỉ khi phần 1 vẫn cho thấy TOAST rất lớn sau khi đã VACUUM thường: VACUUM FULL viết lại bảng
-- + TOAST + toàn bộ index, trả chỗ về hệ điều hành. Nhưng nó KHOÁ TOÀN BẢNG (ACCESS EXCLUSIVE)
-- suốt thời gian chạy — mọi request đọc/ghi bảng đó sẽ treo, và cần chỗ đĩa trống bằng kích cỡ
-- bảng. Chỉ chạy trong cửa sổ bảo trì:
--   VACUUM FULL "Listings";            -- ~629 MB, lệnh lâu nhất
--   VACUUM FULL "SupplierPurchases";
--
-- VACUUM FULL đã bao gồm việc dựng lại index, nên nếu chạy nó thì bỏ qua phần 3.
-- Nó cũng là lệnh làm fillfactor 85 (migration đặt cho "SupplierPurchases") có tác dụng trên
-- toàn bảng thay vì chỉ trang được ghi mới.


-- ===========================================================================
-- Phần 3 — REINDEX CONCURRENTLY (tùy chọn). Không khoá ghi; tốn gấp đôi chỗ đĩa tạm thời.
-- Index của Listings chỉ 10 MB nên lợi ích dung lượng nhỏ; chạy nếu phần 1c cho thấy index nào
-- phình bất thường so với số dòng.
-- ===========================================================================

-- Chạy từng bảng, không song song (mỗi lệnh dựng bản sao của mọi index của bảng đó).
REINDEX TABLE CONCURRENTLY "Listings";
REINDEX TABLE CONCURRENTLY "SupplierPurchases";
REINDEX TABLE CONCURRENTLY "Orders";
REINDEX TABLE CONCURRENTLY "ListingVariations";

-- Nếu một lệnh bị hủy giữa đường, Postgres để lại index "_ccnew" dở dang — tìm và xoá:
--   SELECT indexrelid::regclass FROM pg_index
--   WHERE NOT indisvalid AND indexrelid::regclass::text LIKE '%_ccnew%';
--   DROP INDEX CONCURRENTLY "<tên>";
