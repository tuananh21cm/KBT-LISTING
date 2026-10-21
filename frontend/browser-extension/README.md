# KBT Craper (Browser Extension)

Extension Chrome/Edge (Manifest V3) cho dự án **KBT Global**: đăng nhập bằng tài khoản **Seller**, đẩy sản phẩm từ trang site nguồn (SHEIN, Temu, …) vào **draft 4Seller** qua API `https://4seller-api.kbt.global` (cookie 4Seller không bao giờ rời server), và xem listing Draft/Publish theo shop đang chọn.

> Giao diện mở dạng **Side Panel** cạnh trang web (bấm icon extension), không phải popup nhỏ.

## Tính năng

- Đăng nhập KBT Global (JWT, host `4seller-api.kbt.global`) ngay trong side panel — **chỉ tài khoản Seller** (hoặc Admin / có quyền `seller.*`). Phiên lưu `chrome.storage.local` đến khi **Đăng xuất** hoặc **gỡ/cài lại** extension (login/refresh `persistent: true`); **Đăng xuất** xóa toàn bộ thông tin đã lưu.
- Kiểm tra phiên 4Seller qua `POST /auth/four-seller-cookie/check-login` (cookie giữ trên server).
- Nút **"Thêm sản phẩm"** (`kbt-add-product-btn`) hiện trên trang PDP thuộc nguồn đã cấu hình. Active khi: đã đăng nhập kinhbac + phiên 4Seller live + đã chọn ≥1 shop + trang đã tải. Nếu chưa login / chưa chọn shop → nút xám mở side panel. Sau khi thêm thành công, product id lưu `kbt_added_product_ids` → nút **Đã thêm** + banner Size Chart `#kbt-size-chart-warn` nếu chưa upload. Nếu extension bị reload (lỗi `Extension context invalidated`) → ẩn nút; F5 để gắn lại.
- **Shein danh sách / search / recommend PDP / store panel / category list-v2:** badge ✓ xanh/cam trên ảnh product card (cùng `kbt_added_product_ids`) — gồm `.product-list`, `.recommend-bottom_list`, `#store-panel-items`, **`#product-list-v2`** (layout `product-list-v2__main_view-new`, ô grid không cần class `.product-card`). Nút **Thêm nhanh** (`+`) cạnh badge: click hàng loạt → hàng đợi + tối đa **5** tab PDP nền song song → capture → `post-html` (UI: trong hàng đợi / đang thêm / lỗi / ✓). **Thêm OK giữ tab** để upload Size Chart nếu muốn; **đóng tab thì thôi** (không mở lại). FAB / side panel trên PDP user đang xem: banner Size Chart; đóng tab chưa upload → toast + **mở lại tab**.
- Đẩy sản phẩm: chọn **≥1 shop Active** → `POST /fourseller/crawl/post-html` (proxy `postHtml-file-shop` trên server).
- **Draft / Publish / Search:** 3 tab header ngoài card nội dung (**mặc định Search**). Draft hiện số draft local; Publish hiện `published/limit` TTS (cùng Listing `156/200` trên modal shop). Số lấy `GET /listings/tab-counts` lúc mở panel / đổi shop, không cần click tab. Draft/Publish pager 20/trang. `GET /listings/drafts?pageType=draft|published&fourSellerShopIds=&pageCurrent=&pageSize=20` (local DB, lọc shop đang chọn, paging trên server). Hover dòng draft → icon xóa (`DELETE /listings/{id}`) và bỏ product id khỏi `kbt_added_product_ids` (tab **Publish** không cho xóa). **Làm mới** / đổi tab / đổi shop load lại trang 1. Sau **Thêm sản phẩm** OK: chuyển tab Draft và reload lúc ~14s / ~30s / ~44s (chờ sync 12s + retry nếu `SyncGate` bận).
- **Hôm nay bán gì?:** khi chọn **đúng 1 shop**, nút Phân tích → bullet phân tích + 2 keyword đang win + 3 keyword sắp tới. Link Shein dạng RecentSearch/`historyWord` (không gắn band giá vào URL — tránh risk/limit); band giá chỉ hiện meta.
- **Tìm sản phẩm:** tab **Search** (header ngoài card, cùng hàng Draft/Publish). Hồ sơ danh mục + keyword đã lưu (tag **List ngay** / **Đón đầu** trong dropdown; hàng 1 = nhãn Trending keyword + copy URL + limit + icon tải lại; hàng 2 = combobox full-width chọn đúng 1 keyword). **Tìm kiếm** ghi catalog shop, **chỉ reload tab Shein đang xem** (reset `sessionStorage` tab đó, giữ cookie; không xóa cache origin-wide) rồi mở **đúng 1 URL Shein EditSearch/`insideFilter`** (HAR: `st=2`, `PageSearchResult`. **URL:** body/style/giá/QuickShip/Sort by khi có giá trị — bỏ Style thì không `attr_*`/`filter`, bỏ QuickShip thì không `tag_ids`, Recommend thì không `sort`; đổi bộ lọc phải commit đúng query (`sheinWarmupFilterKey`) rồi mới reload — không reload URL cũ. **Sau JSON:** Rating/Sold/Local/Club — Local = `titleLabelList.labelType=local_stock` (Prefer Local chỉ giữ SP có tag). **Sort by** gắn warmup URL + BFF (`source=sort`/`sourceStatus=4`/`sort`; Recommend = `insideFilter` không `sort`); list luôn xếp điểm giảm dần (Sort by chỉ lấy data Shein). Warmup không `cloudTag`) bằng `chrome.tabs.update` (giống dán URL thanh địa chỉ) **chỉ để lấy header** `get_products_by_keywords`. Chờ trang load xong (`complete`) rồi mới `fetch` **POST** API BFF (đúng method SPA; không hardcode GET); không bắt đầu lấy list khi header SPA tới sớm lúc đang loading. Mọi trang sau đó tự `fetch` **POST** API BFF (`limit=120`, tối thiểu 4 trang/keyword ≈ 480 SP rồi mới lọc; nếu Shein cap ~30/trang thì lấy thêm tới ~300), không mở lại URL keyword, không lấy list từ hook SPA; lọc rating/sold/variants + **cổng tiêu đề** (độ phủ, cùng rule backend: tối đa 2 từ nhu cầu + loại SP, ≥ `ceil(N/2)` trên tập đã cắt, không AND-all; đồng nghĩa `denim`=`jean`, `off-shoulder`=`bardot`, `ruffled`=`ruffle`; dưới ngưỡng → loại; điểm title = trọng số cụm khớp), chấm điểm tuyệt đối 0–100 (**khớp tiêu đề 40%** + **Wilson 35%** + **sao 15%** + **giá 10%**, không percentile pool) rồi cắt số lượng (mặc định **30**/keyword, max 50) — điểm + 5 sao nguồn + giá bán + sold + badge Local/`Nhanh`/`Club` (cùng chip Draft/Publish; Club từ JSON Search) hiện trên từng dòng kết quả. Nếu tab ra trang risk thì dừng và báo hoàn thành captcha. Danh sách kết quả **không checkbox** — mặc định thêm hết; bỏ SP bằng nút ×. Số thứ tự tô màu cùng `kbt_added_product_ids` (xanh = đủ shop đang chọn, cam = thiếu shop) — live khi thêm PDP / batch Search / xóa Draft. Click tên SP đổi màu tím (chỉ 1 link active; click SP khác thì bỏ cái cũ) và mở PDP tab mới. **Thêm sản phẩm (N)** mở PDP lần lượt, chỉ `post-html` khi URL khớp `goodsId`. Nút Thêm sản phẩm cũ vẫn thêm tab hiện tại.
- **Đồng bộ «Đã thêm»:** map local gộp với `GET /listings/added-source-shops` khi mở panel, **Làm mới** list, **Tải lại trang**, và sau xóa draft. Mark vừa thêm (chưa kịp vào DB) giữ **10 phút**; hết grace mà DB không có thì bỏ — tránh badge/nút stale khi listing đã xóa, đồng thời không mất «Đã thêm» ngay sau khi thêm thành công + F5.
- Brand side panel: **KBT SELLER**.

## Kiến trúc

Extension **chỉ gọi API kinhbac** (JWT). Cookie 4Seller lưu trên server và được backend gắn khi proxy sang 4Seller — extension không nhận/nạp cookie, không gọi `4seller.com`.

- Service worker (`src/background`) giữ toàn bộ logic mạng: kinhbac API; mở Side Panel khi bấm icon (`chrome.sidePanel`).
- Content script (`src/content`) chèn nút và lấy HTML trang nguồn (standalone, không import). Metric NCC (rating / stock / shop / SLA) lấy từ intercept `get_goods_detail_realtime_data` (`public/sheinReviewCapture.js`) — **không** bóc HTML. SLA = `dayPercentDesc` / `dayPercents` (không dùng `shippingDayPercentDesc` toàn sàn). Cache `sessionStorage` theo tab + map `goodsId` (nhiều tab / prefetch không lẫn SP). Cùng file hook `get_products_by_keywords` bắt header + **method/body** (`kbt_shein_search_session`); list search do content `fetch` **POST** replay BFF, không dùng JSON SPA.
- Side panel UI (`src/popup` → `sidepanel.html`, React) chỉ gửi message tới service worker.

## Cấu hình

Env theo mode Vite (đã có sẵn trong repo):

| File | `VITE_API_BASE_URL` |
|---|---|
| `.env.production` | `https://4seller-api.kbt.global/api/v1` |
| `.env.development` | `http://localhost:5000/api/v1` |

`public/manifest.json` đã có `host_permissions` cho cả hai origin (`4seller-api.kbt.global` + `localhost:5000`). Đổi port/host API → cập nhật manifest cho khớp.

## Build & Load

```bash
npm install
npm run build        # ⚠ trỏ API production
npm run build:local  # API localhost:5000 (đọc `.env.development`)
npm run dev          # watch + cùng env local như build:local
```

Load vào Chrome/Edge:

1. Mở `chrome://extensions` (hoặc `edge://extensions`).
2. Bật **Developer mode**.
3. **Load unpacked** → chọn thư mục `frontend/browser-extension/dist`.
4. Sau mỗi rebuild local: **Reload** extension trên `chrome://extensions`.

## Cách dùng

1. Đăng nhập 4Seller trong hồ sơ kinhbac (lưu cookie tại **Hồ sơ cá nhân** trên seller-web / admin gán trong **Quản lý Người dùng**), rồi **Check login**.
2. Cấu hình domain nguồn tại **Admin → Nguồn hàng** (field *Domain nguồn*, ví dụ `shein.com, us.shein.com`).
3. Bấm icon extension → **Side Panel** mở cạnh trang → đăng nhập bằng tài khoản **Seller**.
4. Vào **trang chi tiết sản phẩm** site nguồn (URL PDP — vd. SHEIN có `-p-…`). Extension tự nhận diện luồng theo URL → nút **Thêm sản phẩm** mới active.
5. Bấm **Thêm sản phẩm** (nút nổi hoặc side panel) → xem tab **Draft / Publish** (theo shop đang chọn).

## Lưu ý

- Cookie 4Seller **không** được trả về client / extension.
- Tài khoản không có quyền Seller sẽ bị từ chối khi đăng nhập extension.
- HTML trang sản phẩm vẫn capture trên trình duyệt (vượt anti-bot), rồi gửi lên API hệ thống để proxy sang 4Seller. Rating / stock / seller / SLA (`dayPercentDesc`) lấy từ JSON realtime SHEIN (cùng request trang đã gọi), không parse HTML.
