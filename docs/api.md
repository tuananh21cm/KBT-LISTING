# API Documentation

## Base URL

| Môi trường | Base |
|------------|------|
| Development | `http://localhost:5000` |
| Production | `https://4seller-api.kbt.global` |
| Prefix | `/api/v1` |

**admin-web** và **seller-web** gọi API qua `VITE_API_BASE_URL` (dev: `http://localhost:5000/api/v1`, prod build: `https://4seller-api.kbt.global/api/v1`). SPA host: admin `https://kinhbac.ai` / `https://www.kinhbac.ai`, seller `https://seller.kinhbac.ai` (cross-origin → `Cors:AllowedOrigins` phải gồm cả ba). Browser Extension (side panel / service worker) gửi `Origin: chrome-extension://…` — production cũng phải echo ACAO cho scheme đó (JWT vẫn bắt buộc). Cả hai app có `public/web.config` (IIS SPA rewrite) — xem `architecture.md` § Hosting. SPA admin trang trắng sau deploy (chunk JS `Content-Length: 0` khi có header `Origin`) cũng nằm ở mục đó.

**Dev CORS / ports cố định:**

| App | Port | Ghi chú |
|-----|------|---------|
| API | `5000` | `launchSettings.json` → `http://localhost:5000` |
| seller-web | `5173` | Vite `strictPort: true` |
| admin-web | `5174` | Vite `strictPort: true` |

`.env.development` trỏ thẳng `VITE_API_BASE_URL=http://localhost:5000/api/v1` (không qua Vite proxy) → browser gửi Origin `http://localhost:5173|5174`. API cho phép origin `Cors:AllowedOrigins`, `chrome-extension://*` (KBT Craper), và trong `Development` mọi origin `localhost` / `127.0.0.1`. Port bận → Vite **fail** (không nhảy 5175+) để CORS/bookmark không lệch.

Nếu browser báo CORS trên `/auth/login` mà `4seller-api.kbt.global` trả **404** (không phải 400 validation): API chưa được bind/deploy — xem mục Hosting trong `architecture.md`.

## Health

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/health` | No | Liveness + **pulse** auto order sync khi đến hạn. Aliases: `/healthy`, `/api/v1/health`, `/api/v1/healthy`. IIS preload / Task Scheduler keep-alive. Luôn `ScheduleIfDue()` **đơn mới** trước; chỉ pulse **trạng thái** khi luồng đơn mới không đang chạy / không overdue / đã từng finish (tránh status chiếm `FourSellerSyncGate` ngay sau recycle). Không await sync trên request. Response `orderSync.newOrders` / `orderSync.status` mỗi nhánh có `isRunning` / `nextDueVietnam`. Admin Settings poll endpoint này để hiện runtime trên 2 card sync. App Pool AlwaysRunning + Idle Time-out = 0 — xem `architecture.md` § Hosting. |

Response:

```json
{
  "status": "ok",
  "vietnamTime": "2026-07-21T21:15:51.1020809+07:00",
  "timezone": "Asia/Ho_Chi_Minh",
  "orderSync": {
    "newOrders": {
      "enabled": true,
      "intervalMinutes": 5,
      "lastStartedVietnam": "2026-07-21T21:11:49.059208+07:00",
      "lastFinishedVietnam": "2026-07-21T21:11:51.6088764+07:00",
      "lastSummary": "sellers=4, inserted=0, updated=0, failed=0",
      "isRunning": false,
      "nextDueVietnam": "2026-07-21T21:16:51.6088764+07:00",
      "secondsOverdue": null
    },
    "status": {
      "enabled": true,
      "intervalMinutes": 720,
      "lastStartedVietnam": "2026-07-21T09:00:00+07:00",
      "lastFinishedVietnam": "2026-07-21T09:12:30+07:00",
      "lastSummary": "sellers=4, inserted=0, updated=12, failed=0, durationMin=12.5",
      "isRunning": false,
      "nextDueVietnam": "2026-07-21T21:12:30+07:00",
      "secondsOverdue": null
    }
  }
}
```

Các timestamp của riêng endpoint health được hiển thị theo `Asia/Ho_Chi_Minh` với offset `+07:00` để dễ kiểm tra vận hành. Mỗi nhánh: `enabled` / `intervalMinutes` luôn đọc từ Settings (`status.intervalMinutes` = `status.interval_hours × 60`); `last*` sau mỗi sync xong; `isRunning` chỉ `true` khi đang gọi 4Seller; `nextDueVietnam` = `lastFinishedVietnam + interval`; `secondsOverdue` > 0 khi đã quá hạn mà không đang sync (cảnh báo keep-alive/BackgroundService dừng, sync lỗi, hoặc đang bị `FourSellerSyncGate` chặn bởi luồng kia — `lastSummary` có thể bắt đầu bằng `ERROR:`). Nếu `isRunning` true lâu mà `lastFinishedVietnam` không đổi → scheduler kẹt (recycle App Pool / stale-gate recovery). Admin `/settings` hiện cùng snapshot dưới mỗi card sync (poll 30s).

Login trả `{"message":"Đã xảy ra lỗi hệ thống."}` (HTTP 500): thường `Jwt:Secret` trống trên Production, hoặc DB không kết nối được — xem log `Unhandled exception` / `Jwt:Secret` trong `architecture.md`.

## Response Format

```json
{
  "success": true,
  "message": "",
  "data": {},
  "errors": []
}
```

## Auth Endpoints

> **Tổ (team scope):** JWT có claim `owner` = `ApplicationUser.TeamOwnerId ?? Id`. Mọi controller seller-scoped (`/shops`, `/orders`, `/listings`, `/fourseller`, cookie 4Seller…) lọc theo `User.GetOwnerId()` (claim `owner`), còn `change-password`/`theme` dùng `User.GetUserId()` (claim `uid`). Thành viên tổ đăng nhập bằng tài khoản riêng nhưng thấy đúng dữ liệu của tài khoản tổ; `/auth/me` trả `teamOwnerUserName` và trạng thái cookie 4Seller của tài khoản tổ. Đổi tổ/khoá user → security stamp đổi → token cũ bị thu hồi.


| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/auth/login` | No | Đăng nhập (chặn `IsActive=false`; sai mật khẩu 5 lần → khóa 15 phút, 401 riêng «tạm khóa») |
| POST | `/auth/login-pin` | No | Đăng nhập nhanh extension KBI Fulfil bằng **mã PIN 4 số** (duy nhất toàn hệ thống). Chỉ user active có role `Fulfil` hoặc `Admin`. Sai PIN 5 lần / IP → khóa 15 phút. Body: `{ "pin": "1234", "persistent"?: true }`. Response giống `/auth/login`. |
| POST | `/auth/logout` | Yes | Đăng xuất (revoke refresh token của session hiện tại) |
| POST | `/auth/refresh` | No | Làm mới token (chặn user inactive; revoke refresh nếu inactive) |
| GET | `/auth/me` | Yes | Thông tin user hiện tại (`theme`, `hasFourSellerCookie`, `fourSellerCookieLoginStatus`, không trả cookie thô). User inactive → 401 |
| PUT | `/auth/change-password` | Yes | Đổi mật khẩu → cập nhật security stamp + **revoke toàn bộ** refresh tokens (mọi session) |
| PUT | `/auth/theme` | Yes | Cập nhật preference giao diện UI (`light` / `dark`) — lưu `AspNetUsers.Theme`, đồng bộ đa thiết bị |
| PUT | `/auth/four-seller-cookie` | Yes | Lưu / xóa cookie 4Seller của user đang đăng nhập (write-only) |
| POST | `/auth/four-seller-cookie/check-login` | Yes | Kiểm tra session 4Seller qua `GET /api/user/is-login`; hết hạn thật → `fourSellerCookieLoginStatus=expired` (giữ cookie); **567 → `blocked:true`**, **mạng/proxy → `unverified:true`** — giữ nguyên status. Dùng bởi seller-web profile + Browser Extension (cookie **không** trả về client) |

**Thu hồi token:** Access JWT gắn claim `ss` (Identity `SecurityStamp`). Mỗi request xác thực kiểm tra `IsActive` + khớp `ss`. Đổi/reset mật khẩu hoặc vô hiệu hóa tài khoản (`PUT /admin/users/{id}` `isActive=false`) → đổi security stamp + revoke tất cả refresh tokens → access/refresh cũ không dùng được ngay (401 → client clear session). `DELETE /admin/users/{id}` xóa hẳn user (revoke token trước khi xóa). Refresh token TTL: `Jwt:RefreshTokenExpirationDays` = **30** (web mặc định); Browser Extension gửi `persistent: true` → `Jwt:PersistentRefreshTokenExpirationDays` (~100 năm).

Browser Extension **KBT Craper** (`frontend/browser-extension`, brand `KBT CRAPER`, API `https://4seller-api.kbt.global/api/v1`) **không** gọi thẳng 4Seller và **không** nhận cookie thô. Đẩy sản phẩm qua `POST /fourseller/crawl/post-html` (proxy `postHtml-file-shop` trên server): bắt buộc chọn **≥1 shop** (multi-select). Payload `{ url, html, shopIdList:[…], site, siteList, reviewCount?, averageRating?, platform?, stock?, shippingDays?, shippingDayPercent?, storeCode?, storeLogo?, storeTitle?, soldByTips?, shopRank?, storeAttention?, isLocalProduct?, isSupportQuickShip?, sizeGuideJson?, addedFrom?, … }` — HTML zip sang 4Seller; metric NCC (rating/stock/seller/SLA/rank/follow/Local/QuickShip từ SHEIN `get_goods_detail_realtime_data`; **size guide** từ `get_size_guide_info` — bảng inch US (`sizeInfoInch` → `chartRows`, HIP từ `base_code_info_inch`), `measureNote`, `productMeasureGuide`/`bodyMeasureGuide`) chỉ lưu local trên `SupplierProductInfos.SizeGuideJson`. `addedFrom` = `recommended` (tab Search: nút Thêm N hoặc click tên rồi Thêm PDP) / `manual` (PDP không thuộc list Search) — staging `ListingAddOrigins` **sau khi 4Seller OK**, copy write-once sang `Listings.AddedFrom` khi sync nếu listing tạo quanh lúc staging (không gắn listing cũ); list Search / `kbt_recommended_goods_ids` hết hạn **24h** (không forward 4Seller). **Thêm nhanh từ card list Shein** (search / recommend / store panel / browse `#product-list-v2`): extension queue + tối đa **5** PDP tab nền song song → capture HTML + metrics → cùng `post-html` (không endpoint mới; state `kbt_quick_add_jobs`); **thêm OK giữ tab** để upload Size Chart nếu muốn — **đóng tab thì thôi** (`kbt_size_chart_pending.reopenOnClose=false`). FAB / side panel trên PDP user đang xem: banner `#kbt-size-chart-warn`; đóng tab khi chưa Size Chart → toast + **mở lại tab**. **Size Chart ảnh (thủ công):** trên PDP SHEIN extension phóng modal Size Guide lên **900px** (CSS inject — ẩn SizeTools, tab đơn vị bảng size, ProductDesc — user tự chụp/cắt và lưu PNG/JPG); sau **Thêm sản phẩm OK** hiện FAB upload → `POST /files/upload` (Bunny, `seller.files.manage`) → `POST /fourseller/supplier-product/size-chart-image` `{ sourceUrl, publicUrl }` lưu `SupplierProductInfos.SizeChartImageUrl`; listing detail trả **`sizeChartCaptureUrl`** — `ListingEditPage` nút **Dùng ảnh đã chụp** fetch Bunny → `cos-upload` 4Seller (Publish). SLA shop = `dayPercentDesc` / `dayPercents` (không dùng `shippingDayPercentDesc` toàn sàn). Thiếu `storeTitle` thì parse tên từ `soldByTips` (`"Sold by kcruab-kp"` → `kcruab-kp`). Chỉ tài khoản **Seller** (hoặc Admin / quyền `seller.*`) được đăng nhập extension. Auth: `POST /auth/login` / `POST /auth/refresh` gửi `persistent: true` (refresh token dài hạn); phiên JWT lưu `chrome.storage.local` (`kbt_auth`) đến **Đăng xuất** hoặc **gỡ/cài lại** extension; service worker refresh access JWT sớm 5 phút + alarm 10 phút, mutex một lượt refresh; chỉ xóa auth khi `/auth/refresh` trả 401. **Đăng xuất** revoke refresh + xóa storage phiên (kể cả `kbt_wts_cache` / `kbt_shein_search_sort`). Shop + hồ sơ danh mục Search + keyword đã lưu + tab-counts hydrate từ `GET /shops/scraper-options` khi đã JWT — **không** chờ cookie 4Seller; 4Seller live chỉ cần cho Thêm sản phẩm / `post-html`. Selection trống → auto chọn shop đầu; API sources lỗi không ghi đè cache `[]`. **Tìm sản phẩm tự động:** tab **Search** cạnh Draft/Publish; đúng 1 shop → `GET /product-opportunities/saved-keywords` (combobox chọn đúng 1 keyword — không khóa khi list rỗng/lỗi; copy URL `pdsearch` theo catalog shop) + (khi có category) `PUT /shops/{id}/catalog-profile` → warmup **đúng 1 lần** `us.shein.com/pdsearch` kiểu **EditSearch/`insideFilter`** (HAR: `st=2`, `PageSearchResult`, giá/`attr_ids`/QuickShip, **không** `sort=7`/`cloudTag` trên HTML lẫn BFF; `chrome.tabs.update` giống dán URL; mỗi Tìm kiếm xóa localStorage/IDB Shein (content rồi browsingData) + **hard-reload** pdsearch sau khi URL commit để SPA bắt header anti-bot mới, cookie từ tab, không replay token cũ) **chỉ lấy header** `get_products_by_keywords` (chờ tab `complete` rồi mới fetch BFF; không paginate URL, không dùng JSON hook SPA). Mọi trang/keyword sau đó content script **tự fetch POST** BFF overlay `page`/`keywords` (`limit=120`, **tối thiểu 4 trang**/keyword ≈ 480 SP rồi mới lọc; nếu server cap ~30/trang thì lấy thêm tới ~300, trần 12 trang; BFF **POST** đúng method SPA, không replay GET, **không** `filter`; **401/403 abort ngay**, không giữ `searching`). Lọc client rating/sold/variants + **cổng tiêu đề** (độ phủ: tối đa 2 từ nhu cầu + loại SP, ≥ `ceil(N/2)` trên tập đã cắt, không AND-all; đồng nghĩa `denim`=`jean`; dưới ngưỡng → loại; điểm title = trọng số cụm khớp). Điểm tuyệt đối 0–100 = khớp tiêu đề 40% + Wilson 35% + sao 15% + giá 10% (không percentile) hiện trên list (5 sao + giá bán giống Draft) rồi cắt `limit` (mặc định **30**/keyword, max 50); batch mở PDP (URL `{slug}-p-{id}.html` + query search-list `mallCode`/`pageListType=4`/`detailBusinessFrom=0-2`/`imgRatio` + `src_module=search`) chỉ khi URL khớp `goodsId` → cùng `post-html` với `addedFrom=recommended`. Click tên SP trên list mở PDP tab mới (`OPEN_URL_NEW_TAB` / `chrome.tabs.create`) và ghi `kbt_recommended_goods_ids` — Thêm từ PDP đó vẫn `recommended`. Keyword và batch **Thêm sản phẩm (N)** vẫn `OPEN_URL_ACTIVE_TAB`. «Hôm nay bán gì?» vẫn URL organic `historyWord` (không gắn band giá). Build: `npm run build` → `https://4seller-api.kbt.global/api/v1`; `npm run build:local` / `npm run dev` → `http://localhost:5000/api/v1` (`.env.development`).

Browser Extension **KBI Fulfil** (`frontend/fulfil-extension`) — chỉ role **`Fulfil`** hoặc **`Admin`**. Auth: side panel nhập **mã PIN 4 số** → `POST /auth/login-pin` (không còn username/password); PIN do admin đặt trên `/users` (write-only, `AspNetUsers.FulfilPinHash` = HMAC-SHA256 + `Jwt:PinPepper`, unique). Sau login gọi `GET /auth/me` và chặn role khác. Bản clone UI **KBT Fulfil** (`frontend/kbt-fulfil-extension`) login `POST …/auth/api-token` + hàng đợi `GET …/agent/buy-feed?claimState=free|all` (tab Đơn của tôi / Tất cả đơn; mỗi đơn map `suggestion` → UI coupon: `profile` / `couponCode` / `discountText` / `saving` / `du`) + `PUT …/agent/mark-purchased` — **không có chat / issue-tags / SignalR**. KBI Fulfil vẫn: mọi API kèm header **`X-Fulfil-Session`** (UUID bền `kbt_fulfil_session_id` trong `chrome.storage.local` / profile). Click line item → `POST /admin/orders/{id}/fulfil-claim` (kèm sibling cùng phone+zip) **trước** navigate; badge realtime `orderClaimChanged` (held bởi phiên này / phiên khác của mình / người khác + countdown); renew ~5 phút; nút **Nhả đơn** (clear `activeMatch` + `activeMatchDismissed` — ẩn «Sản phẩm đang mua», không auto-chọn lại theo PDP); **LOGOUT** → `DELETE …/fulfil-claims/session` (nhả mọi claim phiên, không phụ thuộc cache); hub connect kèm `?fulfilSession=` — mất kết nối > **90s** (xóa extension / đóng panel / crash) server tự nhả phiên; Đã mua bị 409 → confirm `force`; LOGOUT chỉ nhả claim phiên hiện tại. Gọi `GET /admin/orders?needsPurchase=true&productSourceId=&platformOrderStatus=AWAITING_SHIPMENT&pageSize=200&sortDir=asc` (tab Đơn cần mua — extension phân trang đến hết) + `GET /admin/orders?purchasedToday=true&productSourceId=&pageSize=50&sortDir=desc` (tab Đơn đã mua — FulfilPurchasedAt trong ngày VN; **Fulfil chỉ thấy đơn mình** (`paidByUserId` bị server ép), **Admin thấy tất cả** + hiện `paidByUserName` trên card; card hiện `fulfilAccountName` cạnh mã đơn + nút **Chưa mua** để gỡ stamp khi bấm nhầm) + `PUT /admin/orders/{id}/milestones` (`fulfilPurchasedAt` hoặc `clearFulfilPurchasedAt: true`) + **issue tags** (`GET/POST /admin/issue-tags`, `PUT /admin/orders/{id}/issue-tags`, `PUT /admin/orders/{id}/issue-status` — gắn tag lỗi SP + trạng thái Đang xử lý/Đã xong **không bắt buộc đã gắn tag**; legacy `PUT …/message` vẫn map sang tag) + **chat nhanh đơn** (`GET/POST /admin/orders/{id}/messages`, `GET /notifications/conversations`, `POST /notifications/conversations/{orderId}/read`) — panel `btn-icon-view` / Chat: thread bubble + gửi Chat (seller nhận `UserNotification(OrderChat)` + SignalR trên seller-web) + khối gắn tag; lệnh chat đi qua **service worker** (`LIST_ORDER_MESSAGES` / `POST_ORDER_MESSAGE` / issue-tags) — sau `npm run build:local` phải Reload unpacked extension nếu SW cũ trả `Unknown message.`; badge số chưa đọc góc icon (map từ conversations); khi side panel mở kết nối hub `/hubs/notifications` (`notificationCreated` + `orderClaimChanged`) + poll conversations ~45s; `GET /product-sources/options` để nhận diện nguồn. Click line item ghim `kbt_fulfil_active_match` (`userPinnedAt`) — panel **Sản phẩm đang mua** giữ đúng `orderId`/`itemId` đã chọn (không bị rematch URL lấy nhầm đơn trùng goodsId trong cache). Hàng đợi **toàn bộ đơn cũ → mới** (phân trang `pageSize=200`), chỉ **AWAITING_SHIPMENT** chưa có `fulfilPurchasedAt` (tương đương tab Chờ xử lý trừ `ON_HOLD`; không lọc `isCustomerPaid`) — extension lọc thêm phía client/cache nên đơn đã đánh dấu **Đã mua** không bao giờ hiện lại list; mỗi card hàng đợi hiện badge **Chưa mua** / **Đã mua** theo `fulfilPurchasedAt` (Chưa mua tô màu cảnh báo theo tuổi chờ: mỗi **12 giờ** tăng 1 bậc, tối đa ≥48h); ô tìm (≥2 ký tự) gọi cùng `GET /admin/orders` với `search` — **không** `needsPurchase` và không khóa `AWAITING_SHIPMENT` (lookup toàn cục mọi trạng thái và nguồn — không gửi `productSourceId`); thêm bộ lọc tuổi đơn gọn (**Tất cả / 1 / 2 / 4 / 6 / 8 / 10 giờ**, mặc định **2 giờ**) cạnh **Tải lại**. Nút **Tải lại** (`btn-ghost`) tải lại hàng đợi; sau mỗi **Đã mua** ẩn ngay đơn đó rồi tự gọi lại cùng API để làm mới toàn bộ hàng đợi. Panel **Sản phẩm đang mua** hiện **Thực nhận** / Rev (`sellerNetAmount` → `totalPaidAmount` ≠ 0 → `amount`); trên trang SHEIN **`/checkout`** đọc DOM tổng thanh toán (`.checkout-price-detail__total-right`) → hiện **Giá trả** + **Profit** (= Rev − giá trả); meta đơn tách khỏi khối địa chỉ; có nút **Chat** (badge unread); mỗi OrderCard có icon message + badge unread. Nút **Sync Order** / **Auto Sync** (card `source-bar`) chỉ **active** khi capture sẵn sàng: 4 header bảo mật + `accountEmail` + `accountId` SHEIN; sau login Kinhbac background tự mở `…/user/orders/list?from=navTop` để warm-up; thiếu → CTA **Chuẩn bị session**. Icon **view**: panel checklist tài khoản (email/id) + header bắt buộc + cookie `shein.com` dạng chuỗi liền Cookie header (đọc `chrome.cookies`, click để copy). Capture: MAIN `capture.js` + isolated `sheinBridge.js` + **`chrome.webRequest`**. **Replay chỉ dùng URL API** (`/bff-api/order/list`). Sync thủ công lấy hết `order/list` (không cắt lookback; Auto deep vẫn dùng `refreshLookbackDays`). Sync **2 pha** list-first — **Auto Sync** thường lấy ~20 đơn mới; cứ 10 chu kỳ Auto (hoặc bấm Sync Order) kéo full lookback theo `orderedAt` (trần 40 trang) → detail chỉ `needsDetailBillnos` (đơn mới / thiếu cost / total đổi / thiếu carrier / thiếu milestone); phase detail chạy song song theo `concurrentThreads` từ config, **chỉ retry lỗi mạng/timeout tối đa 2 lần** (backoff+jitter; `sparse` là kết quả cuối nên không retry), stagger 80ms lúc khởi động worker, adaptive hạ concurrency khi tỉ lệ **lỗi mạng** ≥40% và nâng lại sau 5 lần thành công liên tiếp, timeout 15s/request (order/list 25s). Detail chia lô **25 billno**/lần gọi content script (giữ service worker MV3 khỏi bị suspend) với trần **5 phút**/lượt sync. Billno chưa đủ breakdown persist ở `kbt_fulfil_shein_incomplete_billnos` kèm `attempts`/`firstSeenAt` — ưu tiên lần sync sau nhưng rời hàng đợi sau **10 lần thử** / **TTL 7 ngày** / cap **100**, nếu không đơn không bao giờ có detail (free, đã huỷ, billno chết) sẽ nằm mãi và làm mỗi lượt sync chậm dần. Gate POST detail: chỉ billno **rich** (hoặc sparse có tracking/milestone) — không coi fail là thành công. Parser giá (`sheinOrders.ts`): Giá nhập = Subtotal literal; coupon không dùng `totalDiscountNew`; club reconcile identity (tránh gộp ship FREE). Auto-sync theo `GET /admin/orders/shein-sync/config`. Storage prefix `kbt_fulfil_*`, phiên tối đa 30 ngày. Side panel đồng bộ auth với storage: mất `kbt_fulfil_auth` / Sync báo hết phiên → về form login PIN; login xóa status sync cũ (`Chưa đăng nhập.`).

Browser Extension **KBT Seller** (`frontend/tiktok-shop-extension`, brand `KBT SELLER`, API `https://4seller-api.kbt.global/api/v1`) — chỉ **Seller** / `Admin` / `seller.*`. Auth: `POST /auth/login` / `POST /auth/refresh` gửi `persistent: true` (refresh token ~100 năm — phiên giữ trong `kbt_tts_auth` đến khi **Đăng xuất** hoặc **gỡ/cài lại** extension; service worker refresh access JWT sớm 5 phút + alarm 10 phút, mutex một lượt refresh để tránh rotate song song; chỉ xóa auth khi `/auth/refresh` trả 401, không logout khi lỗi mạng). Còn: `GET /auth/me`, `POST /auth/logout`, `PUT /auth/theme`. Storage `kbt_tts_auth` / `kbt_tts_theme` / `kbt_tts_shop_info` / `kbt_tts_capture` / `kbt_tts_unified_sync` / `kbt_tts_daily_tasks` / `kbt_tts_task_navigation` / `kbt_tts_seller_us_tab` / `kbt_tts_task_dwells` / `kbt_tts_auto_daily_tasks` / `kbt_tts_online_session` / `kbt_tts_online_today`. **Online session:** khi có tab `seller-us.tiktok.com` + `sellerId` trong `kbt_tts_shop_info`, SW alarm 1 phút + tab/storage events ghi `startedAt`/`lastSeenAt` local rồi `POST /shops/tiktok-online/heartbeat` (AllowAnonymous, chỉ `externalShopId`); đóng tab → `POST …/close` (`TabClosed`) và cộng giây vào `kbt_tts_online_today` (ngày VN); Chrome kill → `onStartup`/`onInstalled` reconcile bằng `lastSeenAt` local (`BrowserRestartReconcile`). Side panel badge **Online hôm nay** (cạnh badge seller-us): `closedSeconds` + phiên mở (tick 1s khi live), format `Xh Ym` / live kèm giây — không poll `GET /shops`. Content script trên `seller-us.tiktok.com` (`credentials:include`, không nhồi Cookie/X-Bogus): `GET /api/v3/seller/common/get` + passport/onboard/preload/pay onboard/settlement/statement stat + fail-soft products/list (identity lock) + payout list (+ statement detail/orders) + product_categories/Shipping/Listing limit. Riêng `/api/v1/product/*` đi qua **bridge MAIN world** (`public/sellerProductBridge.js`) để trang tự ký request — xem § Listing limit. Side panel: **Sync fast** (Finance + Health Center + Shop Score, không cần mở khóa nhiệm vụ) và **Sync all** — orchestrator ngầm trong service worker (tuần tự profile → payout → keywords → analytics → health center → shop score → **tải nhiệm vụ hệ thống**, cùng `runId`; Sync all yêu cầu hoàn thành ≥50% nhiệm vụ hôm nay); checklist 7 bước (Sync fast chỉ hiện 3 bước); **không** auto-sync. Bước nhiệm vụ **chỉ đọc**: `GET /shop-tasks/extension/daily?externalShopId=` rồi ghi cache `kbt_tts_daily_tasks` — extension không tự mở trang thay người dùng. Observer nhiệm vụ theo đúng tabId (`taskPageObserver` + `taskPageState`): `webNavigation.onBeforeNavigate` (URL đích lúc bắt đầu tải), onCommitted/onCompleted (load thật), onHistoryStateUpdated cho SPA. Content MAIN world `pageIdleHook.js` đếm fetch/XHR của **trang** (isolated không thấy) rồi `SELLER_PAGE_NETWORK` — spinner không tắt vì `complete`/`pushState`/`SELLER_PAGE_READY`. Tắt spinner khi mạng yên (~1s hết XHR trang) hoặc grace 5s nếu không có XHR (fallback 20s). Complete/favicon của document cũ và URL trung gian bị bỏ qua. Khớp `urlMatchPattern` bằng `matchesTaskUrl` (vitest) → đếm `viewDurationSeconds` (`kbt_tts_task_dwells`) rồi `POST /shop-tasks/extension/visit` (hoặc ngay nếu `0`). Đã đứng đúng URL thì không `tabs.update` lại. Menu TikTok hay bấm panel cùng một observer; backend vẫn so khớp lại. Side panel liệt kê nhiệm vụ hôm nay, **bấm một dòng chỉ điều hướng tab seller-us đang mở** (`OPEN_TASK_URL` → `chrome.tabs.update`, không đẻ tab nền); panel tự tải danh sách khi mở nếu cache trống hoặc thuộc seller khác. Cache khác ngày VN bị bỏ qua và xoá khi logout để không khoe kết quả cũ / của tài khoản khác. `REFRESH_SHOP` chỉ scrape local. Mỗi bước gửi `runId?` trên body sync endpoints; backend ghi `TikTokShopSyncLogs`; skip → `POST /shops/tiktok-sync/log`. Lịch sử: `GET /shops/tiktok-sync/logs`. Keywords/Analytics flow giữ nguyên (sync-context, lead/list, Compass product list). Trước scrape: `PING_CONTENT` + inject `content.js` nếu thiếu listener.

### Update Theme Body

Preference giao diện per-user (`AspNetUsers.Theme`). Giá trị: `light` | `dark` (default `light`). admin-web / seller-web đọc từ `GET /auth/me` và ghi qua endpoint này khi đã đăng nhập (optimistic UI + local cache `admin-theme` / `seller-theme`). Cache theme **không** bị xóa khi logout — trang Login vẫn áp dụng và cho phép đổi (chỉ local đến khi login lại rồi sync API).

```json
{
  "theme": "dark"
}
```

Response: `{ "theme": "dark" }`.

### Update 4Seller Cookie Body

Chuỗi rỗng / whitespace = xóa cookie (status → `none`); non-empty = thay thế (status → `unchecked`). Response: `{ "hasFourSellerCookie": true|false, "fourSellerCookieLoginStatus": "none"|"unchecked" }` (không trả giá trị cookie).

```json
{
  "fourSellerCookie": "session-cookie-from-browser"
}
```

### Check 4Seller Login Body

`fourSellerCookie` tùy chọn — nếu có thì kiểm tra draft cookie (chưa lưu); omit/rỗng thì dùng cookie đã lưu trên tài khoản.

```json
{
  "fourSellerCookie": "optional-draft-cookie"
}
```

Response:

```json
{
  "isLoggedIn": true,
  "message": "Cookie 4Seller hợp lệ — đã đăng nhập.",
  "hasFourSellerCookie": true,
  "fourSellerCookieLoginStatus": "logged_in",
  "cookieCleared": false,
  "blocked": false,
  "unverified": false
}
```

`fourSellerCookieLoginStatus`: `none` | `unchecked` | `logged_in` | `expired` (lưu trên `AspNetUsers`). Khi kiểm tra cookie **đã lưu** (không gửi draft):

- `isLoggedIn=true` → persist `logged_in`
- `blocked=true` (EdgeOne HTTP 567) hoặc `unverified=true` (proxy/timeout/HTTP 5xx/429/body không JSON/`code≠0`) → **giữ nguyên** status hiện tại, **không** đánh `expired`
- chỉ khi `isLoggedIn=false` và không blocked/unverified (`code:0` + `data:false`) → persist `expired` (cookie **giữ**)

Outbound 4Seller chỉ dùng cookie khi status = `logged_in`. Kiểm tra draft không đổi status đã lưu. Background `FourSellerCookieRecheckBackgroundService` mỗi 15 phút chỉ **nâng** `expired` → `logged_in` khi is-login OK (không bao giờ set expired).

Seller tự quản lý cookie tại **Hồ sơ cá nhân** (`/profile`). Admin vẫn có thể gán cookie qua [Admin User Endpoints](#admin-user-endpoints).

## Admin Dashboard Endpoints

Tổng quan ops cho admin-web (`/dashboard`; landing FE là `/shop-tasks/daily`) và trang thống kê chuyên sâu (`/stats/orders`, `/stats/revenue`, `/stats/seller-performance`, `/stats/charts`). Permission: `admin.dashboard.view`. Chỉ trả **aggregate** (counts / sums) — không hydrate list Orders / Listings / Shops. Timezone KPI và series ngày: **Asia/Ho_Chi_Minh** (cùng công thức Telegram daily / seller summary). UI dashboard tab **Ngày / Tuần / Tháng này / 30 ngày** (mặc định **30 ngày**) — preset map sang `period` hoặc `fromDate`+`toDate` (`30 ngày` = rolling inclusive today−29 → today, giờ VN). **KPI `orderCount` / `revenue`:** cùng tập — shop `Status=Active` + đã gắn thẻ nhận tiền (`ReceivingBankAccountId`) + exclude `CANCELLED`/`REFUND` (`WhereMoneyEligible`). **`orderStatusCounts` + `listings`:** theo kỳ (cùng cửa sổ period). **`shops`:** snapshot hiện tại. **Heatmap / trends.orders:** mọi shop + mọi trạng thái. **Seller rank / shop finance / trends money:** shop Active + exclude cancel/refund (chưa yêu cầu thẻ nhận tiền).

**Doanh thu (revenue):** tiền TikTok trả seller sau chi phí = `Order.SellerNetAmount` (từ 4Seller `order/detail` `orderAmountVo`, fallback `costDetailVo`/`costVo` khi amount Vo null; Seller ship: `productBefore − productSellerDisc + netShip − tax − other`; TikTok/platform ship: `productBefore − productSellerDisc − tax − other`, **không** ± netShip; `productPlatformDiscount` chỉ display, không trừ); chưa enrich → fallback `TotalPaidAmount` nếu ≠ 0 else `Amount` (`Domain.Orders.OrderRevenue.Resolve`). Áp dụng summary / trends / seller-performance / Telegram. Cost vẫn `SupplierTotal`; `profit` = revenue − cost.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/dashboard/summary` | `admin.dashboard.view` | Snapshot theo `period` + backlog order status + listing/shop counts (chỉ field UI dùng) + top seller + shop finance + top 10 sản phẩm bán chạy trong kỳ. Query `include` (csv) chỉ tính section cần thiết. Query `metrics=orders` → seller/shop ranking chỉ đếm đơn (bỏ join Items/cost, sort theo orders). Bỏ quét product catalog / syncError / connected / listedCount (listed lấy từ `/trends`). Dùng bởi Dashboard, `/stats/orders` (`include=kpis,orderStatus,sellers,shopsFinance` + `metrics=orders`), `/stats/revenue` (`include=kpis,sellers,shopsFinance`); `/stats/charts` (mode Tổng) chỉ `include=sellers` |
| GET | `/admin/dashboard/trends` | `admin.dashboard.view` | Series theo bucket: `orders` (mọi status) / `ordersOk` (exclude cancel/refund) / `ordersCanceled` / `ordersRefund` / `revenue` / `cost` / `profit` / `listed`. `includeListed=false` bỏ quét listing. `metrics=orders` chỉ đếm đơn (bỏ Items/cost). `metrics=revenue` giữ orders+revenue (bỏ Items/cost). Không có `groupBy` → query nhẹ (không join shop). Dùng bởi Dashboard (cột chồng Đơn hàng + Đã hủy + Refund), `/stats/orders` (`includeListed=false` + `metrics=orders`), `/stats/revenue` (`includeListed=false` + `metrics=revenue`), `/stats/charts` (KPI + 3 metric charts; mode Seller/Shop lấy pie từ `series`) |
| GET | `/admin/dashboard/order-heatmap` | `admin.dashboard.view` | Heatmap số đơn theo ngày × seller và/hoặc ngày × shop. Query `dimension=seller\|shop` chỉ tính 1 chiều (Dashboard truyền cả hai khi không set). Shop dim: **chỉ shop Active** (kể cả 0 đơn). Cùng cửa sổ `period`/`days` như trends |
| GET | `/admin/dashboard/order-hourly-heatmap` | `admin.dashboard.view` | Heatmap số đơn theo giờ (0–23), **cộng dồn cả kỳ** (cửa sổ ngày lịch VN). Query `timezone=us\|vn` (mặc định `us` = America/Los_Angeles; `vn` = Asia/Ho_Chi_Minh). Hàng seller + **Tổng**. Cùng `period`/`days`/`fromDate`+`toDate` như trends |
| GET | `/admin/dashboard/seller-performance` | `admin.dashboard.view` | Hiệu suất toàn bộ seller active: **đếm đơn** mọi shop/status; **revenue/cost/profit** chỉ shop Active + exclude `CANCELLED`/`REFUND`. Thêm **đơn/ngày hoạt động** + **đơn/shop-ngày** theo lịch sử `ShopOperationalPeriods`. Không top-N — mọi user role Seller + `IsActive` (kể cả 0 đơn). Attribution = `Order.OwnerId`. Mẫu số vận hành: shop mở khi Active (kéo lùi theo đơn đầu nếu backfill), đóng khi Deactivated/Suspended/đổi owner/xóa. Snapshot shop: `activeShopCount` / `deactivatedShopCount` / `suspendedShopCount` / `shopCount` (tổng). Alias deprecated: `averageOrdersPerDay` / `averageOrdersPerActiveShop*` trỏ sang field chuẩn. Dùng bởi `/stats/seller-performance` và Dashboard (biểu đồ cột chồng **Active / Deactivated / Suspended** theo seller, tooltip **Tất cả**) |

### Query params (summary + heatmap; trends dùng thêm các field dưới)

| Param | Type | Description |
|-------|------|-------------|
| `ownerUserId` | Guid? | Lọc theo seller |
| `shopId` | Guid? | Lọc theo shop local |
| `platform` | string? | Lọc platform (orders: uppercase; listings: lowercase) |
| `period` | string | `day` \| `week` \| `month` (mặc định `day`). Calendar VN: hôm nay / Mon→hôm nay / ngày 1→hôm nay. **Trends:** bỏ qua khi có `fromDate`+`toDate` |
| `days` | int | `/trends`, `/order-heatmap`, `/order-hourly-heatmap` khi `period=day` — lookback (mặc định 7, clamp 1–31). `week`/`month` bỏ qua `days`. **Trends + heatmap:** bỏ qua khi có custom range |
| `fromDate` | string? | Inclusive yyyy-MM-dd (VN). Cùng `toDate` → custom range (ghi đè `period`). Áp dụng cho `/summary`, `/trends`, `/order-heatmap`, `/order-hourly-heatmap`, `/seller-performance` |
| `toDate` | string? | Inclusive yyyy-MM-dd (VN), không vượt quá hôm nay. Summary / seller-performance clamp ≤366 ngày |
| `grain` | string | `/trends` only — `day` \| `week` \| `month` \| `year` (mặc định `day`). Week = ISO Monday. Clamp span: day≤366, week≤730d, month≤1825d, year≤3650d |
| `groupBy` | string? | `/trends` only — `seller` \| `shop`. Khi set, trả thêm `series[]` (top **8** entity theo revenue + `Others`) để FE vẽ multi-series; `days[]` vẫn là tổng aggregate trong scope |
| `dimension` | string? | `/order-heatmap` only — `seller` \| `shop`. Chỉ build matrix đó (nhanh hơn). Bỏ trống = cả hai (legacy) |
| `timezone` | string? | `/order-hourly-heatmap` only — `us` (America/Los_Angeles, mặc định) \| `vn` (Asia/Ho_Chi_Minh). Bucket giờ; cửa sổ ngày vẫn VN |
| `include` | string? | `/summary` only — csv sections: `kpis`, `cardSpend` (deprecated — alias của Cost khi có `kpis`), `orderStatus`, `listings`, `shops`, `sellers`, `shopsFinance`, `topProducts`. Bỏ trống = full snapshot. `/stats/orders` dùng `include=kpis,orderStatus,sellers,shopsFinance`. `/stats/revenue` dùng `include=kpis,sellers,shopsFinance`. `/stats/charts` mode Tổng dùng `include=sellers` (bỏ KPI/listing/shop/topProducts/shopsFinance) |
| `includeListed` | bool? | `/trends` only — mặc định `true`. `false` → bỏ quét listing, `listed=0` mỗi bucket (`/stats/charts`, `/stats/orders`, `/stats/revenue`) |
| `metrics` | string? | `/summary` + `/trends` — `orders` → sellers / shopsFinance / trends chỉ đếm đơn (bỏ join Items + cost/revenue; sort seller/shop theo orders). `revenue` → trends giữ orders+revenue (bỏ join Items/cost; `/stats/revenue`). Không ảnh hưởng section `kpis`. Bỏ trống = full finance. `/stats/orders` luôn truyền `metrics=orders` |

### Summary response (`data`)

- `timezone`, `period`, `fromDate`, `toDate` (inclusive, yyyy-MM-dd)
- `today` — KPI trong kỳ (`date` = `toDate`, `orderCount`, `revenue`, `cost`, `coupon`, `profit`, `profitMargin`, `income`, `cardSpend`, `listedCount`). **`orderCount`** + **`revenue`** cùng tập đơn: shop Active + đã gắn thẻ nhận tiền (`Shop.ReceivingBankAccountId`) + exclude `CANCELLED`/`REFUND`. **`revenue`** = seller net (`SellerNetAmount ?? TotalPaidAmount ?? Amount`). **`cost`** = chi tiêu mua hàng thực tế trong kỳ từ `FulfilTransactions` (Debit − Credit, cùng công thức Lịch sử mua hàng / `/admin/fulfil/transactions/stats`; filter owner/shop/platform qua `Order` khi có). **`coupon`** = Σ `FulfilTransactions.CouponAmount` cùng cửa sổ/filter với `cost` (snapshot giảm giá coupon CostUpdate; Manual null → 0) — **không** trừ vào Cost; khớp cột Coupon trên Lịch sử mua hàng. `profit` = `revenue − cost`. **`profitMargin`** = `profit / cost × 100` (null khi cost = 0) — UI gắn dưới KPI Profit. **`income`** = tổng tiền đã về trong kỳ từ `ReceivingBankTransactions` (**Confirmed only**, Credit − Debit; cùng `/admin/receiving-bank-accounts/transactions/stats`). `cardSpend` deprecated — alias của `cost`. `listedCount` luôn `0` trên summary. Seller rank / shop finance / trends vẫn dùng `SupplierTotal` (NCC) + shop Active (không bắt buộc thẻ nhận tiền) cho cost cấp đơn; margin xếp hạng vẫn `profit/revenue`.
- `orderStatusCounts` — shape workflow tabs (`all` / `pendingProcess` / `purchased` / `inTransit` / `delivered` / `canceled` / `refund`) theo **`OrderWorkflowTabs`**, cộng **`suspended`** = đơn trong kỳ thuộc shop không đủ điều kiện KPI (không Active và/hoặc chưa gắn thẻ nhận tiền) **và đã exclude `CANCELLED`/`REFUND`** (`WhereOrderStatusOk`) — không trùng tile Hủy/Refund. Phân vùng Dashboard: **`all` = `today.orderCount` + `canceled` + `refund` + `suspended`**. Scope: **mọi shop** trong kỳ (`OrderCreateTime ?? CreatedAt` khớp `fromDate`/`toDate`). UI Dashboard + `/stats/orders` hiển thị đủ 6 tab (không gộp hủy+refund); Dashboard thêm tile Suspended.
- **Order count vs money:** KPI `orderCount` + `revenue` (+ top products) = `WhereMoneyEligible` (shop Active + `ReceivingBankAccountId` + exclude cancel/refund). `trends.orders` / heatmap = mọi shop + mọi trạng thái. Seller rank / shop finance / trends revenue·cost = shop Active + exclude cancel/refund (không bắt buộc thẻ nhận tiền).
- `listings` — `draft` / `published` / `active` / `inactive` trong kỳ (cùng clock listed: `PlatformCreateTime ?? PlatformUpdateTime ?? CreatedAt`). `syncErrorCount` / `pending` luôn `0` (không scan thêm)
- `shops` — `total` / `active` / `inactive` / `suspended` từ GroupBy status (**snapshot hiện tại**, không theo kỳ). `connected` luôn `0` (không scan FourSellerShopId)
- `products` — luôn zeros (Dashboard/stats không hiển thị product catalog counts)
- `sellersToday[]` — top 20 trong kỳ: `userId`, `displayName`, `listed`, `orders` (mọi shop/status), `revenue`/`cost`/`profit`/`profitMargin` (chỉ shop Active + exclude cancel/refund; Cost = cùng công thức `totalCost` như KPI; `profit` = revenue − cost; `profitMargin` = profit/revenue×100, null khi revenue = 0; sort Revenue DESC)
- `shopsFinance[]` — top **12** shop trong kỳ (kể cả khóa/Unassigned): `orders` đếm đủ; `revenue`/`cost`/`profit` chỉ khi shop Active + không cancel/refund (shop khóa → tiền 0); sort Revenue DESC
- `topProducts[]` — top **10** sản phẩm bán chạy trong kỳ (line items đơn không canceled). **Gộp variant:** cùng `ListingVariation.ListingId` hoặc cùng prefix MSKU 4Seller (`472773277-I2…` / `472773277-I0…` → product `472773277`) tính **1 sản phẩm**, cộng dồn qty/revenue/đơn. Fields: `productKey`, `sellerSku` (prefix hoặc SKU đại diện), `title`, `imgUrl`, `sellerName`, `shopName`, `shopProfileName` (seller/shop bán nhiều nhất), `quantitySold`, `orderCount`, `revenue`; sort QuantitySold DESC rồi Revenue DESC

### Seller performance response (`data`)

- `timezone`, `period`, `fromDate`, `toDate` (inclusive, yyyy-MM-dd VN)
- `totals` — `dayCount`, `sellerOperationalDays`, `activeSellerCount`, `sellersWithOrders`, `activeShopCount`, `deactivatedShopCount`, `suspendedShopCount`, `shopCount`, `operationalShopCount`, `shopDays`, `eligibleShopOrderCount`, `unassignedOrderCount`, `unassignedOrderRate`, `orderCount`, `revenue`, `cost`, `profit`, `profitMargin`, `averageRevenuePerOrder`, `averageProfitPerOrder`, **`averageOrdersPerCalendarDay`**, **`averageOrdersPerOperationalDay`**, **`averageOrdersPerOperationalShop`**, **`averageOrdersPerShopDay`**
  - Averages tài chính = **weighted** theo tổng đơn (`Σprofit / Σorders`); null khi `orderCount = 0`
  - `dayCount` = số ngày lịch inclusive VN; `averageOrdersPerCalendarDay = orderCount / dayCount` (quy mô tuyệt đối)
  - `sellerOperationalDays` = Σ (theo seller) số ngày VN unique có ≥1 shop vận hành; `averageOrdersPerOperationalDay = orderCount / sellerOperationalDays` (**chỉ số seller/ngày chính**)
  - `operationalShopCount` = số shop có ≥1 ngày vận hành giao kỳ; `averageOrdersPerOperationalShop = eligible / operationalShopCount` (**tổng đơn/shop trong kỳ**, phụ thuộc độ dài kỳ)
  - `shopDays` = Σ ngày vận hành mỗi shop (interval clamp + merge); nguồn `ShopOperationalPeriods` (mở khi Active / backfill từ ngày đơn đầu; đóng khi Deactivated·Suspended·OwnerChanged·Deleted). `averageOrdersPerShopDay = eligibleShopOrderCount / shopDays` (**chỉ số shop chính**)
  - `eligibleShopOrderCount` = đơn kỳ có `ShopId`; `unassignedOrderCount` / `unassignedOrderRate` = đơn thiếu ShopId (không vào phép chia shop)
  - `activeShopCount` / `deactivatedShopCount` / `suspendedShopCount` = snapshot theo `Shop.Status` hiện tại (informational; **không** làm mẫu số)
  - `shopCount` = tổng shop hiện tại mọi status (Active + Deactivated + Suspended + …) trong scope
  - Alias deprecated (cùng giá trị): `averageOrdersPerDay` → calendar; `averageOrdersPerActiveShop` → operational shop; `averageOrdersPerActiveShopPerDay` → shop-day; `participatingShopCount` → `operationalShopCount`
  - Totals intensity = `Σ numerator / Σ denominator` (không mean-of-averages)
  - Clock đơn: `OrderCreateTime ?? CreatedAt`; exclude canceled; filter owner/shop/platform áp dụng cho đơn và period (platform lọc shop qua `Platform.Code`)
- `sellers[]` — mọi Seller active: cùng field như totals + `userId` / `displayName` (gồm `activeShopCount` / `deactivatedShopCount` / `suspendedShopCount` / `shopCount`); sort Profit DESC rồi Revenue DESC
- `days[]` — series ngày cho toàn bộ scope; khi truyền `ownerUserId` thì chỉ một seller: `date`, `orders`, `eligibleShopOrders`, `operationalShopCount`, `ordersPerDay`, `ordersPerShop`. `ordersPerDay` là số đơn ngày VN; `ordersPerShop = eligibleShopOrders / operationalShopCount` đúng ngày đó (null khi không có shop vận hành). UI có lựa chọn **Tất cả** hoặc từng seller.

### Trends response (`data`)

```json
{
  "timezone": "Asia/Ho_Chi_Minh",
  "grain": "day",
  "fromDate": "2026-06-18",
  "toDate": "2026-07-18",
  "groupBy": "seller",
  "days": [
    { "date": "2026-07-12", "orders": 0, "ordersOk": 0, "ordersCanceled": 0, "ordersRefund": 0, "revenue": 0, "cost": 0, "profit": 0, "listed": 0 }
  ],
  "series": [
    {
      "date": "2026-07-12",
      "entityId": "...",
      "entityName": "Seller A",
      "orders": 2,
      "revenue": 100,
      "cost": 40,
      "profit": 60
    }
  ]
}
```

`date` = khóa bucket (ngày / Monday tuần / ngày 1 tháng / 1 Jan năm). `orders` = mọi trạng thái; `ordersOk` = exclude `CANCELLED`/`REFUND` (mọi shop; không yêu cầu Active); `ordersCanceled` = `CANCELLED`/`CANCELED` (+ legacy canceled); `ordersRefund` = `REFUND`/`REFUNDED`. `cost`/`profit` cùng công thức KPI summary. `series` rỗng khi không có `groupBy`. Admin UI Dashboard chart **Đơn hàng**: cột chồng **Đơn hàng** (xanh) + **Đã hủy** (đỏ) + **Refund** (hồng) — chiều cao = tất cả. `/stats/charts`: chế độ **Tổng / Theo Seller / Theo Shop** + lọc Seller/Shop; pie tỷ trọng: mode **Tổng** từ `summary?include=sellers`; mode **Seller/Shop** cộng dồn từ `trends.series` (không gọi summary). Trends luôn `includeListed=false`.
### Order heatmap response (`data`)

- `timezone`, `period`, `fromDate`, `toDate`
- `sellers[]` / `shops[]` — cells `{ date, entityId, entityName, orders, groupId?, groupName?, publishLimitPublished?, publishLimitTotal? }` (**đếm đơn** mọi trạng thái). Seller: top **12** + `Others`. Shop: **chỉ shop Active** trong scope (kể cả 0 đơn trong kỳ; bỏ Unassigned / Deactivated / Suspended); gom theo seller (`groupId`/`groupName`); **thứ tự trong nhóm = "Theo tên Profile"** (prefix + số cuối, giống `/shops` sort `profile` asc — `KBI_SHOP_9` trước `KBI_SHOP_10`); kèm `publishLimitPublished`/`publishLimitTotal` từ TTS sync (UI heatmap hiển thị `published/limit` cạnh tên shop, giống cột Listing `/shops`; null với seller cells / Others). Matrix đủ mọi ngày trong cửa sổ (orders = 0 nếu không có đơn). Khi `dimension=seller` thì `shops=[]` (và ngược lại); không truyền `dimension` → cả hai như cũ. UI Dashboard: hàng entity + hàng **Tổng** (Σ theo ngày từ các ô đang hiện). Card shop: combobox Seller lọc theo `groupId` (client-side; xóa chọn = tất cả); FE `OrderHeatmapGrid` `sortByProfileName` khớp cùng thuật toán.

### Order hourly heatmap response (`data`)

- `timezone` = IANA đã resolve (`America/Los_Angeles` hoặc `Asia/Ho_Chi_Minh`) theo query `timezone=us|vn`. `fromDate`/`toDate` vẫn lịch nghiệp vụ VN
- `period`, `fromDate`, `toDate`
- `sellers[]` — cells `{ entityId, entityName, hour, orders }` cho top **12** seller + `Others` × giờ 0–23 (cộng dồn cả kỳ; zeros filled)
- `totals[]` — 24 cells `{ entityId: "__all__", entityName: "Tổng", hour, orders }` = Σ đơn theo giờ trên toàn kỳ. UI seller-web Dashboard: **Orders theo ngày · Shop** full-width, rồi **Orders theo giờ** full-width (Segmented US|VN); không còn bố cục 2 cột / cột chart. Admin Dashboard không hiện heatmap giờ.
- Clock: `OrderCreateTime ?? CreatedAt` → giờ local theo `timezone`; đếm đơn mọi shop/status

## Admin Order Endpoints

Đơn hàng sync từ 4Seller `POST /api/v2/order/page` vào local `Orders` / `OrderItems`. Sau mỗi trang upsert, đơn chưa có breakdown TikTok thật (`PricingDetailSyncedAt` null, hoặc đơn đã paid mà mọi cột tiền = 0) gọi thêm `GET /api/v2/order/detail` qua `GetOrderDetailsAsync` (cookie 1 lần + concurrency 2; tối đa 40 đơn/trang, ưu tiên mới nhất; detail toàn 0 **không stamp** — retry; cuối seller quét thêm tối đa 40 đơn paid-zero ngoài lookback; lỗi → retry sync sau) để lưu breakdown TikTok (`orderAmountVo`, fallback `costDetailVo`/`costVo`) và tính `SellerNetAmount`. List `amount`/`itemPrice` = 0 không đè số khác 0 đã có. **Rev hệ thống** = `SellerNetAmount` (fallback `TotalPaidAmount` nếu ≠ 0 else `Amount` khi chưa enrich). Cookie lấy từ `ApplicationUser.FourSellerCookie` của từng seller (không dùng cookie admin). Upsert theo `fourSellerOrderId` **của đúng OwnerId** (unique) **và** theo business key `(ownerId, platformOrderId)` khi mã đơn nền tảng không rỗng — cùng mã đơn không tạo row thứ hai nếu 4Seller đổi `orderId`; id 4Seller thuộc seller khác thì skip. Unique DB: `FourSellerOrderId` + filtered unique `(OwnerId, PlatformOrderId)`. `SaveChanges` gặp `DbUpdateConcurrencyException` → tối đa 3 lần: `OrderItem` đang ghi mà row đã mất thì INSERT lại; entity khác (kể cả `Order` merge-xóa) thì detach; không fail cả seller. `orderItemInfoList` rỗng/omit **không** xóa line item local (trang list 4Seller đôi khi thiếu items). Sync thủ công và job nền dùng chung `SyncGate` (một sync tại một thời điểm; nếu đang chạy → skip + message). **Multi-account song song:** khi `ownerUserId` null, mọi user `logged_in` chạy **đồng thời** (`Task.WhenAll`); mỗi seller một DI scope riêng (`DbContext` + `FourSellerApiService` + sticky proxy). Trong từng seller vẫn tuần tự status → page → upsert (+ enrich detail). Lỗi một seller → `failedSellers[]`, không chặn seller khác. Match shop local qua `Shop.FourSellerShopId` (= 4Seller **internal** `record.Id`, không phải `sellingPartnerId` — partner id nằm ở `ExternalShopId`); fallback unique `shopName` trong shops của seller. Khi sync đơn, nếu không resolve được shop thì **giữ** `Order.ShopId` cũ (không clear). Sau Đồng bộ shop (`/fourseller/shops/sync`) backend gọi `RematchOrderShopLinksAsync` để gắn lại `Order.ShopId` theo `Order.FourSellerShopId` → list đơn hiện `shopName` / `shopProfileName` (`Shop.Name` / `Shop.ProfileName`; `ProfileName` là field local, không lấy từ 4Seller).

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/orders` | `admin.orders.view` | List đơn local (filter status, seller, shop, search, date range, `needsPurchase`, `purchasedToday`, **`paidByUserId`**, `isCustomerPaid`, `productSourceId`; paging số trang hoặc `cursor` keyset; gồm `shopName`, `shopProfileName`, `totalPaidAmount`, `sellerNetAmount`, breakdown TikTok khi đã enrich, phí local `cost`/`shippingFee`/`tax`/`ads`/`totalCost`, `isSettlementPaid`, `isCustomerPaid` (khách TT trên sàn — từ `orderPaidTime`), `orderStatus` (bucket 4Seller remote), mốc fulfil `fulfilPurchasedAt` / `trackingAddedAt` / `inTransitAt` / `deliveredAt` / `refundedAt` / `canceledAt`, **`uspsProcessedAt`** (manual USPS), **`buyTrackAmount`** / **`thankCardProcessedAt`** / **`thankCardAmount`** (phí xử lý đơn), **`refundProcessedAt`** (ops xử lý Refund), `paidByUserId` / `paidByUserName`, địa chỉ khách `buyerName` / `buyerPhone` / `buyerEmail` / `address` / `city` / `state` / `postalCode` / `countryCode`, **lease claim** khi còn hiệu lực: `fulfilClaimedByUserId` / `fulfilClaimedByName` / `fulfilClaimedAt` / `fulfilClaimExpiresAt` / `fulfilClaimSessionCount` (null/0 khi free hoặc hết TTL / đã mua)). **`purchasedToday` + non-Admin:** controller ép `paidByUserId = User.GetUserId()` (Fulfil chỉ thấy đơn mình stamp; Admin thấy tất cả / lọc tùy chọn). **`statusCounts` luôn `null`** — badge đếm chuyển sang `GET /admin/orders/status-counts`. **Perf:** page keys trước (không Include, sort `(orderTimeKey, Id)`) → 3 query projection slim theo id (không `RawPayload` / cookie owner / số thẻ thô); index `IX_Orders_OrderTimeKey_Desc` / `IX_Orders_Owner_OrderTimeKey_Desc` phục vụ cả filter ngày và sort. Trả thêm **`nextCursor`** (keyset, opt-in — xem `cursor`) |
| GET | `/admin/orders/status-counts` | `admin.orders.view` | Badge đếm workflow tab, **cùng filter list** nhưng bỏ `pageCurrent`/`pageSize`/`cursor`/`platformOrderStatus`/`pendingFilter` (counts không phụ thuộc tab đang chọn hay trang) ⇒ FE cache riêng, đổi trang/đổi tab không tính lại. **3 query**: 1× `GROUP BY "WorkflowTab"` cho 6 tab chính + 1× `COUNT(*) FILTER` cho bucket ops + 1× cho sub-bucket chờ mua |
| GET | `/admin/orders/{id}` | `admin.orders.view` | Chi tiết + line items (+ phí cost local + mốc fulfil) |
| GET | `/admin/orders/{id}/preview` | `admin.orders.view` | Hover preview nhẹ: line items (ảnh/tên/SL/`itemPrice`, cap 50 — đủ trọn đơn thực tế, `itemCount` để UI báo phần dư) + breakdown **Mua hàng** (`supplierRetailPrice` / `supplierSubtotal` / ship / **`supplierRetailDeliveryFee`** / on-time / coupon / membership / **`supplierPointsDiscount`** / **`supplierWalletPrice`** / tax / `costAmount`=TrueSpend (thẻ+ví+club)) + **`supplierOrderCount`** & **`supplierLinks[]`** (mọi billno đã khớp, cũ → mới — kể cả `IsCharged=false` để hover không trống khi false-terminal; `costAmount`/`supplierOrderCount` vẫn chỉ cộng dòng tính tiền; mỗi phần tử có `supplierOrderNo` / `supplierOrderUrl` / `supplierStatus` / `isCharged` + **`trackingNo`/`warehouseTrackingNo`/`warehouseTrackingSeenAt`/`carrier`/`trackingKind`** + **breakdown đầy đủ** retail / subtotal / ship / retail delivery / on-time / coupon / membership / points / wallet / tax / total — UI hiện badge `×N` + khối breakdown riêng từng billno khi > 1, kèm tag carrier USPS/FedEx + mã track copyable) + order-level **`trackingNo`** (đã publish) + **Doanh thu** khi có `pricingDetailSyncedAt` (`product*` / ship discount / tax / `platformOtherFee` / `totalPaidAmount` + `sellAmount` Rev) — không buyer/notes/milestones; dùng tooltip mã đơn (vd. `/fulfil/transactions`) |
| GET | `/admin/orders/shipping-services` | `admin.orders.view` | Danh sách carrier 4Seller cho Mark Shipped; đọc cache bảng `PlatformShippingServices` (TTL **24h**, upsert từ `list-platform-shipping-service` khi rỗng/stale). Query `platform` (filter), `refresh=true` (bắt buộc gọi live), `ownerUserId` (cookie seller) |
| POST | `/admin/orders/{id}/mark-ship` | `admin.orders.manage` | Mark Shipped trên 4Seller (chọn carrier + tracking). Trước khi đẩy: **reconcile** nếu link đang `unconfirmed`/`requestId` — sàn đã có cùng mã → chỉ sync local; sàn **xác nhận trống** (kể cả trong grace 30′) hoặc hết grace/`SafeToRetry` → clear `unconfirmed` rồi **POST `to-ship-mark-ship` ngay trong cùng request** (Manual không chờ pool Auto). Chỉ giữ lỗi Unconfirmed khi **không đọc được** đơn 4Seller. **Từ chối** khi `shippingMethod` là TikTok/Platform Shipping (BusinessException — tracking do TikTok xử lý; UI ẩn Đẩy track). Poll async rồi cập nhật local; set `trackingAddedAt` lần đầu nếu chưa có. **Mọi lỗi** (poll timeout / `BusinessException` / lỗi khác sau khi đã load order) đều ghi `autoMarkShipStatus` + `autoMarkShipError` (+ `requestId` khi unconfirmed) lên link carrier khớp **trước** khi API trả 4xx — UI Orders hiện lại message sau refetch. Poll timeout → `MarkShipUnconfirmedException` |
| POST | `/admin/orders/{id}/sync-track` | `admin.orders.manage` | **Sync track** một đơn: gọi `get_order_info` bằng cookie/header TK mua hàng đã lưu → phase `detail` + `updateMode=all` (full-replace breakdown tiền + tracking) → Auto Mark Ship khi đủ điều kiện (**poll interactive ~60s**; reconcile `unconfirmed` cũng poll dài `requestId`). Không body. Trả `{ orderId, fetched, updatedTracking, updatedCost, autoShipped, autoShipFailed, autoShipBlocked, billnos[{ billno, fetchOk, skipReason, error, updatedTracking, trackingNo, trackingKind, carrier, updatedCost, supplierTotal, supplierSubtotal, costIncomplete, autoMarkShipStatus, autoMarkShipError, fulfilAccountName }], notes[], summary }`. Soft-fail per-billno chỉ trên `billnos[]` (không echo trùng vào `notes`); khi Mark Ship không chạy lại trong lượt mà còn `failed`/`blocked`/`unconfirmed` cũ → `notes` giải thích cooldown. HTTP **200** kể cả soft-fail từng billno; **400** nếu đơn chưa có billno SHEIN; **404** nếu không thấy đơn. Khác `mark-ship` (không fetch SHEIN) và khác `shein-detail-sync/run` (bulk). UI: menu **Sync track** trên card đơn `workflowTab=IN_TRANSIT_GROUP` có `supplierLinks`. |
| POST | `/admin/orders/{id}/sync-platform-status` | `admin.orders.manage` | **Sync trạng thái sàn** một đơn: tra 4Seller `POST /api/v2/order/page` theo `searchType=orderNo` exact với **một** truy vấn bucket `all` (không phụ thuộc create-date lookback của bulk sync). Trước đây phải thử lần lượt `shipped` → `to_ship` → `in_process` → `canceled` — tốn tới 4 round-trip và **không phủ** đơn đang `on_hold`. Áp lại commerce qua `ApplyRecord` / `ResolveCommerceStatus` (**latch REFUND** — không set/clear hoàn), seed tracking carrier (bỏ qua mã kho `C…`/`N…`), `ApplyPlatformClocks`, `SyncItems`. Không tìm thấy → `found=false`, **không** sửa local. Trả `{ found, changed, previousPlatformOrderStatus, platformOrderStatus, workflowTab, trackingNo, summary }`. Toast «Đã cập nhật: Chờ lấy → Đang giao» / «Không đổi…» / «4Seller không tìm thấy…». UI menu OrderCard **Sync trạng thái sàn** (khác **Sync track** SHEIN). |
| PUT | `/admin/orders/{id}/cost` | `admin.orders.manage` | Cập nhật phí local `cost` / `shippingFee` / `tax` / `ads` + breakdown NCC `supplierOrderNo` / `supplierRetailPrice` / `supplierSubtotal` / `supplierShippingFee` / `supplierRetailDeliveryFee` / `supplierOnTimeGuarantee` / `supplierCouponPrice` / `supplierMembershipDiscount` / `supplierPointsDiscount` / `supplierWalletPrice` / `supplierTax` / `supplierTotal` + optional `fulfilAccountId` / `fulfilCardId` / `paidByUserId` (**người mua hàng**; null → **giữ** giá trị cũ, chỉ fallback current user khi đơn chưa có người mua) + optional `fulfilPurchasedAt` (UTC; null → giữ cũ hoặc UtcNow lần đầu có account/card); reconcile `FulfilTransaction` CostUpdate khi có (account hoặc card) + totalCost — **thẻ không bắt buộc** (nếu thiếu dùng `ActiveCardId` của tài khoản); không gọi 4Seller.<br>**Phí nhập tay là một dòng `SupplierPurchases` nguồn `manual`**, không còn là cột tổng trên `Orders`. Số gửi lên **trùng** tổng của các dòng SHEIN đang được tính tiền và không kèm `supplierOrderNo` ⇒ dòng `manual` (nếu có) bị xoá để sổ cái quay lại quy kết theo từng billno; số **khác** ⇒ dòng `manual` thắng (`OrderSupplierCost.SelectCharged` ưu tiên nhập tay có tiền) và các dòng SHEIN bị gỡ cờ `IsCharged`. Đây là chỗ đóng lỗi cũ "sync xoá công sửa tay": hai bên là hai dòng riêng, lượt sync không chạm dòng `manual`. `supplierOrderNo` gõ tay chỉ được giữ khi chưa dòng nào khác sở hữu (unique index toàn DB) |
| PUT | `/admin/orders/{id}/milestones` | `admin.orders.manage` | Cập nhật mốc fulfil local (partial): chỉ `fulfilPurchasedAt` / `trackingAddedAt`, hoặc `clearFulfilPurchasedAt: true` để **gỡ** stamp mua (undo bấm nhầm — clear mốc + provenance; nếu không còn `SupplierPurchases` thì clear luôn `fulfilAccountId`/`fulfilCardId`). **Từ chối** clear khi đơn đã gắn ≥1 billno (`SupplierPurchases.SupplierOrderNo`). Không kết hợp clear với `fulfilPurchasedAt` có giá trị. Gửi `inTransitAt` / `deliveredAt` / `refundedAt` → **400** (Transit/Giao thuộc Sync Fulfil; Hủy/Refund thuộc Sync 4Seller). Không gọi 4Seller. **Claim guard (lần đầu stamp mua):** nếu đơn đang bị user khác giữ (`FulfilClaimExpiresAt > now` và chưa `FulfilPurchasedAt`) → **409** trừ khi body `force: true` (Admin/ops takeover — đóng attempt chủ cũ `TakenOver`, phiên stamp = `Purchased`, phiên còn lại cùng user = `Superseded`, clear lease, SignalR `orderClaimChanged` `purchased`). Header tùy chọn `X-Fulfil-Session` để gắn `Purchased` đúng phiên. `clearFulfilPurchasedAt` → attempt `Purchased` gần nhất thành `Reverted`. **Attribution người mua:** bấm «Đã mua» (`fulfilPurchasedAt` có giá trị) ghi `PaidByUserId = actingUser` khi đơn chưa có người mua **hoặc** chưa có `OrderFulfilAttempt` Outcome=`Purchased` (kể cả khi sync đã stamp `FulfilPurchasedAt` trước) — tạo/đóng attempt `Purchased`, reconcile sổ cái nếu đã có `SupplierPurchases`, SignalR `purchased`. `clearFulfilPurchasedAt` → clear luôn `PaidByUserId`. **Lần claim mua** (Fulfil/Admin ≠ owner) → tự `PostMessage` `"Đã mua hàng thành công"` + `UserNotification(OrderChat)` + SignalR tới **seller (owner)** (toast Chrome khi tab ẩn) |
| POST | `/admin/orders/{id}/fulfil-claim` | `admin.orders.manage` | **Giữ đơn** (claim/lease). Bắt buộc header **`X-Fulfil-Session`** (UUID Chrome profile — thiếu → **400**). Body optional `{ siblingOrderIds?: Guid[], sessionLabel?: string }`. **Exclusive session:** mỗi phiên chỉ focus một nhóm click (primary + sibling cùng request); trước khi claim, mọi attempt mở của session trên orderId ngoài nhóm đó bị đóng `Released` (lease clear chỉ khi không còn phiên khác). Atomic: mở/renew `OrderFulfilAttempts` theo `(orderId, sessionId)`; lease trên `Orders` thuộc **user**; `FulfilClaimExpiresAt` = MAX ExpiresAt các phiên mở; `FulfilClaimSessionCount` = COUNT. TTL Settings `fulfil.claim.ttl_minutes` (mặc định 15, clamp 3–60); cap đơn/user `fulfil.claim.max_per_user` (mặc định 10 — đếm **đơn**, không đếm phiên). Sibling cùng phone+zip claim kèm; conflict sibling không fail primary. **409** khi user khác đang giữ. Trả `{ claim, siblingClaims[], conflicts[], releasedClaims[], serverTime }` + publish SignalR `orderClaimChanged` (released cũ + held mới). |
| DELETE | `/admin/orders/{id}/fulfil-claim` | `admin.orders.manage` | **Nhả claim**. Không `allSessions` → bắt buộc `X-Fulfil-Session`, đóng attempt phiên đó (`Released`); lease chỉ clear khi không còn phiên mở. Query `allSessions=true` (chủ lease hoặc Admin) nhả mọi phiên. Trả claim snapshot (`released` hoặc `held` + `sessionCount`). SignalR `orderClaimChanged`. |
| DELETE | `/admin/orders/fulfil-claims/session` | `admin.orders.manage` | **Nhả mọi claim của phiên hiện tại** (header `X-Fulfil-Session` bắt buộc). Dùng khi extension **LOGOUT** — không phụ thuộc cache client; chỉ đóng attempt thuộc user JWT + session đó; lease clear khi không còn phiên khác. Trả `{ items[], serverTime }` + `orderClaimChanged`. |
| GET | `/admin/orders/fulfil-claims` | `admin.orders.view` | Danh sách lease còn hiệu lực (poll fallback / sync panel): `{ items[{ orderId, state, claimedByUserId, claimedByName, claimedAt, expiresAt, sessionCount, sessionIds, startedAt }], serverTime }`. Trước khi trả, server dọn focus cũ: mỗi `sessionId` chỉ giữ batch (`BatchId`) mới nhất; attempt batch cũ → `Released`. |
| GET | `/admin/orders/fulfil-attempts` | `admin.orders.view` | Lịch sử lượt mua theo phiên (KPI). Query `pageCurrent`/`pageSize`/`orderId`/`userId`/`sessionId`/`dateFrom`/`dateTo`. Outcome: `Purchased` / `Released` / `Expired` / `TakenOver` / `Superseded` / `Reverted`. `durationSeconds` = ClosedAt−StartedAt (null nếu stamp mua không qua claim). |
| POST | `/admin/orders/shein-sync` | `admin.orders.manage` | Body `{ phase: "list"\|"detail", updateMode?: "new"\|"all", orders[] }` (thiếu `updateMode` → Settings) (mỗi order có `accountEmail`, optional `isJointShipment`, **`relationBillno`**, **`confluenceBatchNo`**, **`orderType`**, **`isMembershipOrVirtual`**, `productIds`, **`sheinStatus`** / **`sheinStatusGroup`** / **`sheinStatusAt`**, và **mốc thời gian per-chặng**: **`sheinShippedAt`** / **`sheinTrackingAt`** / **`sheinDeliveredAt`** / **`sheinCanceledAt`** / **`sheinRefundedAt`** — clock thật, độc lập nhau; và **tín hiệu thanh toán** **`sheinPaidAt`** / **`sheinUnpaid`**). ⚠️ **"Bỏ qua" giờ chỉ có nghĩa "chưa gắn được đơn sàn", không phải "không ghi gì".** Mọi billno trong payload đều được upsert vào `SupplierPurchases` **trước** khi chạy khớp (`SupplierPurchaseIngest.Upsert` — idempotent theo billno, merge sparse nên phase `list` không xoá breakdown mà phase `detail` vừa lấy), rồi nhánh bỏ qua chỉ ghi `skipReason` + `skipEvidenceJson` lên dòng đó. Dòng đứng chờ ở `/fulfil/supplier-purchases` với `OrderId = null`. Nhóm bị bỏ qua: đơn Club/Saver/virtual (`order_type=5|6`, `is_virtual_order`, goods_id `50000…` **đúng 9 chữ số**, SKU `VPP…`/`SQN…`) — cờ `IsMembershipOrVirtual` là **một chiều**, đã bật thì payload `list` (không mang `order_type`) không gỡ được, bằng không mỗi lượt sync lại gắn billno Club vào một đơn TikTok rồi phase detail gỡ ra, ghi rồi xoá một dòng sổ cái mỗi vòng — và billno **Refunded/Canceled chưa từng gắn**. **Gắn thêm billno vào đơn đã có link** (`sibling_linked` / `sku-already-covered`) chỉ tính SKU do billno **còn hiệu lực** phủ: billno đã huỷ/hoàn hoặc chưa thanh toán không chiếm chỗ, nên lần mua lại sau khi huỷ gắn được vào đúng đơn cũ. **Ràng buộc nhân quả:** billno mua trước khi đơn sàn được đặt (lệch quá 12 giờ) không khớp đơn đó ở mọi đường — khoá khớp là phone+zip người nhận nên một khách mua lặp lại trùng khoá với cả đơn đặt sau lần mua; lý do `ordered-before-platform-order`. Gỡ link kéo theo dọn mốc SHEIN không còn bằng chứng (mốc Giao cần billno `delivered`, Transit cần `shipped`/`delivered`, mốc Mua cần khớp clock của link còn lại). **CostUpdate** chỉ sum billno còn hiệu lực. Billno `refunded`/`canceled` (tiền đã về tài khoản mua hàng) **không** tính sổ cái; sync **gỡ** chúng khỏi đơn TikTok (`skipReason=terminal_unlinked`) giống `packageState: canceled`, để người đi đơn mua lại từ tài khoản SHEIN khác.<br>⚠️ **Đơn SHEIN chưa thanh toán không được đồng bộ — bất kể trạng thái.** `sheinPaidAt` = `pay_time` (list, giây) / `paymentTime` (detail, millisecond); `sheinUnpaid=true` **chỉ** khi payload nói rõ khoá đó bằng 0 (`null` = không kết luận được ⇒ giữ hành vi cũ). SHEIN cho tạo đơn trước rồi mới thu tiền và tự huỷ khi hết `order_expire_time` (~4 giờ) nếu không ai trả: billno như vậy chưa bị trừ đồng nào, nên giá mua / mốc **Mua** / dòng chi sổ cái suy ra từ nó đều là số bịa. Xử lý: **gỡ hẳn link khỏi đơn sàn** (kể cả link đã gắn), xoá dòng CostUpdate của billno đó, gỡ mốc **Mua** theo `OrderPurchaseEvidence`, và xoá `FulfilAccountId`/`FulfilCardId` khi đơn không còn link lẫn giá mua (hai field này do sync tự điền từ email billing của chính billno ghost). Đơn **đã** thanh toán rồi mới huỷ/hoàn vẫn sync như trước để theo dõi tiền hoàn.<br>**Chốt an toàn một chiều:** billno đã lưu `SupplierPaidAt` thì payload sau đó báo `pay_time = 0` **không** gỡ được nó (payload rút gọn / cache cũ không được xoá một lần mua thật); mode `all` vẫn giữ danh sách billno đã trả tiền (`preLinkedPaidBillnos`) để payload rút gọn không gỡ nhầm lần mua thật; không wipe link hàng loạt. Ca gốc: `577494030896631910` `DELIVERED` trên TikTok nhưng billno `GSU18J247000TWB` có `pay_time = 0` — trước đây bị tính $15.80 chi phí, stamp "Đã mua" sai và có một dòng chi CostUpdate không thật. Stamp `RefundedAt`/`CanceledAt` cấp đơn chỉ khi **mọi** dòng mua đều terminal. Match: billno (qua **`SupplierPurchases`**, tra thẳng cả dòng chưa gắn) rồi **phone+zip** (refresh billno đã gắn cho phép thiếu phone/zip). Gắn **thêm** Joint theo `relationBillno` / `confluenceBatchNo` / SKU; billno **gần giống** (Hamming) chỉ dùng khi payload nói rõ `isJointShipment=true` — heuristic đó quá yếu để tự gắn tiền. **Một khách nhiều đơn riêng** (cùng phone+zip, thường cùng SKU, `relationBillno` khác nhau): mỗi đơn sàn có trần số lần mua = số dòng hàng; billno tiếp theo **chuyển** sang đơn anh em còn trống thay vì `sibling_linked`. Joint thật (`relationBillno`/`confluenceBatchNo` chung) vẫn vượt trần. Đơn đang ôm quá trần được cân bằng trong cùng lượt (`rebalanced` trên response, mã `rebalanced_to_sibling` chỉ là lý do gỡ tạm — không vào `skipCounts`). Lưu `SupplierPurchases.SupplierStatus*` + **`SupplierPaidAt`** / **`SupplierUnpaid`** per billno; stamp timeline (**Sync Fulfil sở hữu** — rank `shein`) theo từng mốc thật: **Mua**→`FulfilPurchasedAt` (=`sheinPaidAt`, lùi về `orderedAt` **chỉ khi** payload không nói rõ đơn chưa thanh toán), `shipped`→`InTransitAt` (=`sheinShippedAt`) + Track (carrier), `delivered`→`DeliveredAt` (=`sheinDeliveredAt`), `canceled`/`refunded` — **không** stamp bằng sync-time (thiếu clock ⇒ để trống), **không** đổi `PlatformOrderStatus`. `OrderFulfilTimeline.Normalize` ép thứ tự đơn điệu (xoá Track/Transit vi phạm thay vì gán bằng nhau). Mode **`new`**: incremental update khi tracking/status/total đổi **hoặc** thiếu mốc mà payload có clock (không churn khi mốc thật vắng). Mode **`all`**: mở rộng pool mọi đơn không huỷ/hoàn trong cửa sổ **Lookback** (`refreshLookbackDays`, 1–60) và luôn ghi đè link đã có (không xóa link hàng loạt). `TrackingAddedAt` chỉ stamp khi có mã **carrier**. CostUpdate một dòng / order. Phase **list**: `needsDetailBillnos[]` khi **billno lần đầu** vào `SupplierPurchases` (`first_seen` — kể cả chưa khớp đơn sàn; trừ unpaid/terminal rõ từ list), **đơn mới gắn link** (`new_link`), **cost thiếu** (subtotal/total null), **total đổi**, **shipped/delivered còn thiếu carrier** (tracking trống hoặc mã kho `C…`/`N…`), **thiếu clock milestone** mà list không mang, **wallet_cost_stale**, hoặc **mode_all**. Status/tracking-only trên list (đơn cũ đủ cost) **không** kéo `get_order_info`.<br>**Nhật ký lượt sync:** body nhận thêm `runId` (extension sinh 1 mã cho cả lượt, dùng chung mọi POST list + detail) và `batchIndex`; backend ghi mỗi request 1 dòng vào `SheinSyncRunLogs` (giữ 7 ngày). Response thêm **`skipCounts`** (`{reason: count}`), **`skipOutcomes[]`** (`{billno, reason, detail}`) và **`unchanged`** (billno đã gắn, không có delta — chỉ ở mode `new`), cùng **`needsDetailReasons`** (`{billno: reason}` cho phase list). Mã lý do bỏ qua: `membership_virtual`, `unpaid_ghost`, `terminal_unlinked`, `missing_address`, `address_conflict`, `ambiguous_match`, `sibling_linked`, `no_local_match`, `awaiting_detail`, `duplicate_billno`, `purchased_before_order`. Response thêm **`rebalanced`** (số billno thừa đã chuyển sang đơn cùng khách còn trống — không vào `skipCounts`). Mã lý do gọi detail: `first_seen`, `new_link`, `cost_incomplete`, `total_delta` (kèm `:cũ→mới`), `no_carrier_tracking`, `missing_milestone_clock`, `mode_all`, `wallet_cost_stale`. Thêm **`saveFailed`** (bool): `SaveChangesAsync` vỡ thì các counter `matched`/`updatedTracking`/`updatedCost`/`autoShip*` của lô đó về 0 — báo thành công cho lô không ghi được là cách chỉ số sync nói dối. **Bất biến phase list:** `matched + unchanged + Σ skipCounts == fetched` — extension cảnh báo ngay trên thông báo khi lệch, và phần bỏ qua hiển thị là `chưa gắn N (…) — xem trang Đơn NCC chưa gắn` để chỉ đúng chỗ xử lý tiếp. |
| GET | `/admin/orders/shein-sync/config` | `admin.orders.view` | Settings → `{ enabled, intervalSeconds, updateMode, concurrentThreads, refreshLookbackDays, refreshBillnos[], pendingAutoMarkShipBillnos[], captureRawPayload, headerSource }` (`updateMode` = `new`|`all` từ Settings `extension.shein_sync.update_mode`; `headerSource` cố định `realtime`; Sync Order luôn extension gọi SHEIN; `refreshBillnos` = **chỉ** `force_billnos` chẩn đoán — **không** open-set từ DB; `refreshLookbackDays` = cửa sổ ngày `order/list` SHEIN + pool TikTok khi `updateMode=all` (1–60, mặc định 14); `concurrentThreads` 1–20, mặc định 10). **`pendingAutoMarkShipBillnos[]`** (trần 20, mới nhất trước) = billno còn việc Auto Mark Ship: link `TrackingKind=carrier` có mã, đơn vẫn `AWAITING_SHIPMENT` + `Orders.TrackingNo` trống, và `autoMarkShipStatus` ∈ `null`/`pending`/`unconfirmed`/`failed` đã hết cooldown 15 phút, hoặc còn `autoMarkShipRequestId` treo. Extension **ép POST** những billno này kể cả khi fingerprint `order/list` không đổi — xem mục Auto Mark Ship bên dưới. Response sync (`POST`) thêm `incompleteCostBillnos[]` khi matched nhưng cost chưa đủ (subtotal/total null) — extension **đưa vào hàng đợi** `get_order_info` (cùng `needsDetailBillnos`); `needsDetailBillnos` = **billno lần đầu** (`first_seen`, kể cả chưa khớp) / link mới / thiếu cost / total đổi / thiếu carrier sau ship / thiếu milestone clock / `mode_all` / wallet stale / **`pending_auto_mark_ship`** (Mark Ship bị hoãn theo ngân sách request — cần thêm một lượt POST, **không** vào cost queue); **`unmatchedBillnos`** = fail phone+zip (thiếu địa chỉ / không khớp / conflict) — extension lấy Shipping từ `get_order_info` rồi POST detail để rematch. Khóa khớp đơn mới lấy đúng 4 field SHEIN `shipping_telephone` / `shipping_postcode` / `shipping_firstname` / `shipping_lastname` (list: nested `shippingaddr_info`; detail `get_order_info`: cùng key **phẳng** trên order root — không dùng `telephone`/`postcode` Billing) (trong `shippingaddr_info`) — không alias, không Billing; detail **overwrite** địa chỉ Shipping lên list. Pool đơn local dùng `AsSplitQuery` (Include `Items` + `SupplierLinks` cùng lúc gây tích Descartes, lặp lại ở mọi lô POST); mode list-first chỉ nạp đơn mở có `OrderCreateTime` trong **90 ngày** (đơn đã gắn billno trong batch luôn vào pool bất kể tuổi). |
| POST | `/admin/orders/shein-sync/run` | `admin.orders.manage` | Legacy Local runner (không còn dùng vận hành — Sync Order luôn Realtime qua extension). Body `{ accountId }`. |
| POST | `/admin/orders/shein-detail-sync/run` | `admin.orders.manage` | Chạy tay **một chu kỳ đầy đủ** quét chi tiết SHEIN phía backend (bỏ qua cờ bật/tắt và nghỉ interval — để kiểm tra không phải đợi). Không body. Snapshot **toàn bộ** ứng viên → xử lý theo lô `max_billnos` → `get_order_info` → `SyncFromSheinAsync(phase=detail, updateMode=all)` → Mark Ship khi có carrier (ngân sách `max_mark_ship_per_pass` **reset mỗi lô**). Single-flight: nếu chu kỳ khác đang chạy → `{ summary: "Đang chạy chu kỳ khác…" }`. Trả `{ candidates, batches, fetched, updatedTracking, updatedCost, autoShipped, autoShipFailed, autoShipBlocked, skipped{}, notes[], summary }`. Mã lý do `skipped`: `account_unknown`, `session_missing`, `account_inactive`, `fetch_failed`. Tuổi session >12 giờ **không** skip. Khi lần đầu lấy được mã carrier thật, Telegram **Add track** (gate `shein.order_detail_sync.notify_carrier_tracking`); Mark Ship còn `pending` → stamp `TelegramAddTrackNotifyPendingAt`, gửi ở chu kỳ `detail-` sau. |
| PUT | `/admin/orders/{id}/settlement` | `admin.orders.manage` | Đánh dấu thanh toán tiền về shop: `isSettlementPaid`; `true` upsert `ReceivingBankTransaction` SettlementPaid (Credit = Rev = `sellerNetAmount ?? totalPaidAmount ?? amount`) trên thẻ gắn shop — lỗi nếu shop chưa gắn thẻ; `false` xóa txn SettlementPaid; không gọi 4Seller |
| PUT | `/admin/orders/{id}/usps-processed` | `admin.orders.manage` | Đánh dấu / bỏ đánh dấu xử lý thêm bước USPS: body `{ "isUspsProcessed": boolean, "applyFee"?: boolean }` — `applyFee` mặc định `true`; `true`+`applyFee` stamp `uspsProcessedAt` + Settings `order.handling.buy_track` → `buyTrackAmount` (**không** ghi đè khi đã có `uspsScanBatchId` — phí từ `price_per_label` PrintPackShip là nguồn chân lý); `true`+`applyFee:false` **chỉ stamp** (giữ amount, thường 0 — menu Done); `false` clear stamp, `buyTrackAmount=0`, clear `uspsScanBatchId`/`uspsScanSentAt` (tiền PrintPackShip đã trừ, không hoàn); **không** tạo dòng Manual sổ cái; dọn legacy Manual `[BuyTrack]`/`[ThankCard]` nếu còn; không gọi 4Seller |
| POST | `/admin/orders/usps-scan/push` | `admin.orders.manage` | **Đẩy track scan USPS** hàng loạt lên PrintPackShip (2 bước: `POST /batches/tracking` tạo draft → `POST /batches/{id}/send` xác nhận — **bước gửi không idempotent, trừ tiền thật**). Body `{ "orderIds": Guid[], "notes"?: string }`. Lọc hợp lệ: có mã VC, `HasUspsSignal`, chưa `uspsScanBatchId`, không trùng mã trong request, không nằm batch `SendUnknown`, chưa In-transit/Delivered (`InTransitAt`/`DeliveredAt` hoặc platform `IN_TRANSIT`/`DELIVERED`); loại REFUND/CANCELLED. Persist `UspsScanBatches` (`Draft`) trước khi gửi; thành công → stamp `uspsProcessedAt` + `uspsScanBatchId` + `uspsScanSentAt` + `buyTrackAmount = price_per_label`. Response `{ batchId, status (Sent\|SendFailed\|SendUnknown\|""), sentCount, pricePerLabel, scanFee, totalCharged, balanceCurrent, pagesFailed, skipped[{ orderId, platformOrderId, trackingNo, reason }], error }`. Skip reasons: `not_found` / `not_owned` / `no_tracking` / `not_usps` / `already_pushed` / `duplicate_tracking` / `pending_batch` / `already_in_transit` / `terminal_commerce`. `SendUnknown` (timeout/5xx) **không** stamp đơn và **chặn đẩy lại** mã đó đến khi admin kiểm tra PrintPackShip |
| GET | `/admin/orders/usps-scan/batches` | `admin.orders.view` | List batch scan đã đẩy (paged `pageCurrent`/`pageSize`) — audit `externalBatchId`, status, pricing, items (order + tracking) |
| PUT | `/admin/orders/{id}/thank-card-processed` | `admin.orders.manage` | Đánh dấu / bỏ đánh dấu mua ThankCard: body `{ "isThankCardProcessed": boolean, "applyFee"?: boolean }` — `applyFee` mặc định `true`; `true`+fee stamp + Settings → `thankCardAmount`; `true`+`applyFee:false` chỉ stamp (menu Done); `false` clear stamp/amount; **không** tạo dòng Manual; dọn legacy Manual nếu còn; không gọi 4Seller |
| PUT | `/admin/orders/{id}/refund-processed` | `admin.orders.manage` | Đánh dấu / bỏ đánh dấu ops đã xử lý Refund: body `{ "isRefundProcessed": boolean }` — `true` stamp `refundProcessedAt` (giữ timestamp cũ nếu đã có); `false` clear stamp; **không** ledger fee, không gọi 4Seller; khác `refundedAt` (đồng hồ 4Seller) |
| PUT | `/admin/orders/{id}/notes` | `admin.orders.manage` | Cập nhật Buyer/Seller/Internal notes trên 4Seller (`POST /api/v2/order/update-note`) rồi lưu local |
| PUT | `/admin/orders/{id}/message` | `admin.orders.manage` | **Legacy** — nội dung → GetOrCreate 1 issue-tag + replace set; rỗng = clear tags + `IssueStatus=None`. Prefer `…/issue-tags`. |
| GET | `/admin/issue-tags` | `admin.orders.view` | Thư viện tag lỗi (system + custom) |
| POST | `/admin/issue-tags` | `admin.orders.manage` | Tạo tag thư viện `{ label }` (max 50, unique CI) |
| PUT | `/admin/issue-tags/{tagId}` | `admin.orders.manage` | Đổi nhãn thư viện `{ label }` — refresh `Orders.Message` các đơn đang gắn |
| DELETE | `/admin/issue-tags/{tagId}` | `admin.orders.manage` | Xóa tag custom (chặn system); gỡ khỏi mọi đơn + sync Message/IssueStatus |
| PUT | `/admin/orders/{id}/issue-tags` | `admin.orders.manage` | Replace bộ tag trên đơn `{ tagIds }` — sync `Message`, `IssueStatus`, Telegram (không còn audit `OrderMessages` / inbox / realtime) |
| PUT | `/admin/orders/{id}/issue-status` | `admin.orders.manage` | `{ status: Processing\|Done }` — **không** bắt buộc đã gắn tag |
| POST | `/admin/orders/sync` | `admin.orders.manage` | **202 Accepted** — xếp hàng sync 4Seller detached (`OrderSyncJobRunner`); poll `GET …/sync/status/{jobId}`. Body optional `ownerUserId`, `orderStatuses[]` (bucket 4Seller; rỗng → bucket **`all`**, một truy vấn/tài khoản lấy mọi trạng thái), `dateFrom`/`dateTo`, `openStatusesIgnoreDateFilter` (chỉ có tác dụng khi `orderStatuses[]` là bucket cụ thể: bucket mở bỏ filter ngày, `shipped`/`canceled` vẫn theo range; bucket `all` **luôn** theo range). Job gắn `MaxParallelOwners` từ Settings `fourseller.order_sync.new.batch_size`. Response job: `{ jobId, state, syncedSellers, inserted, updated, failedSellers[], message, error }`. Upsert semantics giữ nguyên (commerce status latch REFUND, tracking carrier, pricing detail, …) — xem architecture |
| GET | `/admin/orders/sync/status/{jobId}` | `admin.orders.manage` | Tiến độ / kết quả job sync thủ công (`running`\|`completed`\|`failed`); 404 khi hết hạn (~30 phút sau xong) |
| POST | `/admin/orders/after-sales/sync` | `admin.orders.manage` | Đồng bộ đơn sau bán ngay, bỏ qua chu kỳ cấu hình. Body optional `{ ownerUserId }`. Chạy `GET /api/returns/tiktok/sync` (4Seller kéo từ TikTok Shop) rồi `POST /api/returns/tiktok/page`. **Luồng duy nhất** đặt/gỡ `PlatformOrderStatus=REFUND`, `RefundedAt`, `CancelReason`, `ReturnStatus`. `RefundedAt` stamp từ **`platformCreateTime`** của case (lúc mở yêu cầu hoàn) — **không** phải `platformUpdateTime`; xem hàng dưới. Response `{ syncedSellers, returnsRead, ordersUpdated, refunded, refundReverted, refundWithoutCaseReleased, refundWithoutCaseStuck, casesWithoutClock, unmatchedReturns, notified, skipped, failedSellers[] }`; `skipped=true` khi sync đơn đang giữ `FourSellerSyncGate` |
| — | **`REFUND` bám danh sách return của 4Seller** | — | Nguồn sự thật cho `REFUND` là **sự có mặt của case** trên `POST /api/returns/tiktok/page`, không phải việc tiền đã về buyer hay chưa: 4Seller liệt kê đơn ở danh sách hoàn ngay từ lúc buyer mở yêu cầu, nên tab hoàn của ta phải khớp danh sách đó. Case **còn mở** (`AWAITING_BUYER_SHIP` / `BUYER_SHIPPED_ITEM`) và case **Complete** ghi y hệt nhau — chỉ nhãn Telegram khác (case mở báo "đơn cần xử lý hoàn/trả", không báo "vừa bị refund"). Đơn **không có case nào** thì **không** được ở `REFUND`: đợt full scan gỡ về trạng thái thương mại trong `Order.RawPayload`, đếm vào `refundWithoutCaseReleased` (đọc không ra status → để yên + warning, đếm `refundWithoutCaseStuck`). Bước gỡ này **chỉ** chạy trong full scan đã đi hết danh sách — pull incremental dừng ở watermark nên "không khớp case" ở đó là kết luận sai |
| — | **`returnStatus`** | — | Chữ trạng thái case từ 4Seller (`returnStatus`: "Awaiting customer return", "Refund complete", …). OrderCard hiện **dưới** `cancelReason`. Chỉ luồng after-sales ghi; xoá khi case đóng hoặc đơn rời `REFUND`. Backfill one-shot `maintenance.order_return_status_v1` (pull incremental không đọc lại case yên) |
| — | nguồn **`refundedAt`** | — | Clock hoàn = **`platformCreateTime`** từ `POST /api/returns/tiktok/page` (lúc buyer mở case) — đây là mốc TikTok Shop / 4Seller hiển thị và sort theo, áp cho cả case còn mở. **`platformUpdateTime`** là lần sàn xử lý case gần nhất: nó dịch về sau mỗi lượt cập nhật nên chỉ dùng làm **con trỏ pull incremental** (`orderBy=platformUpdateTime`), và chỉ đỡ vào `refundedAt` khi sàn không trả mốc mở. Provenance `platform`; stamp tay (`manual`) / SHEIN (`shein`) rank cao hơn nên không bị đè. `REFUND` + `refundedAt` null là trạng thái không đọc được (card hiện `Refund —`, tab hoàn đẩy đơn xuống đuôi null) nên case không có cả hai mốc thì **không** set `REFUND`, chỉ ghi `cancelReason` + warning, đếm `casesWithoutClock`. Dữ liệu cũ được sửa bởi các đợt one-shot `maintenance.refunded_at_platform_create_time_v1` và `maintenance.refund_case_presence_reconcile_v1` — xem `docs/architecture.md` |
| DELETE | `/admin/orders/{id}` | `admin.orders.manage` | Xóa 1 đơn local (+ cascade items); **không** gọi 4Seller |
| DELETE | `/admin/orders` | `admin.orders.manage` | Xóa theo cùng filter list (bỏ paging); response `{ deletedCount }`; **không** gọi 4Seller |

### Hàng đợi đơn NCC — `/admin/supplier-purchases`

Khớp tự động cần `shipping_telephone` + `shipping_postcode` trùng tuyệt đối nên luôn có phần trượt. Trước đây trượt là **mất luôn**: chỗ duy nhất trong DB chứa được một billno là bảng con của `Orders`, không khớp đơn ⇒ không ghi gì cả (dấu vết duy nhất là `SheinSyncRunLogs`, tự xoá sau 7 ngày). Giờ mọi billno vào `SupplierPurchases` **trước** khi chạy khớp, `OrderId = null` khi chưa gắn được, và các endpoint dưới đây là đường để người xử lý kết thúc nốt.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/supplier-purchases` | `admin.orders.view` | List phân trang (`pageCurrent` mặc định 1, `pageSize` mặc định 50 / max 200) + filter `matchState` (`matched`\|`unmatched`\|`ignored`), `skipReason`, `fulfilAccountId`, `orderId`, **`paidByUserId`** (người mua hàng hiệu lực = `SupplierPurchase.PaidByUserId ?? Order.PaidByUserId`), `from`/`to` (theo `SupplierPaidAt ?? OrderedAt ?? FirstSeenAt`), `keyword` (billno / `relationBillno` / tracking / tên–điện thoại–mã bưu chính người nhận). Trả `{ items, total, pageCurrent, pageSize, unmatchedCount, ignoredCount }` — hai số đếm cuối là **badge tổng**, không theo bộ lọc trang. Thứ tự: dòng chưa gắn lên đầu, rồi mốc mua giảm dần, tie-break `Id` (hai lần mở cùng bộ lọc cho cùng thứ tự). Mỗi dòng kèm `source` (`shein`\|`manual`), `matchState`, `skipReason`, **`skipEvidenceJson`** (bằng chứng near-miss: `poolSize` / `phoneMatches` / `zipMatches` / `bothMatches` / `rejectedPurchasedBeforeOrder` / `rejectedCanceled` / `phoneKey` / `zipKey` / `mode`), `isCharged`, tiền, mốc, người nhận, tài khoản mua hàng, **`paidByUserId` / `paidByUserName`** (cột UI **Fulfill** — cùng chuỗi sổ cái; dòng chưa gắn thường `null`), `firstSeenAt` / `lastSeenAt` / `lastRunId` |
| POST | `/admin/supplier-purchases/{id}/attach` | `admin.orders.manage` | Body `{ orderId, fulfilAccountId? }`. **`fulfilAccountId`** chỉ dùng khi lần mua chưa quy kết được tài khoản (email billing SHEIN không map `FulfilAccount`): điền vào `SupplierPurchases.FulfilAccountId` + thẻ active của tài khoản đó. Truyền một tài khoản **khác** với tài khoản đã lưu → **400** (đổi quy kết tiền phải đi đường sửa dòng chi, không núp sau nút Gắn); tài khoản không tồn tại → **404**. Gắn lần mua vào đơn sàn rồi chạy lại `OrderSupplierCost.ApplyAggregates` + `ReconcileCostUpdateTransactionsAsync` cho **cả** đơn cũ (nếu đang gắn nơi khác) lẫn đơn mới — bằng không cùng một khoản chi nằm trên hai đơn. Gán `OrderId` mới chạy **trước** hai lượt reconcile: vòng dọn của reconcile bỏ qua dòng chi mà lần mua đã chuyển sang đơn khác, nên dòng tiền **giữ nguyên `Id`** và chỉ đổi `orderId` thay vì bị xoá rồi mất hẳn. Reconcile tìm dòng `CostUpdate` theo `SupplierPurchaseId` (kể cả orphan còn `OrderId` cũ sau sync gỡ ngoài pool) rồi **chuyển/cập nhật** thay vì INSERT — tránh 23505 `IX_FulfilTransactions_PurchaseId_CostUpdate`. Đơn mới được stamp mốc **Mua** (`FulfilPurchasedAt`) từ `SupplierPaidAt ?? OrderedAt` (nguồn `shein`, không dùng giờ bấm nút); đơn cũ gỡ mốc đó qua `ClearPurchasedWhenLinkMovedAway` khi provenance không phải Manual, không còn lần mua đã thanh toán, và timestamp khớp clock của lần mua vừa dời — bằng không UI hiện "Mua?" trên đơn đúng và tab Đã mua trên đơn sai. Quy kết `FulfilAccountId` / `FulfilCardId` của **cả hai** đơn được suy lại từ tập lần mua qua `OrderPurchaseEvidence.SyncAttributionWithPurchases` (đơn mới nhận tài khoản của lần mua, đơn cũ mất nó khi không còn lần mua nào của tài khoản đó); đơn mới cũng nhận `PaidByUserId` của lần mua khi đang trống. Set `matchState=matched`, `matchedAt`, `matchedByUserId` và **`manualLinkedAt`** (khoá gắn tay — sync không tự chuyển billno này sang đơn khác nữa), xoá `skipReason`/`skipEvidenceJson` |
| POST | `/admin/supplier-purchases/{id}/detach` | `admin.orders.manage` | Gỡ khỏi đơn sàn. Dòng `shein` **không bị xoá** — quay về hàng đợi với `skipReason=manual_detach` (một lần mua đã xảy ra thì bằng chứng phải còn lại); dòng `manual` bị xoá hẳn vì không còn nguồn nào ngoài người nhập. Xoá `manualLinkedAt` (về hàng đợi thì không còn quyết định tay nào để khoá). Tính lại giá mua + sổ cái của đơn |
| POST | `/admin/supplier-purchases/{id}/ignore?ignored=true` | `admin.orders.manage` | Đánh dấu đã xem và quyết định bỏ qua (Club/Saver, đơn cá nhân, mua thử…) → `matchState=ignored`, bị loại khỏi `SelectCharged` **và luồng sync không gắn lại** (payload vẫn làm tươi tiền/trạng thái, chỉ bước khớp bị chặn); `skipReason` trống thì ghi `operator_ignored`. `ignored=false` trả về hàng đợi và xoá `skipReason`/`skipEvidenceJson` cũ. **400** nếu dòng đang gắn vào một đơn (gỡ ra trước) |
| DELETE | `/admin/supplier-purchases/{id}` | `admin.orders.manage` | Xóa hẳn lần mua NCC + dòng chi `CostUpdate` gắn với nó (nếu có). Nếu đang gắn đơn sàn thì tính lại giá mua/sổ cái của đơn. Khác **Bỏ qua**: không còn dấu vết trong hàng đợi. Lượt sync SHEIN sau vẫn có thể ghi nhận lại cùng billno nếu còn trong cửa sổ list |
| POST | `/admin/supplier-purchases/bulk-delete` | `admin.orders.manage` | Body `{ ids: Guid[] }` (tối đa **200**). Cùng ngữ nghĩa với DELETE đơn lẻ: xóa hẳn lần mua + dòng chi `CostUpdate`, tính lại giá mua/sổ cái cho các đơn bị ảnh hưởng (mỗi đơn reconcile **một lần** dù chọn nhiều lần mua cùng đơn). Trả `{ deleted }` |

Mã `skipReason` (`Shared.Constants.SheinSkipReasons`): `membership_virtual`, `unpaid_ghost`, `terminal_unlinked`, `missing_address`, `address_conflict`, `ambiguous_match`, `sibling_linked`, `no_local_match`, `duplicate_billno`, `ledger_deleted` (legacy — xoá sổ cái giờ xóa hẳn lần mua), `purchased_before_order`, `manual_detach`, `operator_ignored`, `rebalanced_to_sibling` (gỡ tạm khi chuyển billno thừa sang đơn cùng khách còn trống — dòng được gắn lại trong cùng lượt, không nằm lại hàng đợi), `manual_link_locked` (đã gắn tay, sync giữ nguyên đơn đang gắn; chỉ báo cáo khi đơn đó nằm ngoài pool của lượt), `platform_canceled` (ứng viên duy nhất là đơn sàn đã hủy/hoàn và mua sau khi hủy — vào hàng đợi), `moved_off_canceled_order` (gỡ tạm khi tự chuyển billno khỏi đơn đã hủy sang đơn còn hiệu lực trong cùng lượt).

**Khoá gắn tay** — `supplierPurchaseDto.manualLinkedAt` (cũng có trên `fulfilTransactionDto.manualLinkedAt`) là mốc người xử lý tự chọn đơn. Có mốc này thì lượt SHEIN sync **không** chuyển lần mua sang đơn khác ở cả ba đường máy tự quyết: fast-path lệch phone/zip chế độ Cập nhật tất cả, nhánh thu hồi billno đang thuộc đơn khác, và bước tự cân bằng đơn vượt trần (`SelectSurplusPurchase`). Máy chỉ khớp được phone+zip+tên+SKU nên nó không biết thứ người xử lý đã tra ra; thiếu khoá này thì một lần sửa tay biến mất âm thầm sau lượt sync kế tiếp.

Admin UI: `/fulfil/supplier-purchases` (**Đơn NCC chưa gắn**, menu Fulfillments) — mặc định lọc `matchState=unmatched`, cột billno/ngày/tiền/người nhận/lý do + cột **Fulfill** (`paidByUserName`) kèm nút xem bằng chứng, thao tác **Gắn vào đơn** (tra đơn theo `search`, tối thiểu 3 ký tự) / **Gỡ** / **Bỏ qua** / **Xóa**. Desktop: checkbox chọn nhiều + **Xóa (N)** hàng loạt (`POST …/bulk-delete`); mobile không checkbox. Bộ lọc **Người fulfil** (gated `admin.users.view`, `GET /admin/users/options?roles=Admin,Fulfil`) gửi `paidByUserId`.

### List query

List load: **two-phase** — query 1 lấy `(Id, orderTimeKey)` của trang (không Include), rồi **projection slim** theo id: root `Select` vào `OrderListItemDto` (nav inline), 1 query `OrderItems`, 1 query `SupplierPurchases`. Không materialize entity ⇒ list **không** kéo `SupplierPurchase.RawPayload` (~55 KB/dòng), `SkipEvidenceJson`, `ProductIdsJson`, `ApplicationUser.FourSellerCookie` hay `FulfilCard.CardNumber` thô (mask trong C# sau khi materialize). `WorkflowTab` / `TotalCost` / `PrimaryPurchase` / `TotalQuantity` tính trong C# sau khi attach — dùng đúng helper cũ (`OrderWorkflowTabs.Resolve`, `OrderSupplierCost`, `ResolveOrderTotalCost`).

**Khóa thời gian thống nhất**: filter ngày, `PendingQueueFilter` và sort đều dùng `OrderPaidTime ?? OrderCreateTime ?? CreatedAt` (trước đây sort dùng thứ tự coalesce khác nên không index nào phục vụ được cả hai). Index: expression `IX_Orders_OrderTimeKey_Desc` / `IX_Orders_Owner_OrderTimeKey_Desc`, cột denormalize `(WorkflowTab)` / `(OwnerId, WorkflowTab)` / `(HasUspsSignal, UspsProcessedAt)` / `(HasWarehouseSignal, ThankCardProcessedAt)`, GIN `pg_trgm` cho search (`Orders.PlatformOrderId` / `TrackingNo` / `ShipmentNo` / `BuyerName` / `BuyerPhone` / `Address`, `SupplierPurchases.SupplierOrderNo` / `TrackingNo`), `IX_ListingVariations_SellerSkuLower` cho overlay listing. Overlay listing (SourceUrl + rating/shop NCC) dùng **một** lookup chung `(OwnerId, LOWER(SellerSku))` cho cả list và sync.

| Param | Mô tả |
|-------|--------|
| `pageCurrent` | Trang (mặc định 1) |
| `pageSize` | Size (mặc định 20, max 200; admin-web mặc định **50**, hover menu prefetch list mặc định tuần) |
| `cursor` | **Keyset (opt-in)**: gửi lại `nextCursor` của trang trước → server seek thẳng tới dòng kế tiếp trên `(orderTimeKey, Id)`, **bỏ qua `pageCurrent`** (hết `OFFSET` lớn). Chỉ hợp lệ khi sort + filter không đổi; cursor sai/hỏng → fallback trang 1 (không trả lệch). Bị bỏ qua ở mọi view **không** sort theo order-time key: `FulfilPurchasedAt` (`purchasedToday=true` hoặc tab `PURCHASED`) và clock hoàn/hủy (tab `REFUND` / `CANCELLED` / `REFUND_QUEUE_*` — xem `sortDir`). `nextCursor` = `null` ở trang cuối (trang chưa đầy) và ở các view sort mua / sort hoàn-hủy. Paging theo số trang vẫn hoạt động nguyên như trước |
| `platformOrderStatus` / `statusTab` | Filter **workflow tab** (suy diễn): `PENDING_PROCESS` / `PURCHASED` / `AWAITING_COLLECTION_GROUP` / `IN_TRANSIT` / `DELIVERED` / `CANCELLED` / `REFUND` — hoặc legacy union **`IN_TRANSIT_GROUP`** (= Chờ lấy + Đang giao) — hoặc filter-only **`USPS_UNPROCESSED`** / **`USPS_PROCESSED`** — hoặc **`THANK_CARD_PENDING`** / **`THANK_CARD_PROCESSED`** — hoặc **`REFUND_UNPROCESSED`** / **`REFUND_PROCESSED`** — hoặc **`CANCEL_UNPROCESSED`** / **`CANCEL_PROCESSED`** (đơn hủy + cùng stamp `RefundProcessedAt`) — hoặc legacy combined **`USPS_THANK_CARD_PENDING`** / **`USPS_THANK_CARD_PROCESSED`** — hoặc legacy raw (`ON_HOLD`…). Raw TikTok vẫn lưu `platformOrderStatus`; list trả thêm `workflowTab` + `cancelReason`. TikTok `COMPLETED` được chuẩn hoá thành `DELIVERED` khi ghi, và một đơn đã `REFUND` **không** bị lượt sync sau đẩy về status thương mại (latch — xem `docs/architecture.md`) |
| `pendingFilter` | Sub-filter sider **Chờ mua hàng** (`/orders/pending`, thường kèm `platformOrderStatus=PENDING_PROCESS`): `new` = đơn trong ngày VN; `out_stock` = `IssueStatus=Processing`; `24h` = tuổi 0–&lt;24h; `48h` = tuổi 24–&lt;48h; `over_48h` = tuổi ≥48h (`OrderPaidTime ?? OrderCreateTime ?? CreatedAt`). Bỏ trống = tất cả chờ mua |
| `ownerUserId` | Filter theo seller |
| `platform` | Filter theo mã platform (vd. `TikTok`, so khớp không phân biệt hoa thường với `order.platform`) |
| `shopId` | Filter theo shop local |
| `search` | Mã đơn / tracking / shipment / fourSellerOrderId / thông tin khách (`buyerName`, `buyerPhone`, `buyerEmail`, `address`, `city`, `state`, `postalCode`); SĐT cũng khớp theo chuỗi số (bỏ `+()-` khoảng trắng). **Route theo hình dạng từ khóa:** từ khóa **≥8 ký tự chỉ gồm chữ số** + phân cách (` -_.+()`) là mã/SĐT chứ không phải người ⇒ chỉ tìm trên `platformOrderId` / `shipmentNo` / `trackingNo` / `fourSellerOrderId` / `buyerPhone` / billno-tracking NCC, bỏ 6 cột text (tên, email, địa chỉ, city, state, postal) mà từ khóa dạng đó không thể khớp có nghĩa. Có bất kỳ chữ cái ⇒ đi đường đủ 10 cột như cũ (mã tracking có thể bắt đầu bằng chữ). **Khi có `search`:** server **bỏ** `dateFrom` / `dateTo` only; `platformOrderStatus` / `pendingFilter` vẫn áp dụng nếu client gửi. Admin/seller UI: queue sider (`/orders/pending|usps|thank-card|refund`) giữ path + tab (search trong queue); trang chính `/orders` + tab status → về tab **Tất cả** (lookup toàn cục, không gửi tab) |
| `dateFrom` | Mốc thời gian đơn từ (inclusive; `OrderPaidTime ?? OrderCreateTime ?? CreatedAt`) |
| `dateTo` | Mốc thời gian đơn đến (inclusive) |
| `needsPurchase` | `true` → chỉ đơn chưa mua local (`FulfilPurchasedAt` null) — dùng bởi extension KBI Fulfil tab «Đơn cần mua». Bị bỏ qua khi `purchasedToday=true` |
| `purchasedToday` | `true` → chỉ đơn có `FulfilPurchasedAt` trong **ngày lịch VN hôm nay** (`AppTimeZones.GetVietnamDayUtcRange`). Sort theo `FulfilPurchasedAt` (không theo ngày đặt). Extension tab «Đơn đã mua»; thường kèm `productSourceId`. **Scope:** non-Admin (Fulfil) bị controller ép `paidByUserId = chính mình` — không thể lách bằng query; Admin bỏ trống = tất cả |
| `paidByUserId` | Lọc theo `Orders.PaidByUserId` (fulfil đã stamp mua). Dùng cùng `purchasedToday` hoặc độc lập. Non-Admin + `purchasedToday` → giá trị client bị ghi đè bằng user hiện tại |
| `isCustomerPaid` | `true`/`false` → lọc theo `Orders.IsCustomerPaid` (khách đã/chưa TT trên sàn). Extension fulfil gửi `true` cùng `needsPurchase` |
| `productSourceId` | Lọc đơn có ≥1 `OrderItem.SourceUrl` khớp `domains` của ProductSource (host/subdomain) |
| `sortDir` | `asc` = sớm nhất trước; mặc định / `desc` = mới nhất trước. Sort mặc định theo **`OrderPaidTime ?? OrderCreateTime ?? CreatedAt`** (khớp filter ngày + `pendingFilter`), tie-break `Id`. **Hai ngoại lệ đổi khóa sort theo tab** (xem hàng dưới): mua hàng và hoàn/hủy |
| `sortDir` — ngoại lệ **mua** | Khi `purchasedToday=true` **hoặc** `platformOrderStatus=PURCHASED` → sort theo **`FulfilPurchasedAt`** (đơn mua mới nhất lên đầu; stamp null xếp cuối) |
| `sortDir` — ngoại lệ **hoàn/hủy** | Tab hoàn/hủy sort theo **clock hoàn/hủy của sàn** để thứ tự khớp 4Seller / TikTok Shop (mới hoàn/hủy lên đầu), **không** theo ngày đặt: `REFUND` / `REFUND_UNPROCESSED` / `REFUND_PROCESSED` → **`RefundedAt`**; `CANCELLED` / `CANCEL_UNPROCESSED` / `CANCEL_PROCESSED` → **`CanceledAt`**; queue gộp `REFUND_QUEUE_ALL` / `REFUND_QUEUE_UNPROCESSED` / `REFUND_QUEUE_PROCESSED` → **`COALESCE(RefundedAt, CanceledAt)`**. Stamp null xếp **cuối** và đuôi đó vẫn sort theo `OrderPaidTime ?? OrderCreateTime ?? CreatedAt` (chỉ luồng sau bán stamp `RefundedAt` nên một phần tab `REFUND` còn trống — không để rơi về thứ tự `Id` vô nghĩa), tie-break `Id`. **`RefundProcessedAt` là stamp ops, không bao giờ là khóa sort** |

Badge đếm nằm ở endpoint riêng `GET /admin/orders/status-counts` (seller: `GET /orders/status-counts`) — response list trả `statusCounts = null`. Shape counts (aggregate theo **workflow tab** trong cùng scope filter, **bỏ** tab đang chọn + paging): `all`, `pendingProcess`, `purchased`, `awaitingCollection` (= `AWAITING_COLLECTION_GROUP`), `inTransit` (= `IN_TRANSIT`), `delivered`, `canceled`, `refund`, **`uspsUnprocessed`** / **`uspsProcessed`**, **`thankCardPending`** / **`thankCardProcessed`**, **`refundUnprocessed`** / **`refundProcessed`**, **`cancelUnprocessed`** / **`cancelProcessed`**, legacy **`uspsThankCardPending`** / **`uspsThankCardProcessed`**, cộng sub-bucket chờ mua **`pendingNew`** / **`pendingOutStock`** / **`pending24h`** / **`pending48h`** / **`pendingOver48h`** (trong `PENDING_PROCESS`, bỏ `pendingFilter`). Mutual exclusion workflow: REFUND → CANCELLED → DELIVERED (chỉ TikTok) → Đang giao (`platform=IN_TRANSIT`) → Chờ lấy (carrier track hoặc `AWAITING_COLLECTION`, sàn chưa `IN_TRANSIT`) → Đã mua (fulfil purchased, chưa carrier) → Chờ xử lý. Sider **`/orders/pending`**: tabs **Tất cả / Đơn mới / 48 Giờ / Quá 48h / Hết hàng** (`?pending=` → API `pendingFilter`). Sider **`/orders/usps`**: `USPS_UNPROCESSED`/`USPS_PROCESSED`. Sider **`/orders/thank-card`**: `THANK_CARD_PENDING`/`THANK_CARD_PROCESSED` — pending = tín hiệu kho (`warehouse` / `C…`/`N…` / `WarehouseTrackingNo`) **và** chưa stamp **và** chưa last-mile carrier / mark ship (`AWAITING_COLLECTION` / `IN_TRANSIT` / `DELIVERED`); đơn đã add track thật 4Seller không còn trong hàng đợi dù còn `WarehouseTrackingNo`; UI tab Chưa xử lý / Đã xử lý; deep-link `?handling=processed`. Sider **`/orders/refund`**: một hàng tab **Tất cả** / **Chờ xử lý** / **Đã xử lý** / **Đơn hoàn** / **Đơn hủy** (`?kind=` omit=Tất cả / `refund` / `cancel`) (`?handling=` omit=chờ / `processed`; `REFUND_QUEUE_UNPROCESSED`/`REFUND_QUEUE_PROCESSED` / `REFUND_*` / `CANCEL_*`).

**Nguồn của tab filter + counts là cột denormalize trên `Orders`** (`WorkflowTab`, `HasCarrierTracking`, `HasSupplierPurchase`, `HasUspsSignal`, `HasWarehouseSignal`) chứ không còn là `EXISTS` tương quan vào `SupplierPurchases` + `LOWER(...) LIKE '%usps%'` — xem `docs/architecture.md` (single-writer qua `SaveChangesAsync` + verifier chống drift). Logic phân tab **không đổi**: cột chỉ materialize kết quả `OrderWorkflowTabs.Resolve` / `OrderFulfilHandlingSignals`.

**FE cache**: list và counts là hai query key khác nhau, nên mọi mutation đơn phải invalidate cả hai. Dùng helper dùng chung `invalidateOrdersCaches(queryClient, scope?)` (`@drop/api-client`) thay cho `invalidateQueries(['adminOrders'])` / `['sellerOrders']` rời rạc — đã áp ở `useAdminOrders`, `useOrders`, `useAdminSupplierPurchases`, `useAdminReceivingBanks`, `useFourSeller`. Bỏ sót một call site = badge đếm đứng số cũ.

Admin-web `/orders` OrderCard body (desktop): 5 cột **Sản phẩm · Khách hàng · Thời gian · Vận chuyển · Trạng thái** — **đủ line items** (ảnh 52px + số thứ tự overlay góc thumb như Listings, rating listing cạnh SKU), Đặt+Hạn, tracking (+ lỗi Auto Mark Ship); header · **badge carrier** đầu identity (USPS đỏ/xanh theo `uspsProcessedAt`; carrier khác cam nhạt; chưa last-mile nhưng có mã kho SHEIN → badge **Kho**) · mã đơn (click → copy text `platformOrderId`) · `ownerName · shopProfileName` · **`fulfilAccountName`** khi đã mua (click → copy URL chi tiết đơn NCC/SHEIN từ `supplierOrderNo`/`supplierOrderUrl`/`supplierLinks`, toast «Đã copy link SHEIN»; tooltip `TK mua: … · Copy: …`) + **badge `message`** (local, max 500, solid đỏ `error` — mọi nội dung) cạnh badge ghi chú; `cancelReason` dưới status khi có; **`returnStatus`** (trạng thái case 4Seller) dòng thứ hai dưới lý do khi có; package/shop name xem drawer chi tiết.

### Sync body

```json
{
  "ownerUserId": null,
  "orderStatuses": ["to_ship"],
  "dateFrom": "2026-07-01T07:00:00.000Z",
  "dateTo": "2026-07-16T06:59:59.999Z"
}
```

`ownerUserId` null = mọi user có cookie. `orderStatuses` rỗng/null → mặc định **một bucket `all`** (**bucket 4Seller remote** khi gọi `/api/v2/order/page`, referer `order2.html?type=allOrder`): response trả mọi trạng thái trộn chung trong cùng một lượt phân trang, mỗi record tự mang `orderStatus` nên vẫn đóng dấu đúng từng đơn. Trước đây mặc định là 6 bucket rời (`pending`/`on_hold`/`to_ship`/`in_process`/`shipped`/`canceled`) ⇒ mỗi tài khoản 4Seller phải gọi ít nhất 6 request; nay chỉ còn một chuỗi trang. Tab UI admin = **workflow tab** (`PENDING_PROCESS`/`PURCHASED`/`AWAITING_COLLECTION_GROUP`/`IN_TRANSIT`/`DELIVERED`/`CANCELLED`/`REFUND`); Đồng bộ map workflow tab → bucket 4Seller (`PENDING_PROCESS`→`on_hold`+`to_ship`, `PURCHASED`→`to_ship`, `AWAITING_COLLECTION_GROUP`/`IN_TRANSIT`/`IN_TRANSIT_GROUP`/`DELIVERED`→`shipped`, `CANCELLED`→`canceled`, `REFUND`→`canceled`+`shipped`). Map tab hẹp vẫn giữ để sync từ một tab chỉ kéo đúng 1–2 bucket liên quan. Manual date UI; auto lookback_days. Tab **Tất cả** gửi `orderStatuses` rỗng → bucket `all`.

⚠️ **4Seller không lọc đủ 6 bucket — đừng quay lại thiết kế cũ.** Đo thực tế bằng `GET /api/v1/dev/diagnostics/order-page-status-coverage` (và `GET /api/v1/dev/diagnostics/order-page-search-probe/{orderNo|auto}` cho nhánh tra một đơn theo `searchValue`; `allHonoursSearch` phải `true`) (chỉ Development; đối chiếu `all` với 6 bucket rời trên cùng tài khoản + cùng khoảng ngày, trả `missingFromAll` = đơn chỉ bucket rời thấy). Trên 5 tài khoản, cửa sổ 2 ngày: `missingFromAll = 0` ở mọi tài khoản ⇒ `all` là **tập cha**. Nhưng `returnedStatuses` từng bucket cho thấy 4Seller **bỏ qua** bộ lọc với hai giá trị: `on_hold` và `canceled` đều trả về **toàn bộ** tập đơn (một tài khoản: bucket `on_hold` và bucket `canceled` cùng trả 88 bản ghi gồm `on_hold:5, to_ship:36, shipped:45, canceled:2` — đúng bằng `all`). `in_process` luôn trả **0**. `pending` trả về các bản ghi mang `orderStatus=on_hold` (từ vựng request ≠ từ vựng record). Chỉ `to_ship` và `shipped` là lọc đúng. Hệ quả: vòng lặp 6 bucket cũ tải **262 bản ghi để lấy 88 đơn riêng biệt** (~3× thừa) cộng một round-trip `in_process` chắc chắn rỗng — đây là nguyên nhân thật của việc sync chậm, không phải số lượng đơn. Khi UI chọn khoảng ngày (RangePicker / preset Ngày·Tuần·Tháng theo **Asia/Ho_Chi_Minh**), body gửi `dateFrom`/`dateTo` (ISO UTC = biên ngày VN) — server forward sang `dateQueryCreateStartTime` / `dateQueryCreateEndTime` trên `POST /api/v2/order/page` dưới dạng wall-clock **America/Los_Angeles** (`yyyy-MM-dd HH:mm:ss`, cùng khoảng tuyệt đối). Chuỗi thời gian 4Seller inbound là naive UTC → lưu UTC (giữ nguyên digits, tag UTC). Lọc theo **ngày tạo đơn**; để trống Paid/Ship/Cancel/PrintLabel. Không chọn ngày → để trống `dateQuery*` (sync không giới hạn thời gian). Seller cookie hết hạn / chưa `logged_in` → bỏ qua hoặc ghi `failedSellers[]` khi chọn đúng seller; không dừng batch.

### Shipping services query

| Param | Mô tả |
|-------|--------|
| `ownerUserId` | Bắt buộc — seller có cookie 4Seller |
| `platform` | Optional — filter carrier theo platform (vd. `tiktok`, so khớp không phân biệt hoa thường) |

Response: mảng `{ id, carrier, platform, region }` (dedupe theo `carrier`).

### Mark ship body

```json
{
  "shippingService": "USPS",
  "trackingNo": "9400111899223344556677"
}
```

Server map đơn local → payload 4Seller `to-ship-mark-ship` (`id` = `fourSellerOrderId`, `shopId` = `fourSellerShopId`, `platform` lower-case, `trackingUrl` mặc định `""`), rồi poll `get-async-ship-order-result`: manual **60×2s (~2 phút)**; **Sync Track / detail sweep** (interactive) **30×2s (~60s)**. **HTTP extension `POST shein-sync` không chạy Auto Mark Ship** (để job `shein.order_detail_sync` / Sync Track). Pending/timeout → `unconfirmed` (giữ `requestId`); chỉ `Status=Failed` + message reject thật mới `failed` + xoá `requestId`. Trước khi trả `unconfirmed`, server đọc thêm đơn trên 4Seller — nếu tracking đã lên sàn thì sync local, không báo lỗi. **Retry thủ công (Đẩy track)** khi link `unconfirmed`/`requestId`: poll/reconcile request cũ trước; nếu sàn đã có cùng mã → sync local; nếu sàn **xác nhận chưa có carrier tracking** (kể cả trong grace) hoặc hết grace → clear `unconfirmed`/`requestId` rồi **POST `to-ship-mark-ship` lại ngay**; chỉ báo Unconfirmed khi không đọc được đơn 4Seller. Poll thành công khi `data.success` truthy (`true`, `"1"`, …) và `failed` = `"0"`/`0`/absent — kể cả `data.data = "[]"`. Thành công → cập nhật local `trackingNo`, `shippingService`, `platformOrderStatus = AWAITING_COLLECTION`.

List/detail `supplierLinks[]` trả thêm `autoMarkShipTrackingNo` / `autoMarkShipStatus` (`pending`|`succeeded`|`failed`|`blocked`|`unconfirmed`) / `autoMarkShipAttemptedAt` / `autoMarkShipError` / `autoMarkShipRequestId`. Khi SHEIN đã có tracking carrier nhưng `Orders.TrackingNo` còn trống và auto Mark Ship `failed`/`blocked`/`unconfirmed`, UI Orders hiện `autoMarkShipError` ở cột Vận chuyển + drawer chi tiết. **Manual Mark Ship** (menu hoặc icon đẩy track) cũng ghi cùng các field này khi lỗi — không chỉ Auto Mark Ship.

`pending` không có message nào để hiện, nên UI đo thời gian chờ thay vì đọc lỗi: khi link `pending` mà `supplierStatusAt` (fallback `inTransitAt`) đã quá **2 giờ**, cột Vận chuyển hiện cảnh báo vàng «Auto Mark Ship chưa đẩy được mã lên sàn» và drawer gắn tag **Mark Ship chờ sync** trên billno. Không có nó thì đơn kẹt trông giống đơn vừa ship — cùng tab **Chờ lấy**, cùng nút Đẩy track, không dấu hiệu nào cho biết tự động đã bỏ qua.

**UI đẩy track thủ công:** đơn `AWAITING_SHIPMENT` **Seller Shipping** (không phải TikTok/Platform Shipping), còn track carrier trên `supplierLinks` và chưa có `Orders.TrackingNo` → icon cạnh mã track. Click → tải shipping-services; exact-match carrier SHEIN↔4Seller thì confirm + `POST mark-ship`; không khớp / thiếu carrier → modal Mark Ship prefill `trackingNo` + hint tên carrier SHEIN. Đơn TikTok Shipping: ẩn icon + menu Mark Shipped; badge tooltip giải thích TikTok tự giao.

**Ma trận lỗi / retry Auto Mark Ship**

| Status | Ý nghĩa | Tự thử lại? |
|--------|---------|-------------|
| `pending` | Có carrier, chờ publish | Có — job **Auto Sync Order detail SHEIN** / Sync Track (`max_mark_ship_per_pass`) |
| `failed` | 4Seller từ chối / cookie / mạng | Có sau **15 phút** |
| `blocked` | Thiếu carrier / không khớp Shipping Service / thiếu shop / sàn đã có mã khác | Không — Mark Ship thủ công; đơn TikTok/Platform Shipping |
| `unconfirmed` | Đã gửi Mark Ship, poll hết hạn | Auto: **reconcile** trước (poll dài `requestId` / đọc sàn); hết grace **30 phút** mà sàn trống → `failed`. Manual Đẩy track: sàn trống (kể cả trong grace) → clear rồi **POST lại ngay**; chỉ block khi không đọc được 4Seller |
| `succeeded` | Đã publish (hoặc reconcile xác nhận) | Không |

Response shein-sync: `updatedTracking` = số lần **local** `SupplierPurchases.TrackingNo` đổi (warehouse hoặc carrier); `autoShipped` = số lần publish/reconcile Mark Ship thành công lên 4Seller (hai counter độc lập) — **HTTP extension thường để 0** vì không chạy pha Mark Ship.

**Tách luồng Sync vs Mark Ship.** Extension / realtime `POST shein-sync` chỉ ingest (match, gắn billno, cập nhật tracking/cost). Phase `list` vẫn ghi `TrackingKind=carrier` khi `order/list` đã có `shipping_no` để link vào hàng đợi `PendingQueuePredicate`. Auto Mark Ship + reconcile chỉ khi caller set `MaxAutoMarkShipAttempts` hoặc `AutoMarkShipPollInteractive` (detail sweep / Sync Track). Khi sweep hết ngân sách giữa các lô (`MaxAutoMarkShipAttempts=0`), billno bị hoãn có thể vào `needsDetailBillnos` với reason **`pending_auto_mark_ship`** trong lượt nội bộ đó.

**Transit ≠ Mark Ship.** Tab **Chờ lấy** (`AWAITING_COLLECTION_GROUP`) gồm sàn `AWAITING_COLLECTION` **hoặc** đã có mã carrier SHEIN mà sàn chưa `IN_TRANSIT` (Mark Ship chưa lên). Tab **Đang giao** chỉ khi sàn `IN_TRANSIT`. Legacy filter `IN_TRANSIT_GROUP` = union cả hai. **Text/chấm trạng thái trên card Orders đọc `platformOrderStatus` (không theo tab)** — đơn trong tab Chờ lấy có thể hiện «Chờ giao» khi sàn còn `AWAITING_SHIPMENT`; tooltip có dòng `Nội bộ: Chờ lấy`. Timeline UI hiện Transit trước Track cũng theo đúng thứ tự đó.

**Retry giữa các chu kỳ sync (detail sweep).** Khi mã vận đơn đã đứng yên, fingerprint extension có thể `skipDbEntirely`. Job **Auto Sync Order detail SHEIN** mỗi chu kỳ snapshot lại `NeedsCarrierTrackingPredicate` + `PendingQueuePredicate` (không phụ thuộc POST extension; **không** cắt top-N theo `LastSeenAt`). `interval_minutes` = nghỉ **sau khi** xử lý hết snapshot. Extension vẫn nhận `pendingAutoMarkShipBillnos[]` để ép POST list khi cần làm tươi carrier. `HasSheinIncrementalDelta` coi `NeedsReconcile` / `unconfirmed` là delta để lượt sweep mode `all` không `continue` trước reconcile.

- `GET shein-sync/config` trả `pendingAutoMarkShipBillnos[]`; extension truyền tập này làm `alwaysInclude` cho `filterSheinListOrdersChanged`.
- `HasSheinIncrementalDelta` coi `NeedsReconcile` / `unconfirmed` là delta, để link còn requestId treo đi tiếp tới hàng đợi reconcile trên sweep.

Billno kẹt nằm ngoài cửa sổ list nông (20 đơn gần nhất) không bị ép POST riêng — cứ 10 lượt Auto Sync có một lượt full lookback (`forceFullPost`) POST cả cửa sổ, đủ để dọn mà không phải trả thêm request `get_order_info` nào.

### Update cost body

```json
{
  "cost": 14.2,
  "shippingFee": 3.99,
  "tax": 1,
  "ads": 0,
  "fulfilAccountId": null,
  "fulfilCardId": null,
  "paidByUserId": null
}
```

Phí local trên `Orders` (không sync từ 4Seller). Null từng field = clear field đó. Validate `>= 0`. Response = order detail; `totalQuantity` = tổng `quantity` line items (min 1). Các field `cost` / `shippingFee` / `tax` / `ads` **deprecated**. **`totalCost` API = gross NCC + BuyTrack + ThankCard** (subtotal+ship+tax+… từ `Charged*`; fallback ledger khi thiếu subtotal; **fallback cuối = SUM live trên dòng `IsCharged` đang nạp** khi snapshot `Charged*` trống, để `totalCost` không bao giờ null trong khi các field `supplier*` của cùng response đã có số; cộng `buyTrackAmount` / `thankCardAmount` khi đã đánh dấu); **Profit = Revenue − totalCost** — không còn công thức `cost × qty + shipping + tax + ads`.

### Update settlement body

```json
{
  "isSettlementPaid": true
}
```

Đánh dấu tiền về shop trên thẻ nhận tiền (riêng với Cập nhật phí). `true` yêu cầu shop đã gắn thẻ; Amount ledger = Rev (`sellerNetAmount` nếu có, else `totalPaidAmount` nếu ≠ 0 else `amount`). `false` xóa txn `SettlementPaid` và set `isSettlementPaid=false`. Response = order detail (có `isSettlementPaid`).

### Update USPS processed body

```json
{
  "isUspsProcessed": true,
  "applyFee": true
}
```

Đánh dấu xử lý thêm bước last-mile USPS (local, không gọi 4Seller). Body thêm `applyFee` (mặc định `true`). `isUspsProcessed:true` + `applyFee:true` → stamp `uspsProcessedAt` **và** set `buyTrackAmount` từ Settings `order.handling.buy_track` (mặc định `0.5`); `applyFee:false` → **chỉ stamp**, không đổi amount (menu **Done** trên `/orders/usps`). `false` clear stamp, `buyTrackAmount=0`. **Không** tạo dòng Manual trên sổ cái — phí hiện cột **BuyTrack** trên dòng CostUpdate của đơn (read-model từ Order). Toggle cũng xóa legacy Manual `[BuyTrack]`/`[ThankCard]` nếu còn. Reconcile CostUpdate chỉ refresh fee khi stamp **và** `buyTrackAmount > 0` (không gắn phí lại cho đơn Done không phí). Response = order detail. UI OrderCard: badge **USPS** đỏ/xanh đầu identity (click = có phí); carrier khác / **Kho** cam nhạt outline (không toggle).

### Update ThankCard processed body

```json
{
  "isThankCardProcessed": true,
  "applyFee": true
}
```

Đánh dấu mua ThankCard (local). Body thêm `applyFee` (mặc định `true`). `true`+fee → stamp `thankCardProcessedAt` + Settings `order.handling.thank_card` (mặc định `1`) → `thankCardAmount`; `applyFee:false` → chỉ stamp (menu **Done** trên `/orders/thank-card`). `false` clear stamp/amount. **Không** tạo dòng Manual — phí hiện cột **ThankCard** trên CostUpdate. UI: badge **TKS** (click = có phí) khi còn việc Thank Card hoặc đã stamp — sau mark ship / carrier thật thì không hiện TKS đỏ. Reconcile chỉ refresh fee khi stamp **và** `thankCardAmount > 0`.

### Update Refund processed body

```json
{
  "isRefundProcessed": true
}
```

Đánh dấu ops đã xử lý đơn TikTok REFUND hoặc CANCELLED (local, không gọi 4Seller, **không** ledger fee). `true` stamp `refundProcessedAt` = UTC now nếu đang null (giữ timestamp cũ); `false` clear stamp. Khác `refundedAt` (đồng hồ commerce 4Seller). UI OrderCard: badge **REF** (hoàn) / **HỦY** (hủy) — đỏ chờ / xanh đã xử lý; sider `/orders/refund` một hàng tab **Tất cả** / **Chờ xử lý** / **Đã xử lý** / **Đơn hoàn** / **Đơn hủy** (`?kind=`) (`REFUND_*` / `CANCEL_*`). Menu **Done** trên tab Chờ xử lý / Tất cả (đơn chưa stamp) = cùng API.

**Phí BuyTrack / ThankCard:** chỉ lưu trên `Orders.BuyTrackAmount` / `ThankCardAmount` (cộng vào `totalCost` / Profit). Không còn dòng Manual `[BuyTrack]`/`[ThankCard]` trên sổ cái (migration `RemoveLegacyHandlingFeeLedgerRows` + cleanup khi toggle / CostUpdate reconcile). Đơn chưa có CostUpdate: phí vẫn trên Order; cột sổ cái hiện khi đã có dòng mua.

### Update notes body

```json
{
  "buyerMemo": "Cần giao nhanh",
  "sellerMemo": "Hết size",
  "internalMemo": "Internal Notes"
}
```

Đẩy lên 4Seller `POST /api/v2/order/update-note` với cookie seller (`order.ownerId`): `orderIdList = [fourSellerOrderId]`, `updateBuyerMemo/updateSellerMemo/updateInternalMemo = true`, `tagIdList = []`. Chuỗi rỗng/whitespace → clear (lưu `null`). Max 2000 ký tự/field. Thành công → cập nhật local `buyerMemo` / `sellerMemo` / `internalMemo`. List API cũng trả 3 field này để card hiện badge.

Khi **đồng bộ đơn** (`POST /admin/orders/sync` / job nền): nếu `order.cost` còn `null` → autofill từ `ListingVariation.initialPrice` (cùng `ownerId`, match `orderItem.sellerSku` ↔ `listingVariation.sellerSku`, không phân biệt hoa thường). Nhiều SKU khác giá → lấy InitialPrice của line item đầu (theo `fourSellerOrderItemId`) có match. Không đè `cost` đã có; không đụng `shippingFee` / `tax` / `ads`. Nếu `orderItem.sourceUrl` trống → autofill từ `Listing.sourceUrl` (cùng owner + sellerSku); sync không ghi đè `sourceUrl` đã có khi 4Seller bỏ trống. List/detail cũng overlay `sourceUrl` từ listing khi thiếu (không cần re-sync). Line item có `sourceImgUrl` (nếu có trên `OrderItem`); admin UI thumbnail ưu tiên `sourceImgUrl`, sau đó `imgUrl` TikTok/4Seller. Click thumbnail / tên SP mở `sourceUrl` (fallback `listingUrl`) tab mới. List/detail overlay thêm (read-model, không persist trên `OrderItem`) từ `Listing` match OwnerId + `ListingVariation.SellerSku` (ưu tiên listing mới cập nhật): `averageRating` / `reviewCount` + shop NCC qua `SupplierProductInfos`→`SupplierSellerInfos` — `storeTitle` / `storeUrl` / `shopRank` / `storeAttention` / `shippingDays` / `shippingDayPercent` / `isLocalProduct` / `isSupportQuickShip` (`storeTitle` trống → parse `soldByTips`). FE OrderCard cột SP parity ListingCard: dòng 1 tên · dòng 2 sao/`4.51 (n)` (+ SKU/variant) · dòng 3 shop NCC + rank/follow + SLA + badge Local/`Nhanh` (ngưỡng màu Settings `listing.rating.*`).

Background / auto sync — **hai job nền độc lập**, mỗi job clock + `enabled` + cỡ lô riêng; chu kỳ đếm từ lúc lượt trước **kết thúc**:

1. **Đơn hàng mới** — `IOrderSyncOrchestrator` + `OrderSyncBackgroundService` (tick 1 phút, delay 15s) + pulse `/health`. Settings: `fourseller.order_sync.new.enabled` / `.new.interval_minutes` (mặc định **bật** / **5**) / `.new.lookback_days` (mặc định **2**, clamp 1–7) / `.new.batch_size` (mặc định **15**, clamp 1–100). Một truy vấn bucket `all` cho mỗi tài khoản 4Seller (mọi trạng thái trộn chung, gồm cả `canceled` để Telegram hủy không phải đợi luồng 12 giờ), lọc create-date theo lookback ngắn (`OpenStatusesIgnoreDateFilter=false`).
2. **Trạng thái đơn hàng** — `IOrderStatusSyncOrchestrator` + `OrderStatusSyncBackgroundService` (tick 1 phút, delay 90s) + pulse `/health`. Settings: `fourseller.order_sync.status.enabled` / `.status.interval_hours` (mặc định **bật** / **12**, clamp 1–168) / `.status.lookback_days` (mặc định **30**, clamp 1–60) / `.status.batch_size` (mặc định **10**). Cũng một truy vấn bucket `all` cho mỗi tài khoản, cửa sổ dài; `SkipStalePricingSweep=true` + **`SkipNewOrderTelegram=true`** (chỉ làm tươi `platformOrderStatus`, **không** gửi Telegram đơn mới).

**Đơn vị lô = tài khoản 4Seller** (một cookie), không phải shop: mỗi tài khoản gọi `order/page` với `shopIdList: []` **và `orderStatus: "all"`** nên một chuỗi trang đã trả đơn của mọi shop + mọi trạng thái thuộc cookie đó. `SyncCoreAsync` chia owners thành lô `MaxParallelOwners` — trong lô `Task.WhenAll`, các lô chạy tuần tự. Sync thủ công (`POST /admin/orders/sync` / `POST /orders/sync`) trả **202** ngay qua `OrderSyncJobRunner` (poll `…/sync/status/{jobId}`) và gắn `MaxParallelOwners` = Settings `fourseller.order_sync.new.batch_size` (mặc định 15) — tránh fan-out mọi seller cùng lúc + Cloudflare **524** khi giữ HTTP >~100s. Trần trang mỗi truy vấn: `MaxPagesPerQuery = 100` (một truy vấn `all` gánh khối lượng của cả 6 bucket cũ).

Cả hai + luồng sau bán dùng chung `FourSellerSyncGate` (SemaphoreSlim tĩnh) — hai job ghi `Orders` bằng hai `DbContext` khác nhau, chồng nhau thì `SaveChanges` sau ghi đè. Luồng trạng thái giữ gate **trọn lượt** nên luồng đơn mới + pull sau bán tạm nghỉ (clock không advance khi bị chặn → retry tick sau). **Ưu tiên đơn mới:** `OrderStatusSyncOrchestrator` bỏ qua tick (không advance `lastFinished`) khi luồng đơn mới đang chạy, overdue, hoặc chưa từng finish (cold start); `/health` cũng chỉ pulse status khi điều kiện đó không đúng. **Telegram đơn mới** chỉ từ luồng sync nhanh (+ `TrySendPendingNewOrdersAsync` bù 24h khi shop resolve muộn): `NotifyNewOrdersAsync` **claim** `TelegramNewOrderNotifiedAt` bằng `ExecuteUpdate` (WHERE null) **trước** khi gọi Telegram API — tránh đôi tin khi sync và sweep đua nhau; gửi lỗi thì clear stamp để retry. Đơn mở tạo ngoài `status.lookback_days` không còn tự refresh — vá bằng menu OrderCard **Sync trạng thái sàn**. Đơn hủy → `NotifyCanceledOrdersAsync` (đơn hoàn **không** đi qua đường sync đơn).

**Độ tin cậy của hai vòng lặp:** mọi `BackgroundService` kế thừa `ResilientBackgroundService` — `ExecuteAsync` là `sealed`, bắt exception, `LogCritical` + ghi `BackgroundServiceHealthRegistry`, chờ 30s rồi chạy lại vòng lặp. Trước đó một Npgsql connect timeout thoát khỏi `ExecuteAsync` đủ để .NET `StopHost` tắt cả API (mặc định `BackgroundServiceExceptionBehavior`); `Program.cs` nay set `Ignore` làm lưới an toàn thứ hai. `SyncWatchdogBackgroundService` (delay 3 phút, tick 5 phút) bắn Telegram vào `telegram.cancel_refund_chat_id` khi một luồng trễ quá `fourseller.order_sync.watchdog.overdue_minutes`, chưa từng finish, hoặc khi có vòng lặp nền vừa crash — kèm tin "đã chạy lại" khi hết kẹt.

**Đồng bộ đơn sau bán (return / refund)** — `IAfterSalesSyncOrchestrator` (singleton) + `AfterSalesSyncBackgroundService` (tick 1 phút, delay khởi động 45s). Hai nhánh due **độc lập**, không nối tiếp nhau vì 4Seller kéo TikTok bất đồng bộ: nhánh *trigger* gọi `GET /api/returns/tiktok/sync`, nhánh *pull* gọi `POST /api/returns/tiktok/page` (`orderBy=platformUpdateTime`, `desc`, 100 dòng/trang, tối đa 10 trang/seller). Settings: `fourseller.after_sales.trigger.enabled` / `.trigger.interval_minutes` (mặc định **tắt** / 30 phút), `fourseller.after_sales.pull.enabled` / `.pull.interval_minutes` (mặc định **tắt** / 15 phút), `fourseller.after_sales.notify_enabled`, `fourseller.after_sales.telegram_chat_id`, `fourseller.after_sales.watermark` (JSON `{ ownerId: platformUpdateTime }`, job tự ghi — per-owner để một seller lỗi cookie không làm seller khác mất dữ liệu).

Quy tắc áp trạng thái (`OrderAfterSalesSyncService`): gom theo đơn và **chỉ lấy case mới nhất** (`platformCreateTime`, `id`) — khách mở rồi huỷ yêu cầu thì bản ghi cũ không được tranh trạng thái; khớp đơn theo `erpOrderId` → `Order.FourSellerOrderId`, fallback `platformOrderId` + `OwnerId`. Map từ `platformReturnStatus`: `RETURN_OR_REFUND_REQUEST_CANCEL` (buyer rút yêu cầu / sàn từ chối) → khôi phục trạng thái thương mại trong bản ghi, gỡ mốc `Refunded` nếu mốc đó do Platform stamp, xoá `CancelReason`. **Mọi trạng thái còn lại** — `RETURN_OR_REFUND_REQUEST_COMPLETE` cũng như case còn chạy (`AWAITING_BUYER_SHIP`, `BUYER_SHIPPED_ITEM`, trạng thái lạ) → `REFUND` + stamp `RefundedAt` = `platformCreateTime ?? platformUpdateTime` (rank Platform) + ghi `CancelReason`; cả hai mốc null thì **không** đặt `REFUND` (chỉ ghi `CancelReason` + warning). Trạng thái lạ rơi vào nhánh này là có ý: TikTok thêm status mới không được âm thầm đẩy đơn ra khỏi tab hoàn. `Stamp` cùng rank chỉ nhận clock sớm hơn nên case mới mở trên đơn đã hoàn trước đó không đè mất mốc hoàn thật. Telegram chỉ bắn cho đơn **thực sự đổi** một trong ba cột `PlatformOrderStatus` / `RefundedAt` / `CancelReason` trong lượt đó — dedupe tự nhiên; lượt chạy đầu tiên (watermark rỗng) quét 1 trang, đồng bộ trạng thái nhưng **không** gửi thông báo. Timestamp 4Seller (`OrderCreateTime`/`OrderPaidTime`/…) là naive UTC → lưu UTC; list/filter/dashboard/Telegram thống nhất ưu tiên `OrderCreateTime ?? OrderPaidTime ?? CreatedAt` (dashboard seller summary: `OrderCreateTime ?? CreatedAt`). Format đơn mới và job tổng hợp ngày giữ nguyên như trước.

Hiệu năng upsert (khi số đơn lớn + lookback nhiều ngày): `UpsertRecordsAsync` chạy change-detection để **bỏ ghi thừa** — `ApplyRecord`/`ApplyItem` chỉ gán giá trị nghiệp vụ, chỉ đóng dấu `UpdatedAt`/`LastSyncedAt` khi đơn thực sự thay đổi (đơn không đổi → không sinh `UPDATE`). `RawPayload` JSON thô chỉ ghi khi đơn mới hoặc khi field nghiệp vụ đổi — **không** để JSON 4Seller “nhấp nháy” mỗi lần kéo làm `updated` nhảy 78/65/213 dù không có thay đổi thật. `updated` trong toast = số đơn **thực sự đổi nghiệp vụ**. Vì vậy `LastSyncedAt` chỉ tăng khi đơn có thay đổi. Mỗi trang tắt `AutoDetectChangesEnabled`, `DetectChanges` thủ công 2 lần (sau enrichment + sau khi đóng dấu timestamp) rồi `ChangeTracker.Clear()` để bộ nhớ/`DetectChanges` cố định theo trang (~100 đơn) thay vì tích lũy cả run.

Admin UI: `/orders` — load từ DB; sync thủ công qua nút Đồng bộ; job nền `OrderSyncBackgroundService`. List **không auto-poll** (tránh nhảy dữ liệu khi đang xem) — làm mới bằng nút Tải lại / Đồng bộ hoặc rời trang rồi vào lại (`refetchOnMount: 'always'`). Đơn `AWAITING_SHIPMENT`: **icon đẩy track** cạnh mã carrier (khi SHEIN đã có track chưa publish) — khớp Shipping Service thì Mark Ship ngay; không khớp → modal chọn service (prefill tracking). Menu 3 gạch cạnh **Chi tiết** → **Mark Shipped** (khi `AWAITING_SHIPMENT`) → modal chọn Shipping Service + Tracking No (cũng prefill tracking từ supplier nếu có). Trên queue **USPS / Thank Card / Refund** (tab chờ, đơn chưa stamp): menu **Done** → confirm + `PUT …/*-processed` với `applyFee: false` trên USPS/Thank Card (chỉ stamp, **không** cộng BuyTrack/ThankCard); Refund không có phí. Badge USPS/TKS click vẫn `applyFee: true` (có phí). Menu **Ghi chú** → modal Buyer / Seller / Internal Notes (đẩy 4Seller + local); badge B/S/I trên header card, hover xem nội dung. Menu **Thông báo / Chat** → panel chat. **Không** còn mục **Cập nhật phí** / **Thanh toán** trên menu OrderCard (API `PUT …/cost` / `PUT …/settlement` vẫn dùng từ luồng khác: SHEIN sync / fulfil). Drawer **Mua hàng**: hiển thị 1:1 các cột tiền của `SupplierPurchases` được tính tiền (thiếu → 0; không suy/tính từ field khác). **Không** còn menu Đánh dấu In Transit / Delivered / Refund (mốc Transit/Giao từ Sync Fulfil; Hủy/Refund từ Sync 4Seller — tránh timeline lệch cột trạng thái sàn). Card Rev + drawer **Thực nhận** / section **Doanh thu** (breakdown khi đã enrich; cạnh **Mua hàng** 2 cột). Card list: header 1 hàng — identity (cột cố định) + **compact fulfil timeline** cùng dòng (cột cố định 5 mốc Đặt→Mua→Track→Transit→Giao, căn giữa các card) + **Cost/Rev/Pft**/actions (`$cost → $rev +$pft`; `Pft` = Rev − `SupplierTotal`; xanh / cam khi <$10 / đỏ lỗ); duration giữa mốc (xong = static, đang chờ = live 30s). Drawer chi tiết: phí NCC + notes + timeline dọc kèm duration + người đi đơn. Timeline **không** gọi API riêng — dùng mốc đã có trên list/detail payload.

**Fulfil pipeline milestones** (cột trên `Orders`, UTC) — **một cửa ghi duy nhất** `OrderFulfilTimeline.Stamp` + `Normalize`, provenance lưu ở `Orders.FulfilTimelineSources` (jsonb). **Rank ghi đè: `Manual > Shein > Platform > Legacy`** (rank cao đè bất kể giờ; bằng rank giữ mốc sớm hơn). **Mọi mốc chỉ ghi bằng clock sự kiện thật — không bao giờ giờ chạy sync**; thiếu clock ⇒ để trống.

Chủ sở hữu từng cột:

| Cột | Chủ / nguồn clock | Ghi chú |
|-----|-------------------|---------|
| `OrderCreateTime` | 4Seller sync | mốc gốc |
| `FulfilPurchasedAt` | Update Cost / manual (Manual) · SHEIN `orderedAt` (Shein) | tốc độ mua = `− OrderCreateTime` |
| `TrackingAddedAt` | Mark Ship (Manual) · **4Seller `OrderShippedTime`** (Platform) | mốc **mã vận đơn lên sàn**. SHEIN **không** phát ra clock này (payload chỉ có `shipping_no` trần) nên tuyệt đối không lấy `sheinShippedAt` làm dự phòng — làm vậy Track sẽ luôn bằng Transit |
| `InTransitAt` | **SHEIN `sheinShippedAt`** (Shein) | mốc **hàng rời kho** SHEIN; 4Seller không có clock này; **không** stamp tay |
| `DeliveredAt` | **SHEIN `sheinDeliveredAt`** (Shein) | mốc **khách ký nhận**; 4Seller không có clock này; **không** stamp tay |
| `RefundedAt` | 4Seller `CancelTime` khi `PlatformOrderStatus=REFUND` (Platform) | commerce terminal — **không** stamp từ SHEIN hoàn NCC / tay |
| `CanceledAt` | 4Seller `CancelTime` khi CANCELLED/REFUND (Platform) | commerce terminal — **không** stamp từ SHEIN / tay |

**Nguồn thật của mốc SHEIN** (kiểm chứng trên 158 payload gốc; Unix giây **UTC**):

| Mốc gửi lên | Trường SHEIN | Cạm bẫy |
|-------------|--------------|---------|
| `sheinShippedAt` | `order_package_info_list[].deliveryTime`, dự phòng `delivery_time` cấp đơn | **Đây mới là mốc rời kho, không phải mốc giao.** 55/57 đơn còn `shipped` vẫn có trường này |
| `sheinDeliveredAt` | `order_package_info_list[].signed_time`, dự phòng `track_info.time` **chỉ khi** `track_info.status_desc` báo đã giao | `signed_time` vắng ở 0/57 đơn `shipped`, có đủ ở 99/99 đơn đã giao, luôn muộn hơn `delivery_time` trung bình 5,9 ngày. Đọc `track_info.time` không kiểm `status_desc` sẽ đánh dấu đã giao cho đơn còn đang trên đường |

Đơn nhiều gói: Transit theo gói rời kho **sớm nhất**, Giao theo gói ký nhận **muộn nhất** và chỉ khi **mọi** gói đã ký nhận.

`Normalize` ép mỗi mốc mềm nằm trong `max(OrderCreateTime, FulfilPurchasedAt) ≤ mốc < DeliveredAt`, **xoá** mốc vi phạm (không gán bằng nhau) ⇒ UI không còn gap `—` / `<1m` giả. **Không** ràng buộc `TrackingAddedAt ≤ InTransitAt`: Track (mã lên sàn, clock 4Seller) và Transit (rời kho, clock SHEIN) là hai sự kiện độc lập ở hai hệ thống — 49/153 đơn (32%) có hàng rời kho **trước** khi mã lên sàn. Chỉ `InTransitAt < DeliveredAt` là luật cứng (`delivery_time < signed_time` ở 112/112 payload gốc có cả hai). `Normalize` trả về danh sách mốc bị xoá kèm lý do và sync 4Seller log `Warning` cho từng mốc, vì xoá mốc là mất clock thật. KPI: đặt→nhận = `DeliveredAt − OrderCreateTime`. Tab UI “Đã giao” = 4Seller `shipped` — **khác** `DeliveredAt` (khách nhận).

**Chẩn đoán payload gốc:** setting `extension.shein_sync.capture_raw_payload` (bool) bật thì `POST /admin/orders/shein-sync` nhận thêm `rawListItem` / `rawDetail` (JSON gốc, cắt ~256KB) và lưu vào `SupplierPurchases.RawPayload` dạng `{capturedAt, listItem, detail}` (hai phase gộp vào cùng một vỏ, phase sau **không** xoá mảnh của phase trước); `extension.shein_sync.force_billnos` (danh sách cách nhau dấu phẩy, tối đa 50) ép lấy lại chi tiết cho billno cụ thể kể cả đơn đã kết thúc. Chỉ bật khi cần chẩn đoán vì làm phình payload mỗi lượt sync. Nhóm endpoint `GET|POST /api/v1/dev/diagnostics/*` (**chỉ Development**, ngoài Development trả 404) đọc payload đã lưu: `order/{platformOrderId}`, **`after-sales/{platformOrderId}?maxPages=`** (đối chiếu trạng thái hoàn local với **case return thật**: đọc `POST /api/returns/tiktok/page` bằng cookie của đúng owner đơn, **không watermark** nên thấy cả case pull incremental đã đi qua; trả local `{ platformOrderStatus, refundedAt, cancelReason, workflowTab }` + mọi case khớp `erpOrderId`/`platformOrderId` kèm `platformReturnStatus` + `classified` + `platformCreateTime`/`platformUpdateTime`, và `latest` = case quyết định trạng thái. Đây là chỗ duy nhất phân biệt được ba nguyên nhân `REFUND` thiếu clock: case **còn mở**, case **đã đóng không hoàn**, hay case **hoàn nhưng payload không mang mốc nào** — endpoint 4Seller chỉ nhận POST + cookie seller nên không gọi tay từ browser được), `captured-links`, `shein-raw-analysis/{billno}`, `shein-raw-path/{billno}?fragment=&path=`, `shein-raw-survey`, `rerun-shein-timeline-backfill`, `shein-extract/{billno}` (chạy `SheinRawTimelineExtractor` trên payload đã lưu — tách bạch "payload không có clock" với "bộ trích xuất đọc không ra"), **`shein-sync-runs`** (20 lượt Sync Order gần nhất gộp theo `runId`: fetched/matched, histogram skip reason + needs-detail reason, cờ `listBalanced`/`listUnaccounted` cho bất biến `matched + unchanged + skip == fetched`), **`shein-sync-runs/{runId}`** (chi tiết một lượt: từng lô, từng billno bị bỏ qua kèm lý do + chi tiết, từng billno bị bắt gọi detail kèm điều kiện đã bật — đây là nơi đọc ra "billno nào không khớp pool TikTok" thay vì suy từ số tổng), **`shein-match-explain?phone=&zip=&name=&mode=&lookbackDays=`** (giải thích một ca `no_local_match`: dựng lại đúng pool của luồng sync rồi xếp đơn sàn thành `exactMatches` / `phoneMatchZipMismatch` / `zipMatchPhoneMismatch` kèm `verdict` bằng tiếng Việt và khoá đã chuẩn hoá của từng ứng viên. Billno chưa khớp thì chưa có link nên payload SHEIN **không** nằm trong DB — phải tự đọc phone/zip trên trang đơn SHEIN rồi truyền vào. Dùng chung `Application.Services.SheinMatchKeys` với luồng sync nên kết quả không thể nói khác thực tế), `track-transit-order` (phân bố thứ tự thật giữa mốc Track và Transit + nguồn từng mốc — dùng để kiểm chứng bằng số liệu thay vì suy luận), `shein-probe/{accountEmail}?billno=&page=&limit=` (gọi thật `order/list` hoặc `get_order_info` bằng session + proxy đã lưu trên tài khoản fulfil — dùng để xác nhận SHEIN chấp nhận request từ .NET trước khi bật `headerSource=local`), **`supplier-purchases-summary`** (đếm `SupplierPurchases` theo `source`/`matchState`/`isCharged`, histogram `skipReason` của dòng chưa gắn, và ba đối chiếu: `chargedTotal` vs `ledgerTotal` (`ledgerDelta` ≠ 0 ⇒ có lần mua tính tiền mà sổ cái trống), `ledgerUnattributed` (dòng CostUpdate không trỏ lần mua nào), **`chargedDriftCount`** — chạy lại `OrderSupplierCost.SelectCharged` bằng C# trên dữ liệu thật rồi so từng dòng với cờ `IsCharged` đang lưu, vì cờ đó do migration tính bằng SQL và hai bản dịch của cùng một luật lệch nhau là cách giá mua sai âm thầm).

### Đồng bộ SHEIN (Extension Realtime)

Sync Order **luôn** chạy trên extension (Realtime): Capture → fetch `order/list` trên trình duyệt (**Auto nông**: pageSize 20 × 1 trang ≈ 20 đơn; **Auto mỗi 10 chu kỳ (`auto_full`)**: full lookback + `forceFullPost`; **Sync thủ công mode `all`**: lấy hết list, không cắt lookback, trần 60 trang; **Sync thủ công mode `new`**: list cắt theo Lookback, pageSize 50) — extension chọn cửa sổ qua `resolveSheinSyncPlan` (một pipeline list→detail cho cả 3 chế độ) → so fingerprint với snapshot `chrome.storage` (**Auto nông**: không đổi **và** không còn incomplete cost → bỏ POST; có đổi → chỉ POST đơn mới/đổi; **auto_full**/Manual: POST đủ list; billno thiếu giá **không** vào snapshot) → (bổ sung Shipping nếu list thiếu phone/zip) → phase list (**strip** subtotal/coupon/wallet/tax/club/points/retail — chỉ gửi `total`+`shippingFee`) → `get_order_info` **luôn từ tab My Orders** (`ensureSheinOrdersListTabForDetail`: tab list sẵn có / navigate tab nền / tạo tab nền — không phụ thuộc user đang xem PDP) cho `needsDetailBillnos` (**gồm `first_seen`**) + `incompleteCostBillnos` + incomplete local (stub) + `unmatchedBillnos` (rematch) → phase detail (Shipping overwrite; **cost queue** chỉ POST khi rich/`detailCostProven` hoặc subtotal từ detail thật; tracking-only vẫn sparse). Incomplete cost: tối đa **10 lần / TTL 7 ngày**. Auto refresh capture khi thiếu hoặc cũ hơn ~30 phút. Extension **không** dùng `force_billnos` trong Sync Order. `headerSource` cố định `realtime`. Job nền Local không còn đường vận hành.

**Setting `extension.shein_sync.update_mode` chỉ điều khiển nút Sync Order thủ công** (cả cửa sổ list SHEIN lẫn luật backend). Auto Sync luôn gửi `updateMode=new` bất kể Setting: lịch `chrome.alarms` trên background extension (chạy cả khi đóng side panel; chu kỳ từ Settings, sàn ~30s), mỗi nhịp là lượt kiểm tra nhanh (list nông page 20 + snapshot diff), để mode `all` chi phối thì mỗi nhịp auto lại kéo full list và rematch cả pool.

| Chế độ | Pool khớp | Hành vi |
|--------|-----------|---------|
| `new` (mặc định) | Đơn đã gắn billno của lô **∪** fulfil đang chờ trong cửa sổ lead từ mốc mua sớm nhất (`ComputeSheinMatchPoolSince` = `max(14, lookback)`, trần 90 ngày); lọc theo `FulfilAccountId` của email billing SHEIN. Query id trước rồi Include Shop/Items/SupplierPurchases chỉ cho tập đó | Extension (thủ công): list cắt Lookback; Auto: nông 20 hoặc deep lookback. Backend: **billno lần đầu** → `needsDetail` (`first_seen`) + skip tạm `awaiting_detail` (**hoãn khớp** đến phase detail — tránh gắn Club/sparse từ list); sau detail mới gắn hoặc `no_local_match`; đơn cũ chỉ cập nhật khi có delta tracking/status/total/milestone; billno đang thuộc đơn khác → `duplicate_billno` |
| `all` | Mọi đơn trong cửa sổ lead từ mốc mua (kể cả huỷ/hoàn), **không** lọc `FulfilAccountId` | Extension: lấy hết list SHEIN. Backend: khớp lại phone+zip rồi họ tên; billno Refunded/Canceled **gỡ** khỏi đơn (`terminal_unlinked`) và **không** gắn mới; phân giải ambiguous bằng tên + `OrderCreateTime`↔`pay_time`; **thu hồi** billno gắn sai rồi gắn lại; mọi billno đã khớp (còn hiệu lực) vào `needsDetailBillnos` (`mode_all`) |

**Cửa sổ Lookback không còn quyết định được billno nào bị mất.** `refresh_lookback_days` giới hạn: (1) list SHEIN của Sync thủ công mode `new` và Auto deep, (2) số ngày lùi tối thiểu từ mốc mua sớm nhất khi dựng pool (`max(14, lookback)`, trần 90) — dùng cho **cả** mode `new` lẫn `all`. Billno ngoài pool vẫn được **ghi nhận đầy đủ** vào `SupplierPurchases` với `skipReason=no_local_match`. Auto nông (page 20) không cắt lookback vì chỉ lấy trang đầu.

`SupplierPurchases.SupplierOrderNo` có unique index toàn DB (filtered `WHERE ... IS NOT NULL` — dòng `manual` không có billno nên được phép null) nên một billno chỉ thuộc đúng một dòng. Fast path theo billno tra thẳng `SupplierPurchases` (không qua bộ lọc pool/tài khoản) để dòng đang giữ billno luôn tìm thấy được, kể cả dòng `OrderId = null`. Query pool có `OrderBy` tường minh nên hai lượt cùng dữ liệu cho cùng kết quả khớp. `SaveChangesAsync` bọc `DbUpdateException` → lô đó trả `notes` cảnh báo **và** `saveFailed=true`, các counter `matched`/`updatedCost`/… reset về 0 (báo thành công cho lô không ghi được là cách chỉ số sync nói dối).

Detail gọi `get_order_info` cho `needsDetailBillnos` (gồm **`first_seen`** — billno lần đầu, kể cả chưa khớp — + link mới / cost / tracking / `mode_all`) + `incompleteCostBillnos` (đơn **đã khớp** thiếu subtotal/total; incomplete local retry cả billno ngoài list nông) và `unmatchedBillnos` chỉ khi list **thiếu** `shipping_telephone`/`shipping_postcode` (rematch). Cost queue POST detail khi breakdown **rich** (`detailCostProven` từ get_order_info — component giảm giá/retail/tax **hoặc** sub+total sau detail) — **không** POST sparse chỉ vì tracking/status. Báo cáo `detail phí X/Y` = rich cost cho đơn đã khớp, không đếm rematch unmatched. List thiếu phone/zip được bổ sung Shipping trước phase list. Song song theo `concurrentThreads`, tối đa 200 billno/tài khoản/lượt — **không** kéo open-set từ DB. Setting `force_billnos` còn trong Settings/config API nhưng **extension Sync Order không đọc**.

## Admin Fulfil Endpoints

Quản lý tài khoản mua hàng, thẻ Visa/Master, và lịch sử giao dịch fulfil. Permissions: `admin.fulfil.view` / `admin.fulfil.manage`. UI lịch sử giao dịch dùng shared `@drop/fulfil` (`ADMIN_FULFIL_TX_CONFIG`); FE: `createFulfilApi({ basePath: '/admin/fulfil' })`. Seller mirror: xem mục **Seller Fulfil Endpoints**.

`FulfilAccountDto` trả thêm trạng thái session SHEIN — `sheinOrigin`, `sheinHeaderCount`, `sheinHasCookie`, `sheinSessionReady`, `sheinMissingHeaders[]`, `sheinSessionCapturedAt`, `sheinSessionLastCheckedAt`, `sheinSessionLastError` (và `sheinProxy` legacy nếu còn trong DB, không cấu hình trên UI). **Không** trả header/cookie thô, chỉ trả đủ để admin biết tài khoản nào cần mở trình duyệt capture lại.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/fulfil/accounts` | `admin.fulfil.view` | List tài khoản fulfil |
| GET | `/admin/fulfil/accounts/options` | `admin.fulfil.view` | Options dropdown (active) — gồm `id`, `name`, `email`, `activeCardId` (extension map email SHEIN) |
| GET | `/admin/fulfil/accounts/{id}` | `admin.fulfil.view` | Chi tiết tài khoản |
| POST | `/admin/fulfil/accounts` | `admin.fulfil.manage` | Tạo tài khoản (`assigneeUserIds[]`; username/email unique case-insensitive nếu có) |
| PUT | `/admin/fulfil/accounts/{id}` | `admin.fulfil.manage` | Cập nhật tài khoản (`assigneeUserIds[]`; username/email unique) |
| DELETE | `/admin/fulfil/accounts/{id}` | `admin.fulfil.manage` | Xóa; 400 nếu còn giao dịch (`Không thể xóa tài khoản đang có lịch sử giao dịch.`). Nếu không còn txn: gỡ `FulfilAccountId`/`FulfilCardId` trên `Order` + `SupplierPurchase` rồi xóa (không chặn vì đơn đã từng gắn). |
| PUT | `/admin/fulfil/accounts/{id}/active-card` | `admin.fulfil.manage` | Đổi thẻ active (`{ cardId }`, null = clear); chỉ 1 thẻ active / account |
| POST | `/admin/fulfil/accounts/shein-session` | `admin.orders.manage` | Lưu session SHEIN `{ accountEmail?, accountId?, origin?, headers{}, cookie, capturedAt? }` lên `FulfilAccounts`. **Extension gọi mỗi lượt Sync Order** (manual + Auto) ngay sau khi `order/list` chạy được — đó là lúc duy nhất chắc chắn cookie còn sống; throttle theo hash `headers+cookie`, đẩy lại khi hash đổi **hoặc** quá 5 phút (`capturedAt` phải tươi vì vòng quét nền bỏ session >12h). Payload kèm `headers['user-agent']` = UA thật của trình duyệt: Cloudflare khoá `cf_clearance` theo IP + UA, lệch UA là ăn trang xác minh. Match email/accountId; sanitize header allowlist (`SheinSessionKeys.DeniedHeaders`). Trả `{ accountId, accountName, …, ready, missingHeaders[] }` |
| GET | `/admin/fulfil/accounts/{id}/shein-session` | `admin.fulfil.manage` | Session đầy đủ **kèm cookie + header thô**: `{ accountId, accountName, origin, cookie, headers{}, userAgent, proxy, capturedAt, lastCheckedAt, lastError, missingHeaders[], ready }`. Endpoint **riêng**, cố tình không nhồi vào `GET /accounts`: cookie ~16 KB/tài khoản mà list load mỗi lần vào trang. UI chỉ gọi khi bấm **Xem cookie/header** |
| PUT | `/admin/fulfil/accounts/{id}/shein-session` | `admin.fulfil.manage` | Dán tay ghi đè session: `{ origin?, cookie?, headers?{}, headersText?, userAgent? }`. `headersText` = khối `name: value` từng dòng (DevTools → Copy request headers); parser bỏ dòng rỗng/`#`, pseudo-header HTTP/2 (`:authority`), dòng request (`GET … HTTP/2`) và header trong `DeniedHeaders`; `headers{}` (nếu có) thắng `headersText`. **Cookie + headers rỗng cả hai = xoá session** (cách bỏ một session hỏng mà không xoá tài khoản). Ngược lại bắt buộc đủ 4 header lõi + cookie, nếu không trả 400 |
| POST | `/admin/fulfil/accounts/{id}/shein-session/test` | `admin.fulfil.manage` | Probe session: gọi `get_order_info` với billno mới nhất của chính tài khoản (không có đơn → `order/list` page 1 limit 1) qua đúng proxy của tài khoản. Ghi `sheinSessionLastCheckedAt`; ghi `sheinSessionLastError` **chỉ khi** SHEIN từ chối session (`sessionInvalid`). `proxyFailed` trả trên DTO lần test (toast "Proxy lỗi") nhưng **không** nhuộm badge tài khoản. Trả `{ ok, error, sessionInvalid, proxyFailed, billno, elapsedMs }`. Admin UI **Kiểm tra session** (header `/fulfil/accounts`) gọi endpoint này song song tối đa 10 cho các tài khoản `sheinSessionReady` — không có bulk API riêng. |
| GET | `/admin/fulfil/cards` | `admin.fulfil.view` | List thẻ (`?accountId=`) |
| GET | `/admin/fulfil/cards/options` | `admin.fulfil.view` | Options dropdown (`?accountId=`) |
| GET | `/admin/fulfil/cards/{id}` | `admin.fulfil.view` | Chi tiết thẻ |
| POST | `/admin/fulfil/cards` | `admin.fulfil.manage` | Tạo thẻ (`sourceId` bắt buộc; `sourceAccountId` bắt buộc khi nguồn `supportsAccounts`; `currency` mặc định USD; `accountIds[]`, `assigneeUserIds[]`; không gắn shop — chọn account/card khi fulfil đơn) |
| PUT | `/admin/fulfil/cards/{id}` | `admin.fulfil.manage` | Cập nhật thẻ (`sourceId` / `sourceAccountId` giống create) |
| DELETE | `/admin/fulfil/cards/{id}` | `admin.fulfil.manage` | Xóa; 400 nếu còn giao dịch. Nếu không còn txn: gỡ `FulfilCardId` trên `Order` + `SupplierPurchase` rồi xóa. |

**Import hàng loạt thẻ (UI admin `/fulfil/cards`):** không có bulk endpoint — FE parse Excel export thẻ ảo PingPong rồi gọi lần lượt `GET /admin/fulfil/cards` (snapshot so trùng) + `POST` / `PUT` ở trên. Upsert theo **số thẻ chỉ digits**; trùng nhiều bản → `PUT` mọi id. `sourceId` + currency fallback chọn trên modal; `sourceAccountId` khớp cột **Account Name** (fallback lựa chọn modal); `cardBrand` suy từ BIN. Bản cập nhật gửi lại `accountIds` / `assigneeUserIds` hiện có để không mất liên kết.
| GET | `/admin/fulfil/transactions` | `admin.fulfil.view` | List giao dịch **phân trang** (`pageCurrent` mặc định 1, `pageSize` mặc định 100 / max 200; **UI Lịch sử mua hàng mặc định 50**/trang) + filter `accountId`, `cardId`, `sourceId`, `sourceAccountId`, `orderId`, `shopId`, `sellerId`, `paidByUserId`, `keyword`, `from`, `to`. Trả `{ items, total, pageCurrent, pageSize, summary }`. `items` chỉ trang hiện tại. Cột UI **Fulfill** = `paidByUserName` (người mua hàng — không dùng `createdByUserName`); bộ lọc **Người fulfil** trên UI gửi `paidByUserId` (cùng filter với `summary`, áp dụng khi bấm Tìm kiếm). `summary` = KPI **cùng filter**: `orderCount` = số `OrderId` khác nhau; `cardAmount` = Σ cột **Thẻ** (Debit − Credit của `amount`, khớp totalPrice hóa đơn / sao kê — **không** gồm BuyTrack/ThankCard); `costAmount` = Σ gross NCC CostUpdate (thẻ+ví+coupon+club+points) **+ BuyTrack/ThankCard 1 lần/đơn**; **`buyTrackAmount`** / **`thankCardAmount`** = Σ phí xử lý cấp đơn 1 lần/đơn (từ `Orders`, không quẹt thẻ); `couponAmount` / `walletAmount` / `membershipAmount` (Club) / `pointsAmount` = Σ snapshot **từng lần mua**; `profitAmount` = Σ Profit **1 lần / `OrderId`** (Revenue − `Orders.totalCost` = gross NCC Charged* + BuyTrack + ThankCard — **không** Σ `profitAmount` từng dòng ledger; joint shipment / đơn TikTok mua nhiều tài khoản Shein vẫn chỉ cộng một lần). Mỗi item CostUpdate đọc **snapshot cột DB**: `amount` = **SupplierTotal** (totalPrice hóa đơn / phần thẻ — đối chiếu sao kê); `walletAmount` tách riêng; **`purchaseCostAmount`** read-model = gross NCC (ưu tiên breakdown `SupplierPurchase`: subtotal+ship+tax+…; fallback amount+wallet+coupon+club+points) **+ BuyTrack + ThankCard**; **`trueSpendAmount`** = amount + walletAmount + membershipAmount (Chi thật — preview Mua hàng); **`buyTrackAmount` / `thankCardAmount`** read-model từ Order (cùng giá trị trên mọi dòng của đơn; UI cột Track/Thank); **`supplierShippingFee` / `supplierTax`** read-model từ SupplierPurchase gắn dòng (UI cột Ship/Tax); `membershipAmount` / `pointsAmount` snapshot giảm; `couponAmount`; `revenueAmount`; `profitAmount` = **Revenue − Cost** (= `Orders.totalCost` = gross NCC + BuyTrack + ThankCard). Manual: snapshot tiền null. Đơn Total=0 + Coupon&gt;0 + Wallet=0 → **wallet_cost_stale** (xin lại detail). `keyword` quét thêm billno |
| GET | `/admin/fulfil/transactions/stats` | `admin.fulfil.view` | Thống kê chi tiêu thẻ (cùng filter trừ `orderId`): KPI `totalAmount` = Debit−Credit; `debitAmount` / `creditAmount`; `orderCount`; series ngày **Asia/Ho_Chi_Minh**. Query `breakdown=card\|account\|shop\|paidBy` (mặc định `card`) — **chỉ tính/trả 1 chiều** breakdown (các list còn lại `[]`); projection scalar (không Include full graph). `byCard.name` = `Nguồn · Tài khoản nguồn` (fallback tên thẻ), `byCard.extra` = masked · currency · TK fulfil |
| GET | `/admin/fulfil/transactions/{id}` | `admin.fulfil.view` | Chi tiết giao dịch |
| POST | `/admin/fulfil/transactions` | `admin.fulfil.manage` | Tạo giao dịch thủ công (`Source=Manual`); `fulfilAccountId` / `fulfilCardId` optional; `direction` = `Debit` (trừ tiền, mặc định) \| `Credit` (nạp tiền) |
| PUT | `/admin/fulfil/transactions/{id}` | `admin.fulfil.manage` | Sửa giao dịch (CostUpdate: không đổi OrderId, luôn `Debit`; `fulfilAccountId` / `fulfilCardId` optional); Manual: `fulfilAccountId` / `fulfilCardId` optional; `direction` Debit/Credit |
| DELETE | `/admin/fulfil/transactions/{id}` | `admin.fulfil.manage` | Xóa bất kỳ giao dịch (Manual hoặc CostUpdate). **CostUpdate** → **xóa sạch** lần mua gắn với dòng chi (`SupplierPurchases` shein lẫn manual), tính lại giá mua / mốc SHEIN còn bằng chứng; đơn **hết sạch** CostUpdate → clear `FulfilAccountId` / `FulfilCardId` / `PaidByUserId` (+ mốc `FulfilPurchasedAt` khi hết giá mua). Joint Shipment giữ account/card khi còn dòng anh em. Lượt sync SHEIN sau Upsert + khớp lại billno như lần đầu (không còn khóa `ignored`/`ledger_deleted`) |
| POST | `/admin/fulfil/transactions/bulk-delete` | `admin.fulfil.manage` | Xóa nhiều giao dịch — body `{ ids: Guid[] }`; trả `{ deleted }`; CostUpdate → xử lý dòng mua như DELETE đơn lẻ |

**Chuyển dòng sổ cái sang đơn khác** (nút Chuyển đơn trên `/fulfil/transactions`, admin + quyền `admin.orders.manage`): dòng **CostUpdate** không đi qua `PUT` (endpoint chặn đổi `orderId` cho nguồn này) mà gọi `POST /admin/supplier-purchases/{orderSupplierLinkId}/attach` — chuyển đơn cho dòng tiền **là** gắn lại lần mua NCC, nếu chỉ đổi `orderId` của dòng tiền thì giá mua ở lại đơn cũ còn tiền sang đơn mới. Dòng **Manual** (không có `orderSupplierLinkId`) vẫn đi `PUT /admin/fulfil/transactions/{id}`. Dòng CostUpdate mất dấu lần mua (dữ liệu cũ) bị chặn thao tác này.

`FulfilTransactionDto` có **`supplierPurchaseId`** (tên cũ `orderSupplierLinkId`) và **`manualLinkedAt`** (mốc gắn tay của lần mua — UI hiện ổ khoá cạnh nhãn nguồn, báo lượt sync SHEIN sau không tự chuyển đơn nữa); `supplierOrderNo` / `supplierOrderUrl` / **`supplierStatus`** / **`supplierStatusRaw`** / **`trackingNo`** / **`carrier`** / **`trackingKind`** lấy từ **dòng mua của chính dòng tiền** — mỗi dòng sổ cái hiện đúng billno đã bị trừ tiền kèm trạng thái SHEIN đã chuẩn hóa (`processing`/`shipped`/`delivered`/`canceled`/`refunded`; raw ưu tiên `packageState` như `common_delivered`) và mã vận đơn / carrier last-mile (USPS/FedEx/…; `trackingKind` = `none`|`warehouse`|`carrier`). Keyword search cũng khớp `trackingNo` / `carrier` (và `Order.TrackingNo` / `ShippingService` khi đã Mark Ship). Hover mã đơn trên `/fulfil/transactions` mở preview **chỉ của billno đó** (khớp 1-1 số tiền trên dòng, không hiện khối gộp).

**Hai lớp tiền Shein (đừng gọi lẫn “Cost”):**

| Lớp | Field | Nghĩa | UI |
|-----|-------|--------|-----|
| Thẻ | `amount` / `supplierTotal` | totalPrice hóa đơn — đối chiếu sao kê thẻ | Cột **Thẻ** sổ cái |
| **Cost (mua hàng + xử lý)** | **`purchaseCostAmount`** | gross NCC (subtotal+ship+tax+…, không trừ coupon/ví/club/points) + BuyTrack + ThankCard | Cột **Cost** + KPI Cost |
| Chi thật (TrueSpend) | `trueSpendAmount` / preview `costAmount` / `ChargedTrueSpend` | thẻ + ví + club | header **Mua hàng** (preview) |
| BuyTrack / ThankCard | `buyTrackAmount` / `thankCardAmount` (Order + DTO sổ cái) | phí xử lý khi stamp USPS / TKS — **không** quẹt thẻ | cột **Track** / **Thank** + KPI header (đã gồm trong Cost) |
| Ship / Tax | `supplierShippingFee` / `supplierTax` (SupplierPurchase + DTO sổ cái) | breakdown gross NCC theo lần mua | cột **Ship** / **Tax** (đã gồm trong Cost) |
| Cost đơn (`totalCost`) | `Orders.totalCost` / card Cost / Profit | gross NCC + BuyTrack + ThankCard (snapshot `Charged*`, lùi về SUM live dòng `IsCharged` khi snapshot trống); **Profit = Revenue − Cost** | Orders card Cost/Pft; Profit sổ cái |

UI **Mua hàng** (Orders drawer + Fulfil hover) dùng chung `@drop/ui` `SupplierPurchaseSection`: hiện đủ field có dữ liệu (Retail / Subtotal / Ship / RDF / On-Time / Coupon / Club / Points / Wallet / Tax); header Orders = TrueSpend (`costAmount`); header Fulfil hover = tiền thẻ (`supplierTotal` per billno, khớp cột Thẻ); không lặp Thẻ/Cost trong grid.

⚠️ **Sổ cái CostUpdate tách theo lần mua — không còn nhánh "dòng cấp đơn".** **1 dòng / `SupplierPurchase` được tính tiền** (`Amount` = `purchase.SupplierTotal`, `OccurredAt` = `SupplierPaidAt ?? OrderedAt ?? FulfilPurchasedAt`; mốc **trừ tiền** chứ không phải mốc tạo đơn, vì SHEIN cho tạo đơn trước rồi mới thu tiền nên ghi theo `OrderedAt` sẽ lệch với sao kê thẻ), tổng các dòng = `SUM(SupplierTotal) WHERE IsCharged`. Phí nhập tay cũng là một lần mua (nguồn `manual`) nên đi cùng đường ghi này. Tập được tính tiền lấy thẳng từ cờ `IsCharged`, nên sổ cái và giá mua hiển thị **không thể lệch**: hai bên đọc cùng một cột. Dòng mua mất total, bị gỡ khỏi đơn, hoặc bị thay bằng lần mua lại còn hiệu lực → xoá dòng chi; billno `refunded`/`canceled` **không** giữ trong sổ cái (tiền đã hoàn). **Quy kết thiếu vẫn ghi dòng chi**: email billing SHEIN không map `FulfilAccounts` nào thì dòng vẫn được tạo (`fulfilAccountId`/`fulfilCardId` null, ghi chú *"chưa rõ tài khoản mua hàng"*) để người đi đơn gán lại — bỏ qua ở đây từng là đường thứ hai làm "đã mua mà không có trong sổ cái". Unique: 1 CostUpdate / lần mua.

Đợt bảo trì một lần `SupplierPurchaseLedgerBackfill` (`maintenance.supplier_purchase_ledger_rebuild_v1`) dựng lại dòng chi cho đơn có lần mua tính tiền mà sổ cái trống — dữ liệu cũ không tự sửa được vì `HasSheinIncrementalDelta` không thấy delta nên lượt sync kế tiếp bỏ qua, và mode `all` chỉ quét cửa sổ Lookback. Nó gọi thẳng `IOrderSyncService.RebuildCostUpdateLedgerAsync` thay vì viết SQL riêng: một bản dịch thứ hai của luật dựng sổ cái là một chỗ nữa để lệch.

`UpdateOrderCostRequest` mở rộng: `fulfilAccountId`, `fulfilCardId`, `paidByUserId` (**người mua hàng**; null → **giữ** giá trị cũ, chỉ fallback current user khi đơn chưa có người mua), `fulfilPurchasedAt` (optional UTC) + breakdown NCC (`supplierOrderNo`, `supplierRetailPrice`, `supplierSubtotal`, `supplierShippingFee`, `supplierRetailDeliveryFee`, `supplierOnTimeGuarantee`, `supplierCouponPrice`, `supplierMembershipDiscount`, `supplierPointsDiscount`, `supplierWalletPrice`, `supplierTax`, `supplierTotal`). Khi có (account hoặc card) + totalCost → upsert `FulfilTransaction` (`Source=CostUpdate`, `OccurredAt` ≈ `FulfilPurchasedAt`; thiếu thẻ → dùng `ActiveCardId` nếu có). Thanh toán tiền về shop dùng endpoint riêng `PUT /admin/orders/{id}/settlement` (`isSettlementPaid` → upsert/xóa `ReceivingBankTransaction` SettlementPaid).

**SHEIN Sync Order = Sync Fulfil** (`POST /admin/orders/shein-sync`) — sở hữu tracking **NCC** trên `SupplierPurchases` (warehouse local-only; carrier → Auto Mark Ship). Sync Order 4Seller (`POST /admin/orders/sync`) seed `TrackingNo`/`ShippingService` khi payload có mã **carrier** (bỏ qua warehouse) và stamp `TrackingAddedAt` từ `OrderShippedTime`. Extension gửi `{ phase, orders[] }` với `trackingNo`, `carrier`, các mốc thật per-chặng và breakdown. **Map giá SHEIN → `Supplier*`:** `supplierSubtotal` = UI Subtotal; `supplierWalletPrice` = `usedWalletPrice.amount` (thanh toán ví cashback, **không** phải coupon/membership); `supplierRetailDeliveryFee` = `retail_delivery_fee.amount` (tách khỏi ship); coupon/membership/points reconcile identity (`coupon + club + points ≈ subtotal + ship + retailDelivery + onTime + tax − total − wallet`) — `wallet` và `pointsDiscount` trừ khỏi ngân sách trước, phần còn lại chia coupon/club để tách ship FREE bị gộp vào cả hai field API; `supplierShippingFee` = phí đã tính (FREE = 0). Phase **list** extension **strip** breakdown tiền (không gửi subtotal/coupon/wallet…) để tránh “cost giả đủ” từ list invert. Phase **detail** chỉ full-replace cost khi breakdown **rich** (có retail/tax/coupon/club/points/wallet/retailDelivery **hoặc** `subtotal > 0 && total > 0` kèm `detailCostProven` từ get_order_info) **và** (`Total > 0` hoặc free-order tường minh); không ghi `SupplierTotal = 0` từ `Total` null. Payload sparse merge từng field, bỏ qua 0 placeholder. Response thêm **`incompleteCostBillnos[]`** (matched nhưng `SupplierSubtotal`/`SupplierTotal` còn null) — extension đưa vào hàng đợi `get_order_info` (`kbt_fulfil_shein_incomplete_billnos`, tối đa **10 lần / TTL 7 ngày**, cap 100) và hiện ` · chờ giá N`. Sổ cái CostUpdate bỏ qua link `SupplierTotal = 0` khi chưa có `SupplierSubtotal` (tránh dòng giả).

⚠️ **Giá mua của đơn:** breakdown từng billno ở `SupplierPurchases`; tổng IsCharged **materialize** vào `Orders.Charged*` lúc `OrderSupplierCost.ApplyAggregates` (cùng chỗ ghi `IsCharged`). **TrueSpend** = `ChargedSupplierTotal + ChargedSupplierWalletPrice + ChargedSupplierMembershipDiscount` (= `ChargedTrueSpend`) — Chi thật (thẻ + ví + club; club cộng vì đã mua Membership). **`totalCost` / Profit** = gross NCC (`ChargedSubtotal+Ship+Tax+…` hoặc fallback ledger) + `BuyTrackAmount` + `ThankCardAmount`; **Profit = Revenue − totalCost**. `SupplierTotal` / `ChargedSupplierTotal` riêng là phần thẻ. List đơn đọc cột Charged* (fallback SUM nếu chưa backfill). Luật chọn: dòng `manual` **có tiền** thắng → còn không thì billno còn hiệu lực (không `refunded`/`canceled`, không `SupplierUnpaid`); `ignored` bị loại. Billno huỷ/hoàn — sync gỡ (`terminal_unlinked`) và không tính sổ cái. Đơn đã mua mà thiếu giá vì thế còn: chưa gắn được billno còn hiệu lực (xem `/fulfil/supplier-purchases`), mọi billno đều terminal/ghost, hoặc chỉ còn dòng ghost chưa thanh toán. Muốn đối chiếu 1-1 với UI SHEIN phải xem từng `SupplierPurchase`; **hợp đồng JSON giữ nguyên tên field** (`supplierTotal`, `supplierSubtotal`, `supplierLinks[]`, …) nên frontend không đổi — chỉ nguồn tính bên trong đổi. Preview trả `supplierOrderCount` / `supplierLinks[]` (mỗi phần tử thêm `isCharged` + `source`) (**`costDetailComplete`** derived = subtotal+total đều có) để UI phân biệt null («—» / badge «Thiếu order detail») với `$0` thật, và sổ cái `FulfilTransactions` cũng tách 1 dòng / billno. Mỗi link mang `FulfilAccountId` / `FulfilCardId` / `PaidByUserId` riêng (billno anh em có thể mua từ tài khoản SHEIN khác nhau). Preview/drawer chỉ hiển thị DB. Phase **list** chỉ match + trả `needsDetailBillnos`; phase **detail** lấy `get_order_info`, ưu tiên last-mile carrier thay mã kho `C…`/`N…`, lưu tracking thô trên `SupplierPurchase`, rồi tự gọi đúng luồng 4Seller Mark Ship **chỉ khi** đơn còn `AWAITING_SHIPMENT` **và chưa có** `Order.TrackingNo` (kể cả joint vừa nhận carrier tracking từ SHEIN); đơn đã có track thì **không** Mark Ship lại (tối đa **2** remote / request, ngân sách ~**45s** — tránh Cloudflare **524**). Carrier chỉ được map với Shipping Service 4Seller theo chuỗi chuẩn hóa khớp chính xác (không fuzzy/fallback); không khớp ⇒ trạng thái `blocked` và yêu cầu Mark Shipped thủ công. Thành công mới publish `Order.TrackingNo`/`ShippingService`, đổi status `AWAITING_COLLECTION` và stamp Track bằng clock SHEIN/source `shein`. Trạng thái `AutoMarkShip*` trên dòng mua chống gọi trùng; lỗi transient retry sau cooldown 15 phút. Remote 4Seller **thành công nhưng poll hết hạn** → `MarkShipUnconfirmedException` → trạng thái `blocked` (không retry): thử lại có thể publish tracking lần thứ hai cho một Mark Ship đã đi qua. Response bổ sung `autoShipped`, `autoShipFailed`, `autoShipBlocked`, `incompleteCostBillnos`; lỗi từng đơn không làm hỏng cả batch. Timeline còn lại ghi qua `OrderFulfilTimeline.Stamp` + `Normalize`, không fallback giờ sync. Sổ cái CostUpdate snapshot thêm `membershipAmount` / `pointsAmount`; **Profit = Revenue − Cost** (gross NCC + BuyTrack + ThankCard).

Admin UI: `/fulfil/accounts`, `/fulfil/cards`, `/fulfil/transactions` (list server-paged; lọc theo seller + shop + tài khoản mua hàng + **người fulfil**; **RangePicker mặc định 30 ngày** inclusive today−29 → today, giờ VN; **mobile**: thẻ gọn, không checkbox/bulk-delete), `/fulfil/supplier-purchases` (hàng đợi đơn NCC chưa gắn); thống kê chi tiêu thẻ tại `/stats/card-spending` (lọc **thẻ** / tài khoản tuỳ chọn / shop qua đơn / người đi đơn + kỳ; gửi `breakdown` theo segmented UI; filter dropdown dùng `/admin/shops/options` + fulfil card/account options; KPI ròng + trừ + nạp + đơn/GD; pie theo trừ tiền; bảng có cột trừ/nạp/ròng).

**Dropdown người fulfil / người mua hàng** (`paidByUserId`) ở Lịch sử mua hàng, `/fulfil/supplier-purchases`, `/stats/card-spending` và modal Cập nhật phí đều gọi `GET /admin/users/options?roles=Admin,Fulfil` — chỉ người được phép đi đơn, không liệt kê Seller. Đơn/dòng cũ gắn người ngoài nhóm đó (hoặc user đã khóa) vẫn được UI thêm option ảo từ `paidByUserName` để không phơi GUID và không âm thầm đổi người mua khi lưu lại.

## Admin / Seller Product Discount & Flash Deal (Promotions)

Mirror activity từ 4Seller `POST /api/promotion/tiktok/activity/get-page` vào bảng `ProductDiscountActivities` (chung cho Product Discount + Flash Deal). List đọc DB local; Sync mới gọi 4Seller với **`promotionStatus=ONGOING`** rồi upsert; sau sync **xóa** row local cùng scope seller + loại (PD / Flash) không còn trong tập ONGOING vừa kéo (gồm DEACTIVATED/EXPIRED/stale). Scope sync: sellers cookie `logged_in`; upsert **chỉ** activity thuộc shop local `Status=Active` (map `FourSellerShopId`). Deactivate: proxy `GET /api/promotion/tiktok/activity/deactivate-activity?id=` (cookie **owner**; referer flash-deal khi `discountType=FLASHSALE`) rồi **xóa** row local — chỉ khi đang `ONGOING`.

- **Product Discount:** sync với `discountType` rỗng (referer product-discount); list mặc định **loại trừ** `FLASHSALE`. FE: **Promotions → Product Discount** (`/promotions/product-discount`).
- **Flash Deal:** sync với `discountType=FLASHSALE` (referer flash-deal); list luôn ép `FLASHSALE`. FE: **Promotions → Flash Deal** (`/promotions/flash-deal`).
- UI shared `@drop/promotions` (`ProductDiscountPage` / `FlashDealPage` + variant); API client `createPromotionsApi` / `createFlashDealsApi`.

| Method | Path | Permission | Mô tả |
|--------|------|------------|--------|
| GET | `/admin/promotions/product-discounts` | `admin.promotions.view` | List paged: `page`, `pageSize`, `shopId`, `ownerUserId`, `promotionStatus`, `discountType`, `search` (activityName). Không truyền `discountType` → loại trừ `FLASHSALE`. Sort `beginTime` desc |
| POST | `/admin/promotions/product-discounts/sync` | `admin.promotions.manage` | **202 + `jobId`**. Body optional `{ ownerUserId }`. Job `IProductDiscountSyncJobRunner` → `ProductDiscountSyncService` (discountType rỗng) |
| GET | `/admin/promotions/product-discounts/sync/status/{jobId}` | `admin.promotions.manage` | Poll job: `state` (`running`\|`completed`\|`failed`), `inserted`/`updated`/`skipped`, `sellers[]` |
| POST | `/admin/promotions/product-discounts/{id}/deactivate` | `admin.promotions.manage` | Deactivate ONGOING Product Discount trên 4Seller rồi **xóa** row local (từ chối row `FLASHSALE`) |
| GET | `/promotions/product-discounts` | `seller.promotions.view` | List của JWT owner (ép `OwnerUserId`) |
| POST | `/promotions/product-discounts/sync` | `seller.promotions.manage` | **202 + `jobId`** — chỉ sync seller hiện tại |
| GET | `/promotions/product-discounts/sync/status/{jobId}` | `seller.promotions.manage` | Poll; 404 nếu job không thuộc user |
| POST | `/promotions/product-discounts/{id}/deactivate` | `seller.promotions.manage` | Như admin; ép ownership JWT |
| GET | `/admin/promotions/flash-deals` | `admin.promotions.view` | List paged Flash Deal (ép `discountType=FLASHSALE`). Cùng query params trừ type |
| POST | `/admin/promotions/flash-deals/sync` | `admin.promotions.manage` | **202 + `jobId`**. Body optional `{ ownerUserId }`; server ép `DiscountType=FLASHSALE` |
| GET | `/admin/promotions/flash-deals/sync/status/{jobId}` | `admin.promotions.manage` | Poll job (chung `IProductDiscountSyncJobRunner`) |
| POST | `/admin/promotions/flash-deals/{id}/deactivate` | `admin.promotions.manage` | Deactivate ONGOING Flash Deal trên 4Seller rồi **xóa** row local (`requiredDiscountType=FLASHSALE`) |
| GET | `/promotions/flash-deals` | `seller.promotions.view` | List Flash Deal của JWT owner |
| POST | `/promotions/flash-deals/sync` | `seller.promotions.manage` | **202 + `jobId`** — sync FLASHSALE cho seller hiện tại |
| GET | `/promotions/flash-deals/sync/status/{jobId}` | `seller.promotions.manage` | Poll; 404 nếu job không thuộc user |
| POST | `/promotions/flash-deals/{id}/deactivate` | `seller.promotions.manage` | Deactivate Flash Deal của mình |

Background: `ProductDiscountSyncBackgroundService` poll ~60s; mỗi lane độc lập theo Settings — Product Discount: `fourseller.product_discount_sync.enabled` / `interval_hours` (mặc định bật / **2 giờ**); Flash Deal: `fourseller.flash_deal_sync.enabled` / `interval_hours` (mặc định bật / **2 giờ**). Chu kỳ của mỗi lane **chỉ bắt đầu đếm sau khi lần sync tự động của lane đó hoàn tất** (không tính từ lúc bắt đầu sync).

## Admin Listings (Draft / Published mirror)

Permissions: `admin.listings.view`, `admin.listings.manage`. Admin UI: sidebar **Quản lý Listings** → menu con **Draft** (`/listings/draft`) / **Published** (`/listings/published`) / **Analytics** (`/listings/analytics`) (card list kiểu OrderCard: header ID/platform/shop + giá + actions; body grid Sản phẩm · Giá/Tồn · Thời gian · Danh mục · Variants · Shop/TT); Published còn sub-tab `siteStatus` (`?siteStatus=`). Edit full-page `/listings/:id/edit` (Card form parity product edit; Save Draft / Publish 4Seller). `/listings` redirect → `/listings/draft`.

**FE shared client:** `createListingApi(http, { basePath: '/admin/listings' | '/listings' })` trong `@drop/api-client`; UI chung `@drop/listings` (`ListingsPage` / `ListingEditPage` / `ListingAnalyticsPage` + `ListingsDepsProvider`). Seller mirror cùng relative paths dưới `/listings`, permission `seller.products.*`, server ép `OwnerUserId` từ JWT.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/listings` | `admin.listings.view` | List listing local (filter `pageType`, `siteStatus`, seller, platform, shop, search, `dateFrom`/`dateTo`; paging; kèm `statusCounts`). **Slim projection:** không đọc `RawPayload` / `DescriptionHtml` / cookie owner; tối đa 5 variation nhẹ/card (không `VariantAttributes`). Item kèm `reviewCount`/`averageRating` + `storeLogo`/`storeTitle`/`storeUrl`/`shopRank`/`storeAttention`/`shippingDays`/`shippingDayPercent`/`sourceStock`/`isLocalProduct`/`isSupportQuickShip`/`sellerPlatform`/`storeCode` + **`hasSizeChart`** (`true` khi `SupplierProductInfos.SizeChartImageUrl` không rỗng — cùng cổng auto publish pick; FE icon đỏ trên header card khi `false`) (join `SupplierProductInfos` → `SupplierSellerInfos`; `storeTitle` trống thì parse từ `soldByTips`) + `addedFrom` (`recommended` \| `manual` \| null — local-only; badge Đề xuất trên ListingsPage khi `recommended`) |
| GET | `/admin/listings/status-counts` | `admin.listings.view` | Aggregate counts từ DB (cùng filter list, bỏ `siteStatus`) |
| GET | `/admin/listings/auto-publish-queue-status` | `admin.listings.view` | **Poll nhẹ badge auto publish** trên Draft list. Query `ids[]` (Guid, tối đa 200). Chỉ draft; dùng `ListingAutoPublishQueueStatusResolver` (FIFO head **toàn shop** khi gán `waiting`/`queue`, khớp dispatcher) — **không** join variations/supplier. Response `{ items[{ id, autoPublishQueueStatus, autoPublishNextPickAt, lastAutoPublishRunStatus, autoPublishSkipped, autoPublishSkipReason, lastAutoPublishRunId }] }`. FE patch cache list 12s khi còn `queue`/`waiting`/`processing` — không reload full list / không spin overlay. |
| GET | `/admin/listings/analytics` | `admin.listings.view` | **Listing analytics** — snapshot TikTok Compass *Product analysis* của **một lần sync (ngày VN)**. **Mỗi dòng = tổng cửa sổ `last7days` Pacific** (`rangeStart`→`rangeEnd`), không phải số một ngày lịch. Query `date` (`yyyy-MM-dd`; bỏ trống = ngày mới nhất trong scope), `ownerUserId`, `shopId`, `q` (product name / `product_id`), `page`/`pageSize` (≤100), `sort` (`gmv` mặc định / `orders` / `impressions` / `clicks` / `ctr` / `atc` / `name`), `sortDir`. Response `{ items, total, pageCurrent, pageSize, snapshotDate, previousSnapshotDate, rangeStart, rangeEnd, lastSyncedAt, totals }` — `totals` cộng cả tập đã filter (CTR = clicks/impressions; **không** SUM snapshot khác ngày). `previousSnapshotDate` = snapshot **cách 7 VN ngày** dùng làm mốc `%` khi Compass thiếu ratio (không phải ngày sync liền trước). Item: `tikTokProductId`, `listingId?` (link khi listing đã mirror), `productName`/`mainImage` (fallback sang listing), `shopProfileName`, khi đã link listing còn `addedFrom`, `reviewCount`, `averageRating`, meta shop NCC (`storeTitle`, `storeUrl`, `shopRank`, `storeAttention`, `shippingDays`, `shippingDayPercent`, `isLocalProduct`, `isSupportQuickShip`), `gmv`/`currency`, `orders`, `impressions`, `clicks`, `ctr` (phân số), `addToCartUnits`/`addToCartBuyers`, các `*Ratio` = **% so kỳ 7 ngày trước cùng độ dài** và `ratioSource` (`tiktok` = ratio Compass/`total_prev`; `computed` = BE so snapshot cách 7 VN ngày; null = không có mốc), `ownerUserName`/`ownerFullName`, `shopName`, `syncedAt`. Chỉ đọc `ListingAnalyticsSnapshots` (không gọi TikTok) |
| GET | `/admin/listings/analytics/dates` | `admin.listings.view` | Danh sách ngày snapshot cho bộ chọn ngày: query `ownerUserId`, `shopId` → `{ dates[], latest }` (≤90 ngày, mới nhất trước) |
| GET | `/admin/listings/analytics/delist-suggestions` | `admin.listings.view` | **Đề xuất hủy list để lấy slot.** Query `date` (bỏ trống = ngày mới nhất trong scope), `shopId`, `ownerUserId`, `limit` (mặc định 12, ≤50), `minScore` (mặc định **và sàn** 50 — chỉ nâng được ngưỡng, để `items` luôn chỉ gồm `delist` ∪ `review`). Chỉ chấm khi scope thu về **đúng 1 shop** (trung vị / slot / keyword là đại lượng riêng shop) — nhiều shop thì `items` rỗng + warning. Chấm bằng `ListingDelistScoring`: *không bán được* 40 (`40 × (1 − √(peakOrders/6))`, `peakOrders` = MAX đơn trong bất kỳ cửa sổ `last7days` trên **28 lần sync** gần nhất, đọc `MAX` chứ không `SUM`) + *funnel* 25 (thiếu hụt CTR 10 / thêm-giỏ 8 / chốt-đơn 7 so **trung vị shop cùng snapshot** — mỗi hàng đã là tổng 7 ngày) + *hiển thị* 15 + *xu hướng* 10 (giảm sâu nhất GMV/impressions so **kỳ 7 ngày trước**, bão hoà −50%) + *keyword* 10 (token keyword «Shop này bán gì» tìm thấy trong tên, đạt 50% coverage là hết phạt), **trả lại tới −20** khi đang tăng ≥50%. Trụ nào không đủ dữ liệu để phán bị **loại khỏi mẫu số** (`breakdown.available`) rồi chuẩn hoá về 100 — nếu không, listing chết nhưng thiếu dữ liệu sẽ kẹt dưới ngưỡng. Ngưỡng `≥70` = `delist`, `≥50` = `review`. Loại hẳn khỏi đề xuất: `peakOrders ≥ 4` hoặc GMV ≥2% GMV shop (`skippedWinners`); chưa có snapshot hoặc **mới list &lt; 7 ngày** (`skippedTooNew` — ngày list = `PlatformCreateTime` → `PlatformUpdateTime` → `CreatedAt` của listing mirror; không mirror thì ngày snapshot Compass đầu tiên). Chấm ngay lần sync đầu nếu đủ tuổi list. Response `{ shopId, shopName, snapshotDate, rangeStart, rangeEnd, currency, scanned, skippedWinners, skippedTooNew, skippedHealthy, delistCount, reviewCount, publishLimitTotal, publishLimitPublished, remainingSlots, recommendedDelistCount, headline, warnings[], benchmarks{ medianCtr, medianAtcRate, medianOrderRate, medianImpressions, gmv, windowDays, trendWindowDays, minTrackedDays, minListedDays }, items[{ tikTokProductId, listingId?, siteStatus?, productName, mainImage, gmv, orders, impressions, clicks, ctr, addToCartUnits, gmvRatio?, ordersRatio?, impressionsRatio?, peakOrders, peakGmv, daysTracked, daysListed?, score, verdict, breakdown{ sales, funnel, reach, trend, keyword, risingRelief, available }, keywordFit?, bestKeyword?, reasons[] }], keywords[{ keyword, searchVolume, score?, lane }] }`. **Chỉ đọc** — hủy thật vẫn qua `POST …/listings/{id}/deactivate` |
| GET | `/admin/listings/{id}` | `admin.listings.view` | Chi tiết full form: variations (+ attrs/barcode), `images` / `attributes` / `certifications` hydrate từ `RawPayload` (**`attributes` bỏ dòng sale-prop** — attrId trùng key `option{N}`, vd. `Color`/`Size` echo từ detail NCC — và **backfill `valueId`** + `attrName` theo `categoryProductAttributeList`), brand/productType/4Seller set, `hasRawPayload`, **`searchTerms` / `productHighlights`** (`string[]` — cột jsonb fill-once từ RawPayload, fallback hydrate raw nếu cột trống), **`sizeGuideJson`** (object jsonb từ `SupplierProductInfos` — Shein size chart local-only, không 4Seller), **`appliedPricing`** (công thức batch giá bán local jsonb — không đẩy 4Seller), **`descriptionRewritten` / `descriptionImagesInserted`** (local-only: Rewrite bắt buộc khi Publish; Insert Images tùy chọn). **Mặc định local-only** (không gọi 4Seller — mở edit nhanh). Query `refresh=true`: best-effort fetch per-id `/detail` 4Seller (full skuImgs / gallery / description) rồi re-hydrate + lưu RawPayload; lỗi 4Seller không chặn response. API chỉ trả field form cần thiết (không lộ raw JSON). **Perf:** đường local (`refresh=false`) load parent (Owner/Shop) + variations bằng **2 query tách** (`AsNoTracking`) thay vì `Include(Variations)` — tránh JOIN nhân bản cột `RawPayload`/`DescriptionHtml` theo từng SKU (cartesian) làm phồng payload N× ở listing nhiều biến thể |
| POST | `/admin/listings/sync` | `admin.listings.manage` | **202 Accepted + `jobId` ngay** (không chờ pull xong). Queue job nền `IListingSyncJobRunner` → pull 4Seller → upsert `Listings` / `ListingVariations`. Body: **`syncScope`** (`full` = draft content page+batch-detail; `status` = published status-only — ưu tiên hơn `syncMode` legacy), `syncMode` legacy (`light`/`status` → `status`; `full` → `full`; chỉ `draft` → server suy `full`), `maxRecordsPerSeller` (per status; `status` không gửi → **200**), `statuses`, `dateFrom`/`dateTo`, **`listingIds[]`** (hỏi 4Seller về đúng các listing id này thay vì phân trang cả bucket — dùng để kiểm lại dòng nằm sâu trong bucket sắp theo `update_time`; đặt giá trị này thì watermark delta **không** được đọc lẫn ghi vì trang lọc theo id không nói lên bucket đã quét tới đâu), … Multi-account song song **tối đa 4 seller cùng lúc** (`SemaphoreSlim`, 1 DI scope / seller). **Per-seller lease** (`IListingSellerSyncLease`) thay `SyncGate` process-wide — seller A sync không chặn seller B. Extension post-html giữ lease TTL 90s → background **skip** đúng seller đó. Job chạy với `CancellationToken.None` nên FE timeout / đóng tab **không** hủy giữa chừng. Optional `dateFrom`/`dateTo` → chỉ `createTime*`. **Đổi tài khoản 4Seller:** `FourSellerListingId` là id riêng của từng tài khoản 4Seller, nên shop chuyển seller sẽ về với id mới cho cùng sản phẩm. Upsert vì thế **nhận nuôi** bản mirror cũ khi id lạ nhưng khớp `(ShopId, TikTokProductId)` — đổi `FourSellerListingId` của bản cũ rồi cập nhật tại chỗ, thay vì đẻ vỏ mỏng mất `SourceUrl`/`RawPayload`/NCC (bỏ qua nếu bản cũ cùng `FourSellerShopId` với record hoặc trang sync còn chứa chính id đó). Lượt pull có `inserted > 0` chạy thêm một lần **gộp** listing trùng `TikTokProductId` trên shop + các shop cùng `ExternalShopId` (lỗi gộp không làm hỏng kết quả sync) |
| GET | `/admin/listings/sync/status/{jobId}` | `admin.listings.manage` | Tiến độ job: `state` (`running` \| `completed` \| `failed`), `totalSellers` / `completedSellers`, `inserted` / `updated` cộng dồn, `message`, `error`, và mảng `sellers[]` `{ ownerUserId, ownerName, status, inserted, updated, elapsedMs, message }` với `status` ∈ `pending` \| `running` \| `succeeded` \| `failed` \| `skipped_busy` — **kết quả từng phần**: seller xong trước hiện ngay, seller chậm/lỗi báo riêng. Job giữ **30 phút** sau khi kết thúc rồi tự dọn (404 sau đó). FE poll mỗi **2s**, dừng khi `state ≠ running` |
| PUT | `/admin/listings/{id}` | `admin.listings.manage` | Cập nhật full field local + deep-patch `RawPayload` (ảnh, attrs, certs, variations add/remove) — chỉ local, không push. Body có thể chỉ gửi `appliedPricing` (formula batch) → ghi `Listings.AppliedPricingJson` **không** rewrite RawPayload (nhanh) |
| PUT | `/admin/listings/{id}/description-prep` | `admin.listings.manage` | Local-only: lưu `descriptionHtml` + `descriptionRewritten` + `descriptionImagesInserted` + optional **`productName`** (SEO title từ cùng lần Rewrite; bỏ trống = giữ nguyên title cũ) + optional **`searchTerms` / `productHighlights`** (`string[]` — null/omit = giữ cột; list kể cả `[]` = ghi đè + patch RawPayload JSON array). Patch RawPayload `description`/`descriptionHtml` và `productName`/`title` nếu có. **Không** gọi 4Seller. FE gọi ngay sau Rewrite / Insert Images / Hoàn tác |
| PUT | `/admin/listings/{id}/source-url` | `admin.listings.manage` | Local-only, nhẹ: body `{ sourceUrl }`. Canonicalize (bỏ query/fragment), ghi `Listings.SourceUrl`, patch RawPayload `sourceUrl`/`listingSourceUrl` (không rewrite full payload, không load variations). Link `SupplierProductInfos` nếu `SourceUrlKey` đã có → overlay rating/shop NCC trên card. **Không** gọi 4Seller. FE: listing chưa có URL nguồn → hover tên → icon sửa nhanh |
| POST | `/admin/listings/{id}/save-draft` | `admin.listings.manage` | Body optional `UpdateListingMirrorDto`: patch form lên RawPayload local (refresh 4Seller detail **chỉ** khi body null hoặc chỉ `appliedPricing`) → `POST /api/listing/{platform}/save-draft` → lưu local (chỉ sau khi 4Seller OK). Parse push response: kiểm tra **`data.code`** và **`data.errorMessage`** lồng nhau (root `code:0` chưa đủ). **`variations[]` là tập SKU đầy đủ intentional**: SKU không còn trong body bị **xóa** khỏi `ListingVariations` + mảng `variation` trong RawPayload (kèm prune `skuImgs` theo màu còn lại và **dựng lại toàn bộ khối sale-prop**, xem `ListingSalePropSync` bên dưới) — xóa variant trên edit rồi Save Draft/Publish mới thật sự đẩy lên 4Seller/TikTok. **Sau OK (không await):** `IPostHtmlDraftSyncScheduler.Schedule(ownerId, debounce:0)` → `Task.Run` sync draft TikTok `maxRecordsPerSeller=3` (chỉ seller listing; `SkippedBusy` → retry 8s × 24) — FE đóng tab ngay; **Tải lại** list lấy dữ liệu mới |
| POST | `/admin/listings/{id}/publish` | `admin.listings.manage` | Cùng pipeline save-draft nhưng `POST /api/listing/{platform}/publish` với Referer **`/web/listing/{platform}/edit/{id}.html?status=draft`** (parity browser edit draft). Body form đầy đủ → **không** refresh RawPayload từ 4Seller trước patch (tránh ghi đè sale-prop/searchTerms vừa sửa). **Yêu cầu** `descriptionRewritten` — thiếu → 400. Thành công chỉ khi nested **`data.code=0`** và **`data.errorMessage` rỗng**, và **không** `data:false` (boolean false = reject); nếu không → 400/`BusinessException`, **không** optimistic Pending, giữ Draft + ghi `syncError`. Khi OK: local **optimistic** `PageType=published` / `SiteStatus=pending` / `PublishStatus=Publishing`, clear `SyncError`; message “Đã gửi publish, đang chờ duyệt.” **Sau OK (không await):** schedule sync seller-scoped (`pending` + `active` + `reject`, `debounce:0`, kèm `listingIds` vừa push) để reconcile publish thất bại — **trừ** khi `PublishSource=auto` (auto publish pipeline): không schedule post-sync (tránh extension lease chặn draft tiếp theo; reconcile qua sync nền). Sync (auto/thủ công) cập nhật lại `PublishStatus`/`SiteStatus` từ 4Seller (`publishStatus=publishing` → `Publishing`; `productStatus=2` → `UnderReview`; active/reject khi xong). `ApplyRecord`/`ResolveEffectiveStatuses` chỉ giữ Published/Pending in-flight khi 4Seller thật sự đang review (`productStatus=2`) hoặc báo `publishStatus=publishing`, hoặc trong **30 phút** grace sau push nếu page vẫn draft/`productStatus=1` mà chưa có tín hiệu lỗi; nếu `publishStatus`/`erpStatus` blocked (`unpublishable`, `update_failed`, …) hoặc hết grace mà vẫn draft → hạ về `draft` + `PublishStatus=Failed` + `syncError` (bỏ spinner) |
| POST | `/admin/listings/{id}/update` | `admin.listings.manage` | Cập nhật **nội dung** listing đã đăng (`pageType=published`): cùng luồng patch RawPayload rồi gọi `POST /api/listing/{platform}/update`. Referer edit page theo `siteStatus` hiện tại (`/edit/{id}.html?status=active\|inactive\|…`). Giữ `PageType=Published` và **không đổi** `SiteStatus` / Active↔Inactive — chuyển trạng thái chỉ qua Activate / Deactivate. Khi OK: **optimistic** `PublishStatus=Publishing` (spin trên list); sync sau cập nhật lại trạng thái thật từ 4Seller. **Sau OK (không await):** schedule sync seller-scoped (`active` + `inactive`, `maxRecordsPerSeller=3`, `debounce:0`) |
| POST | `/admin/listings/{id}/deactivate` | `admin.listings.manage` | Gọi `POST /api/listing/{platform}/batch-deactivate` body `[fourSellerListingId]` (number[], referer list Active `…/listing/{platform}.html?type={platform}Active`) → set local `SiteStatus=Inactive`, `PublishStatus=Inactive` (không refresh/patch RawPayload). **Sau OK (không await):** schedule sync seller-scoped (`active` + `inactive`, `maxRecordsPerSeller=3`, `debounce:0`) |
| POST | `/admin/listings/bulk-deactivate` | `admin.listings.manage` | **Hủy list hàng loạt.** Body `{ "ids": [guid,…] }`. Chỉ deactivate listing **Active** có `FourSellerListingId`; còn lại bỏ qua. Nhóm theo owner+platform → một lần `batch-deactivate` / nhóm → local Inactive + giảm `PublishLimitPublished`. Trả `{ deactivatedCount, skippedCount }`. Schedule status sync **một lần / OwnerId**. FE: nút **Deactivate (N)** cạnh **Xóa đã chọn** trên Published |
| POST | `/admin/listings/{id}/activate` | `admin.listings.manage` | Gọi `POST /api/listing/{platform}/batch-activate` body `[fourSellerListingId]` → set local `SiteStatus=Active`, `PublishStatus=Active` (không refresh/patch RawPayload). FE chỉ hiện action **Activate** khi listing published có `siteStatus=inactive` |
| POST | `/admin/listings/{id}/duplicate` | `admin.listings.manage` | Gọi 4Seller `GET /api/listing/{platform}/copy?listingId=` → lấy id bản sao → pull batch-detail/detail → upsert local mirror (`pageType=draft`) → copy `AppliedPricingJson` + `SearchTermsJson` / `ProductHighlightsJson` từ listing nguồn khi bản sao còn trống → trả `ListingDetailDto` bản mới. **Sau OK (không await):** schedule sync seller-scoped draft (`maxRecordsPerSeller=3`, `debounce:0`). FE: menu **Duplicate** trên card list; nút **Duplicate** trên edit — ở lại trang hiện tại (không navigate) |
| POST | `/admin/listings/{id}/sync-one` | `admin.listings.manage` | **Luồng nội bộ** (auto-publish orchestrator): gọi 4Seller `POST /api/listing/{platform}/sync-one-local-listing` body `{ "productId": "...", "shopId": … }` (`productId` = TikTok product id, lưu local `TikTokProductId`; `shopId` = `FourSellerShopId`) rồi refresh local detail. **FE thủ công** (menu **Đồng bộ** trên card draft, nút **Sync** trên edit) dùng `GET …/listings/{id}?refresh=true` — kéo full detail từ 4Seller (`batch-detail` + merge page), **không** gọi endpoint này. Bulk sync page/detail map `productId` → `TikTokProductId`. |
| GET | `/admin/listings/{id}/shop-metadata` | `admin.listings.view` | Query `shopId` (local Guid): tải Brand authorized + Warehouse theo shop qua cookie **owner listing**. Kiểm tra shop cùng owner/platform + có `FourSellerShopId`. Proxy 4Seller `get-brand-list-by-shop-id` + `get-warehouse-list-by-shop-id` (`fbt=0`) **song song** sau 1× cookie/is-login. Trả `brands[]`, `warehouses[]`, `defaultWarehouseId` |
| GET | `/admin/listings/{id}/brand-suggest` | `admin.listings.view` | Query `shopId` + `brandName`: gợi ý brand (`get-brand-list-by-shop-id-brand-suggest`). Cùng ownership check như shop-metadata |
| POST | `/admin/listings/{id}/cos-upload` | `admin.listings.manage` | Upload Size Chart / Product video thẳng 4Seller COS. Multipart `file` + optional `referenceType` (mặc định = platform listing, vd `tiktok`). Cookie **owner listing**. Luồng: MD5 → `POST /api/cos/get-sign` → PUT `https://{domain}{key}` (`Authorization` + `x-cos-security-token`). Trả `{ url, key, domain, localMaterialId, md5, operation, fileName }` để FE hiển thị ngay; `localMaterialId` → form `sizeChartId` / `erpVideoId`. Limit ~12 MB |
| POST | `/admin/listings/{id}/generate-size-chart` | `admin.listings.manage` | **Tạo Size Chart tự động (Grok Imagine + COS).** Yêu cầu Settings `listing.size_chart.auto_generate_enabled=true`, listing có `sizeGuideJson.chartRows` (hoặc legacy `rows`). Luồng: `SizeChartPromptBuilder` dùng `chartRows` inch (SIZE|US|BUST|WAIST|HIP|LENGTH từ `sizeInfoInch` + HIP body chart) + `{{MEASURE_GUIDES}}` / `{{MEASURE_NOTE}}` + prompt `Xai:SizeChartPrompt` → `POST /v1/images/edits` (retry `images/generations` khi edit fail) → upload COS. Trả `{ url, localMaterialId, … }`. Timeout ~120s |
| GET | `/admin/listings/{id}/auto-publish/runs` | `admin.listings.view` | Lịch sử auto publish runs. Query `take` (mặc định 20). Trả `ListingAutoPublishRunDto[]` với `steps[]` (timeline). **Trong lúc `status=running`**, `StepsJson` được flush sau mỗi bước (bước đang chạy có `inProgress=true`) — FE poll 2s khi modal mở để hiện tiến trình live; trước đây chỉ ghi khi run kết thúc nên modal hiện “không có step”. Bước đã checkpoint (chỉ sau **cancel**/crash giữa run) hiện `skipped=true`; sau **failed**/retry không còn checkpoint → mọi bước chạy lại. Step `rewrite` soft-skip khi listing đã `descriptionRewritten` (rewrite tay / lần auto trước) — `skipped=true` + sub-step `already_rewritten`, **không** gọi OpenAI. Step `load_full` có `subSteps[]`: `load_from_db`, `sync_4seller` — **luôn có mặt kể cả khi bước fail** (vd. mirror thiếu dữ liệu sau sync). Mỗi sub-step có thêm **`note?`** (chuỗi mô tả quyết định, hiển thị trong timeline modal cạnh `error`). Step `category` có `subSteps[]` audit đường AI: `category_ai_request` (shop/site/title đã gửi) · `category_ai_response` (categoryId, path, leaf, validated) · `category_ai_failed` (AI lỗi, giữ category cũ) · `category_ai_invalid` (AI trả category không có trong catalog / inactive / không phải leaf) · rồi **một** quyết định `category_confirmed` \| `category_assigned` \| `category_overridden` \| `category_kept`; override còn ghi `category_attributes_reset` (đã xoá `productAttributes` của category cũ để bước specifics map lại). Step `commerce` + `validate` gọi `ApplyDefaultQuantity` (**45**, hardcode) rồi persist; push mirror `Quantity` → `availableStock` + 1 row `stockInfoList`. Step `validate` có `subSteps[]`: `shipping_template`, `warehouse`, `package_weight`, `package_dims`, `size_chart`, `raw_payload`, **`seller_sku_unique`**, `persist_local` (ok/fail/skip). `skipped` của `shipping_template` và `warehouse` **độc lập nhau** — autofill shipping không làm warehouse (vốn đã có sẵn) hiện thành đã-thay-đổi. Run có `status=success` khi 4Seller đã nhận publish, **kể cả** khi auto publish bị tắt ngay sau đó (cancel chỉ hạ `cancelled` nếu xảy ra **trước** khi publish được chấp nhận). Chỉ log auto publish — publish thủ công không tạo run |
| GET | `/admin/listings/{id}/auto-publish/runs/{runId}` | `admin.listings.view` | Chi tiết một run (steps đầy đủ) |
| POST | `/admin/listings/{id}/auto-publish/retry` | `admin.listings.manage` | Clear `AutoPublishSkipped` + skip reason; nếu `PublishStatus=Failed` → reset về `Draft` để lại vào FIFO pick. Badge `failed` tắt ngay vì resolver **chỉ** coi skip là Failed (run cũ `failed`/`cancelled` không giữ badge). FE sau OK gọi `GET …/auto-publish-queue-status?ids[]=` 1 id rồi patch cache → `queue`/`waiting` (không full-list refetch). **Xóa** `AutoPublishCompletedStepsJson` — lần pick sau chạy lại **full pipeline** từ `load_full` (luôn `sync_4seller` refresh 4Seller). Run `failed` cũng clear checkpoint; chỉ `cancelled` giữ checkpoint để resume. Không chạy pipeline ngay |
| POST | `/admin/listings/rewrite-description` | `admin.listings.manage` | AI viết lại mô tả (OpenAI). Body `{ title, description, categoryName?, brandName?, colors[]?, sizes[]?, attributes[]? }` từ form hiện tại (không đọc/ghi DB). Strip HTML → build khối **Verified product facts** (category / brand ≠ No Brand / colors / sizes / ≤40 Specifics; lọc giá trị vô nghĩa `multicolor` / `as picture` / `assorted` / `none`…; loại field `Fit`/`Silhouette`/`Cut` khi mâu thuẫn dáng đã nêu trong title — title thắng; loại hết dòng Material/Fabric/Composition khi title và attribute đặt tên sợi khác nhau (vd. title Linen + attr Polyester) — thiếu dòng chất liệu luôn an toàn hơn một dòng sai; khi conflict, ExtraInstruction cấm nêu sợi/vải trong mô tả/search terms/highlights và C# bỏ bullet/term **và hashtag sợi** còn sót) → `system` = `OpenAI:RewriteDescriptionSystemPrompt`, `user` = `OpenAI:RewriteDescriptionPrompt` (data bọc `<verified_facts>` / `<product_title>` / `<original_description>`, coi là DATA không phải lệnh) → model `OpenAI:Model` + `OpenAI:RewriteTemperature` (0.75; retry bỏ temperature nếu model không hỗ trợ) → **normalize bullet** + **`SanitizeHashtagBlock`** + **`StripShippingSection`** → HTML TTS-safe: `<h3>` / `<p>- …</p>` / flatten bold / `<p>` hashtag (`descriptionHtml`). Prompt: phân tích thầm primary/secondary/long-tail từ title+mô tả gốc; mật độ có kiểm soát primary **4–6** / secondary **1–2** / long-tail **2–4**; **1 dòng 15–25 hashtag** cuối mô tả; thân 1400–2000 + hashtag ≤400, tổng ≤2500 (BE `MaxPlainTextLength=2900` để không trim oan). Trần vượt → **gọi lại 1 lần** rút gọn (giữ hook + CTA + thank-you + hashtag), vẫn vượt → bỏ block giữa, bảo vệ thank-you + hashtag. Không auto-save. **FE kèm:** **1. Rewrite** (+ **Hoàn tác**) · **2. Insert Images** (tùy chọn, bỏ qua paragraph hashtag khi neo ảnh). **Thứ tự Auto Specifics ↔ Rewrite không còn ràng buộc**. **Trả thêm `title`, `searchTerms[]`, `productHighlights[]`** — xem mục *SEO title* bên dưới |
| GET | `/admin/listings/rewrite-config` | `admin.listings.view` | `{ titleMode, allowRepeat, allowSpecificsRepeat, autoGenerateSizeChart }` từ Settings `listing.rewrite.title_mode` / `listing.rewrite.allow_repeat` / `listing.specifics.allow_repeat` / `listing.size_chart.auto_generate_enabled`. Trang sửa listing dùng endpoint này thay vì tải cả list Settings. `allowRepeat=false` (mặc định) khóa nút Rewrite sau lần đầu; `allowSpecificsRepeat=false` khóa Auto Specifics khi đã có Specifics (ngoài Brand/cert). `autoGenerateSizeChart=false` (mặc định) ẩn nút ⚡ tạo Size Chart AI |
| POST | `/admin/listings/{id}/map-specifics` | `admin.listings.manage` | Map `Key: Value` sang Specifics chuẩn category 4Seller (**value-first**). Body `{ categoryId, categoryName?, shopId?, title?, existingAttributes? }` — nguồn cặp `Key: Value` = **`Listing.SupplierDescriptionHtml`** (fallback `DescriptionHtml` khi chưa rewrite). **0 cặp** (mô tả đã sạch / snapshot trống) → HTTP 200, **vẫn đọc schema và seed attribute bắt buộc** của category (`parsedPairCount:0`, `matched` chứa các dòng `matchMethod:"RequiredDefault"`); category không bắt buộc gì → result rỗng, không 4xx, không gọi OpenAI, Auto Publish soft-skip bước specifics, FE toast info. Cookie owner → schema từ `ITikTokCategorySchemaProvider` (`GET /api/meta/tiktok/get-category-attribute-list-by-id`, cache `site|categoryId` 15 phút; parse `attrId`/`attrName`/`isMandatory`/`isMultipleSelected`/`isCustomized`/`requirementConditions` + `valueList` stringified) → **lọc schema**: bỏ certs, sale-prop (`attrType=2` / Color/Size/Number of items), attr có `requirementConditions` chưa thỏa (mặc định Prop65/Dangerous goods = No → Carcinogen/Reprotoxic bị loại). **Mandatory theo từng category** (`897040` không bắt buộc gì; `903824` bắt buộc Weaving method / Washing instructions / Region of origin / Material): dòng nào mô tả không nói tới thì điền mặc định đúng nghĩa (Region → China, Material → sợi phổ biến nhất trong enum, Weaving method → Not applicable, Yes/No → No, còn lại → enum đầu tiên không phải chỉ dẫn phủ định), attr trùng tên khác id copy giá trị của nhau; attr bắt buộc free-text (`valueList` rỗng) không bịa được → vào `missingRequired` và Auto Publish **fail bước B4** kèm tên attribute. Value-first trên full `valueList`; **`isMultipleSelected=1`** + source list (`,`/`/`/`and`) → gom nhiều value canonical vào một matched (`values[]`, max 5); collision → AI `AiCollision`; leftover → AI `Ai` (schema compact ưu tiên fashion Optional + `isMultiple`, bỏ enum khổng lồ không có needle). Validate: `attributeId` Int64 + mọi `valueId`/`valueName` canonical từ schema (reject invent). Confidence ≥ 0.75 → `matched`. Fashion: Care → Washing Instruction. **Không ghi DB** — `{ matched[{…, values[]}], unmatched[], missingRequired[], schemaAttributeCount, parsedPairCount }`. FE Auto Specifics / background merge có value (≥85% + Washing); gen **1 lần**/listing (skip khi đã có Specifics ngoài Brand/cert); **TEMP** nút Auto Specifics luôn enable (bỏ gate ≤3). Push: `PatchAttributes` emit `values[]` đủ id (multi) |
| DELETE | `/admin/listings/{id}` | `admin.listings.manage` | Xóa trên 4Seller (`POST /api/listing/{platform}/delete` body `[fourSellerListingId]`) rồi xóa local (`ListingVariations` + `Listings`) |
| DELETE | `/admin/listings` | `admin.listings.manage` | Xóa theo filter (bỏ paging): nhóm theo owner+platform → 4Seller delete từng nhóm → Include + `RemoveRange` local |
| POST | `/admin/listings/bulk-delete` | `admin.listings.manage` | Xóa theo id đã chọn. Body: `{ "ids": [guid,…] }`. Nhóm theo owner+platform → 4Seller delete → wipe local; trả `deletedCount` |

### TikTok attribute value rejections (denylist tự học)

TikTok thu hồi giá trị enum khỏi một attribute mà **không** thông báo, và 4Seller vẫn liệt kê đủ cặp `id`/`name` (`categoryVersion` không đổi) — nên đồng bộ lại schema **không** phát hiện được. TikTok bỏ `valueId` nó không còn công nhận, coi `valueName` là custom value, rồi chặn cả lần publish: `Cannot use custom value names for product attribute … attribute ID `100401` does not support custom value names`. Bằng chứng duy nhất là chính thông báo lỗi đó, nên `ListingSyncService` parse nó tại `PersistPushFailureAsync` và tại chỗ áp `SyncError` từ record 4Seller (`failedMessage` bất đồng bộ), rồi upsert vào `TikTokAttributeValueRejections` theo `(CategoryId, AttributeId, ValueNameNorm)`. Denylist áp cho **mọi attribute** ở cả map Specifics (lọc schema trước value-first + prompt AI) và đường push (`CanonicalizeProductAttributesAgainstSchema` thay giá trị cũ còn nằm trong `RawPayload` — nên bấm **Retry** là đủ để listing đang Failed publish lại). Mỗi giá trị bị thu hồi chỉ làm fail **1 lần**.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/tiktok-attribute-rejections` | `admin.listings.view` | Danh sách giá trị đã học, lỗi mới nhất trước. Item: `id`, `categoryId` (`*` = mọi category), `attributeId`, `attributeName`, `valueId?`, `valueName`, `rejectionCount`, `firstSeenAt` / `lastSeenAt` (UTC), `source` (`seed` \| `publish_error` \| `manual` \| **`missing_value_id`** \| **`contradicted_by_live`**), `sampleListingId?`, `sampleError?` (đã cắt). Hai source cuối **chỉ để chẩn đoán, KHÔNG chặn giá trị** — chặn sai làm rỗng attribute bắt buộc và đổi lỗi thành `Specifics.<tên attribute>`: `missing_value_id` = giá trị tới TikTok dưới dạng chữ trần (lỗi payload của ta); `contradicted_by_live` = listing đã lên sàn với đúng id đó nên lần từ chối trước không phải do enum. Chỉ `seed` / `publish_error` / `manual` mới vào denylist. **Lỗi "cannot use custom value names" nay luôn ghi `missing_value_id`**, không bao giờ chặn: thông báo đó chỉ nói payload thiếu id, và coi nó là bằng chứng đã chặn mất 5/6 giá trị của `903824/100401`. Dòng `publish_error` cũ thuộc loại này được hạ cấp lúc khởi động API |
| DELETE | `/admin/tiktok-attribute-rejections/{id}` | `admin.listings.manage` | Bỏ chặn khi TikTok mở lại giá trị — Specifics được chọn lại giá trị đó từ lần publish sau (cache denylist TTL 5 phút). Không còn dòng → 404 |

### AI SEO title (kèm trong Rewrite)

`POST …/rewrite-description` trả `{ descriptionHtml, title, searchTerms[], productHighlights[] }`. Mode tiêu đề đọc từ Settings `listing.rewrite.title_mode` (không gửi trong body). **Khóa nút sau lần đầu:** Settings `listing.rewrite.allow_repeat` (boolean, mặc định `false`) — tắt thì FE khóa Rewrite sau khi đã có tiêu đề/mô tả AI (dùng Hoàn tác); bật thì cho bấm lại (tốn token). **Auto Specifics:** Settings `listing.specifics.allow_repeat` (boolean, mặc định `false`) — tắt thì FE khóa nút khi Specifics đã có ngoài Brand/cert; bật thì cho chạy lại. Trang sửa listing lấy cả ba qua `GET …/rewrite-config` (không tải full Settings). Admin UI: card **Rewrite & Auto Specifics** trên `/settings`.

- **`strip_brands` (mặc định):** AI JSON `{ "brands": ["SHEIN BAE","SUMWON"] }` (`OpenAI:StripTitleBrands*`, song song với rewrite mô tả) — **cụm đầy đủ**, không chỉ gốc sàn. C# `StripBrandTokens` xóa token đó; nếu model chỉ trả `SHEIN` thì vẫn nuốt line in-house đi kèm (`BAE` / `ICON` / `Essnce` / `MOTF` / …). `SanitizeTitle` regex sàn cũng match `SHEIN BAE` một cụm (tránh còn sót `BAE`). ExtraInstruction bảo model **không** xuất `===SEO_TITLE===` (vẫn bắt buộc `===SEARCH_TERMS===` + `===PRODUCT_HIGHLIGHTS===`). Call brand fail → fallback `SanitizeTitle` gốc (regex sàn + line SHEIN). **Rẽ nhanh khi tiêu đề quá ngắn:** TikTok không classify title &lt; 40 ký tự (`NeedsSeoTitleFallback`). Nếu `SanitizeTitle` trước call đã dưới sàn → **bỏ** JSON brand, 1 completion mô tả **có** `===SEO_TITLE===` + `ShortTitleSeoInstruction` (nhồi primary/secondary keyword đã có trong mô tả, hàng nữ mở bằng `Women` không phải `Women's`) rồi `BuildSeoTitleAsync` + `EnsureWomenAudienceLead`. Nếu chỉ sau khi xóa brand mới ngắn → `BuildSeoTitleAsync` thêm 1 call title. Hàng nam / unisex không gắn `Women`.
- **`rewrite`:** luồng Machine-first SEO bên dưới; đích **150–250** ký tự bằng token nguồn **cộng 2–4 cụm synonym cùng món** (trần sanitize **255**). Cấm bịa thuộc tính (chất liệu/fit/dịp/mùa) để kéo dài.

**Title + search terms + highlights sinh chung một completion với mô tả.** `RewriteDescriptionSystemPrompt` phân tích thầm 1 primary / 3–5 secondary / 3–5 long-tail từ title + mô tả gốc; nhồi có kiểm soát (primary 4–6, secondary 1–2, long-tail 2–4); kết thúc mô tả bằng **1 dòng 15–25 hashtag** (C# `SanitizeHashtagBlock`: lowercase, alnum, dedupe, lọc sàn/promo/năm, cap 25). Có mục `## Search title`, `## Search terms` (2–3 chính + 2–3 phụ, lowercase, backend) và `## Product highlights` (3–5 benefit-first, hiện PDP). Model xuất mô tả (+ hashtag) → `===SEO_TITLE===` → tiêu đề một dòng → `===SEARCH_TERMS===` một term/dòng → `===PRODUCT_HIGHLIGHTS===` một highlight/dòng. `SplitRewriteBlocks` tách **trước** `Normalize` (body = phần trước sentinel đầu tiên). Bóc nhãn `Title:` / `Primary:` / bullet. C# sanitize: dedupe, lọc marketplace/promo, cap 6 terms × 80 ký tự và 5 highlights × 80. Mode `strip_brands` vẫn **bỏ** `===SEO_TITLE===` nhưng **bắt buộc** hai sentinel meta. Mảng rỗng → FE giữ giá trị form cũ (không fail cả lần rewrite). **Happy path chỉ 1 lần gọi OpenAI** thay vì 2 — call thứ hai trước đây phải gửi lại toàn bộ 2.000 ký tự mô tả vừa sinh làm context.

Title thừa hưởng `OpenAI:RewriteTemperature` (0.75) thay vì 0.4. Đo trên 20 draft thật (dải 85–100 cũ): **không** làm trôi keyword — tỉ lệ tiêu đề vào dải 85-100 tăng 13/20 → 17/20 và số listing không lỗi tăng 9/20 → 14/20, vì tiêu đề viết cùng lượt với mô tả thì bám sát nội dung hơn là đọc lại mô tả đã sinh.

**Đường retry riêng (mode `rewrite`):** khi bản draft trong completion trượt `DescribeTitleProblem`, service mới gọi `OpenAI:RewriteTitleSystemPrompt` / `RewriteTitlePrompt` với `RewriteTitleTemperature` (0.4) — chỉ sinh lại tiêu đề, không sinh lại mô tả. Model từ chối (`BusinessException`) → giữ bản draft đã sanitize, không mất cả lần rewrite.

- **Công thức (machine-first):** `Audience → Product Type → Main Style/Pattern → Key Design → Fit/Shape → Functional Details → Occasion/Season (chỉ khi title gốc đã nêu) → 2–4 cụm synonym cùng món`. 30 ký tự đầu = audience + món **đặc trưng** (Halter Tank, không Women's Top). **Bắt buộc mở rộng** search: `Nightgown` → `Nightdress`/`Sleep Dress`/`Sleepwear`; `Halter Tank` → `Halter Neck`/`Knit Tank`/`Sleeveless Top`; `Cami` ↔ `Tank Top`/`Camisole`. **Cấm bịa thuộc tính** (chất liệu, slim-fitting, vacation, mùa). **Cấm nhồi** `Club Top Party Top Summer Top`. Mini không thành Short Dress. Retry khi title chỉ xáo từ gốc (`CountNovelTitleStems` < 2). Output vẫn một dòng title.
- **Ngân sách keyword:** 1 primary từ title gốc + **2–4 cụm synonym cùng món** (search intent khác) + token nguồn còn giá trị. Cho phép lặp stem khi là cụm search khác (`Halter Top` + `Halter Neck`). Cấm nhồi `Women Dress Women Dress` / `Club Top Party Top`. Không bịa thuộc tính. Không mở đầu bằng brand. Dịp/mùa chỉ giữ khi title gốc đã nêu.
- **Bám dữ liệu nguồn (mục `## Stay on the source product` trong prompt):** cấm đổi loại sản phẩm (`cycling shorts` → `running shorts`, `biker shorts` → `bermuda shorts`, `shorts` → `leggings`); **giữ mọi** danh từ loại SP / synonym nguồn (`pants` + `trousers`); copy nguyên văn từ chỉ họa tiết/chất liệu (`geometric print` ≠ `letter print`), giữ cách viết của nguồn cho waist/rise/length/fit (**`high waist`, không phải `high rise`**), cấm cụm `boy shorts` (ở Mỹ nghĩa là quần lót), và bắt buộc giữ đặc điểm thiết kế nổi bật (`button front`, `side stripe`, `phone pocket`, `cutout`).
- **Sanitize phía BE (`SanitizeTitle`)** — không tin output model: bỏ tên sàn/nguồn (`shein`, `lazada`, `temu`, `amazon`, `aliexpress`, `wish`, `romwe`, `zaful`, `cider`, `shopee`, `alibaba`, `etsy`, `ebay`, `walmart`, `tiktok`) **kèm line in-house SHEIN** (`SHEIN BAE` / `SHEIN ICON` / `SHEIN Essnce`… — không để sót `BAE` sau khi xóa mỗi `SHEIN`), bỏ cụm khuyến mại (`hot sale`, `free shipping`, `50% off`, `$12.99`, `best seller`, **`PerfectSummer` / `MustHave` / `NewIn`**), **bỏ năm catalog `2020–2039`** (giữ `70's` / `Y2K` vì đó là era), **bỏ dịp/mùa/holiday bịa** (Halloween / Christmas / Back to School / Vacation / Summer / Beach / Party / Wedding khi title gốc không nêu — so khớp **word-boundary**, `PerfectSummer` không được tính là có `Summer`), **bỏ claim/chi tiết bịa** (`sexy` / `premium` / `luxury` / `sleeveless` / `sweet` cắt từ Sweetina), **`Short Dress` khi nguồn là Mini chứ không phải Short**, bỏ filler `Daily Wear` / `Casual Wear`, **`OverSize`/`oversize` → Oversized**, sửa `70'S` → `70's` sau Title Case, bỏ `Women` treo cuối khi đã có `Women's`, bỏ emoji + ký tự trang trí, **bỏ qualifier `Boy` trước `Shorts`**, **khôi phục `High Waist`**, gộp separator thừa, Title Case TikTok, cắt > **255**. Mode `rewrite`: **`FillFromSourceTokens`** khi < **150**, rồi **polish lại** (tránh nhồi `OverSize`/`Daily Wear` từ nguồn sau khi vừa xóa). Audit 20 draft 2026-08-17 v2: TB **139** ký tự, 10/20 trong 150–250, lỗi thật là nhồi `… Top`/`… Dress`, Mini→Short, bịa Sleeveless — prompt thêm mục Anti-patterns.
- **Giữ thuộc tính nguồn (`FindMissingSourceKeyword`):** nếu **tiêu đề gốc** có một thuộc tính vốn là truy vấn độc lập (`seamless`, `high waist`, `ribbed`, `tummy control`, `compression`, `quick dry`, `moisture wicking`, `cropped`, `oversized`, `bodycon`, `backless`, `strapless`, `off shoulder`, `plus size`, `maternity`, `fleece`, `linen`, `denim`, `mesh`, `satin`, `velvet`, `corduroy`, `waffle`, `sheer`, `padded`, `pockets`) mà tiêu đề mới rớt mất → tính là lỗi. Chỉ soi **tiêu đề gốc**, không soi mô tả: nhà cung cấp chịu tốn ký tự tiêu đề cho từ nào thì đó là điểm bán chính.
- **Chuẩn hoá thuật ngữ một chữ cái (`NormalizeLetterShapeTerms`):** Title Case coi `A` đứng riêng là mạo từ nên hạ thành thường, biến `A-Line` thành `a Line` và mất hẳn truy vấn `a-line dress`. Bước này chạy **sau** Title Case, khôi phục dạng gạch nối cho `A-Line`, `V-Neck`, `U-Neck`, `T-Shirt`, `O-Ring`, `X-Back` (kể cả khi model viết không gạch nối).
- **Retry 1 lần** khi output thô hoặc bản sanitize > **250**, hoặc &lt; **150** *và* C# không nhồi đủ token nguồn còn lại (lúc đó mới xin synonym trực tiếp; ExtraInstruction: không đủ thì dừng, cấm bịa), rớt thuộc tính `TitleSourceKeywords`, **`FindDroppedSourceHeadlineToken`**, **`FindSpecSheetJunk`** (kể cả `Slim-Fitting`), **`FindInventedGarmentType`** (bia `cami` / `matching set` / `dress` khi nguồn không có family đó; synonym cùng family được: bandeau↔tube, pants↔trousers), hoặc `FindWeakLongTail`. **Không** retry vì lặp stem ở cụm search khác. Mọi lỗi gộp một thông báo. Vẫn không đạt thì **`PickSaferTitle`** ưu tiên bản không bịa loại SP / không spec-sheet, rồi mới bản dài hơn.
- **Fallback:** lỗi gọi title (`BusinessException`) **không** làm hỏng rewrite — trả về bản draft đã sanitize, hoặc title gốc đã sanitize nếu completion không có sentinel; mô tả vẫn giữ nguyên.
- **FE:** ghi đè thẳng `productName` + Select tags Search terms / Product highlights; **Hoàn tác** khôi phục tiêu đề, mô tả, search terms và highlights; lưu ngay qua `PUT …/description-prep`. Save Draft / Publish đẩy JSON array sang 4Seller.
- **Nút Rewrite đặt ở hai chỗ:** ngoài card Description còn một bản ngay cạnh ô Product name — Rewrite ghi đè chính ô title mà seller đang nhìn, trong khi nút gốc nằm tít dưới card Description. Hai nút gọi **cùng** `applyAiRewriteDescription` và đọc **cùng** state (`rewriteDescription.isPending`, `descriptionRewritten`, `rewriteUndoHtml`) nên nhãn ✓, trạng thái disabled và nút Hoàn tác đi kèm luôn khớp nhau — không nhân đôi nhánh logic nào. Nhãn khác nhau có chủ đích: bản trong card giữ số `1.` vì đứng cạnh `2. Insert Images` (tùy chọn sau Rewrite), bản cạnh ô title chỉ ghi `Rewrite` vì ở đó không có bước nào để đánh số. Bố cục là **flex row** (`Form.Item label` bọc ngoài + `Form.Item name="productName" noStyle` bên trong), giống hàng Category — **không** dùng `Input addonAfter` vì addon của input-group là một ô có border riêng, luôn cao hơn ô input cạnh nó 2px.

### Bộ đo SEO hàng loạt (chỉ Development)

`POST /api/v1/dev/seo/rewrite-audit?take=20&verbose=false&seed=` (`DevListingSeoDiagnosticsController`, `[AllowAnonymous]`, ngoài Development trả **404**). Chạy full pipeline rewrite trên N draft chưa rewrite rồi **chấm điểm, không persist** — cùng một tập draft chạy lại sau khi sửa prompt là so được hai con số trực tiếp. Query `seed` optional: chỉ harness gửi OpenAI `seed` (production Rewrite **không** gửi seed).

Chỉ số trả về: độ dài tiêu đề (min/avg/max), tỉ lệ vào dải **150-250**, `avgScore` 0–100, `searchTermsAvgCount` / `highlightsAvgCount` / `metaComplete`, số listing sạch lỗi, độ dài mô tả trung bình, và bảng đếm lỗi. Mỗi listing kèm `faults[]`, `hashtagCount`, `primaryKeywordCount`, `searchTermsCount`, `productHighlightsCount`, `droppedSourceAttributes`, `unsupportedTitleWords`, `descriptionMarketplaceHits`, `missingSections`, `sourceKeyValuePairs`. Verbose dump thêm `searchTerms[]` / `productHighlights[]`.

**Scorer (độc lập với service):** title faults như trước; mô tả **bắt buộc** Why You Will Love It / Product Details / Great For (**không** Shipping, **không** Care Tips). Fault `description-forbidden-care-tips` / `description-forbidden-colors-line` / `description-forbidden-sizes-line` nếu còn section/dòng đó. Hashtag 15–25; primary density ~4–6; Search terms 4–6 + desync/dup; Product highlights 3–5 + grounded/superlative. Trần mô tả 2900.

`POST /api/v1/dev/seo/rewrite-seed-hunt?seeds=30&fixtureCount=3` — cùng 3 draft cố định, mỗi seed 0..N-1 rewrite + score; trả `bestSeed` / `bestScore` / `runs[]`; ghi `backend/artifacts/rewrite-prompt-opt/seed-hunt-*.json`. **Không** ghi seed vào appsettings.

`POST /api/v1/dev/seo/rewrite-prompt-optimize?seed={bestSeed}&take=30&maxRounds=5&commit=false` — khóa seed, audit, patch có kiểm soát **một** `RewriteDescriptionSystemPrompt` theo top faults (Care Tips/Colors/Sizes/hashtag/density/search terms/highlights/title…), re-audit, rollback nếu điểm kém. `commit=true` ghi appsettings.json + Development.example. Artifact `prompt-round-*.txt`.

CLI: `dotnet run --project backend/tools/RewritePromptOpt -- http://localhost:5000 30 30 5` (seed hunt rồi optimize, `commit=false`).

Các phép kiểm **viết lại từ luật TikTok, không dùng lại code của `ListingDescriptionAiService`**: một phép đo dùng chung code với thứ nó đo thì không bao giờ bắt được lỗi trong phần code chung đó.

Số đo trên 20 draft thật. Dải đích **cũ** 200-250 (cột lịch sử 85-100 giữ để so mốc); dải hiện tại **150-250**, không bắt buộc đủ 150 khi nguồn ít thuộc tính:

| | 2 call (trước) | gộp sentinel | + cổng retry 85 | audit 2026-08-17 (dải 200-250) |
|---|---|---|---|---|
| Listing không lỗi (scorer) | 9/20 | 14/20 | 14/20 | **15/20** |
| Tiêu đề trong dải đích | 13/20 | 17/20 | **20/20** (85-100) | **18/20** (200-250) |
| Độ dài tiêu đề TB | 86,6 | 90,0 | 91,1 | **209,4** |
| Tiêu đề ngắn nhất | 75 | 75 | **86** | **194** |

Rò tên sàn, ngôn ngữ khuyến mại, emoji và ký tự cấm: **0/20**. Scorer còn 3 `title-word-not-in-source` (Camisole / Night / Outerwear — synonym, đã nới) và 2 `title-short-of-band` (194, dải 200 lúc đó). Rác thật sanitizer bắt sau audit: **`PerfectSummer`**, năm **`2026`**, **`70'S`**, Halloween / Back-To-School bịa. `SanitizeTitle` + `FillFromSourceTokens` xử lý những cái đó không cần gọi lại model.

### Listings list query

List dùng projection cột card + batch load ≤5 variation/page (tránh Include full entity/`RawPayload`). `statusCounts`: 1× `GroupBy(SiteStatus)`; khi đã filter `pageType=draft|published` thì `Draft`/`Published` = `All`/0 (không thêm 2 count). Filter `siteStatus`/`platform` so khớp equality (giá trị đã normalize lowercase). `search`: Guid → exact id local; số → exact 4Seller listing/shop/SKU id (+ contains TikTok product id / tên / MSKU); text → contains trên tên, MSKU/variant title, 4Seller/TikTok/local id, category, source URL/key, seller, TikTok shop (name/profile/4Seller id), shop NCC Shein (title/code/tips). Index: `(PageType, PlatformUpdateTime)`, `(OwnerId, PageType, SiteStatus)`. FE `useAdminListings` `staleTime` 30s; filter sellers/platforms/shops 5 phút.

| Param | Mô tả |
|-------|--------|
| `pageCurrent` / `pageSize` | Paging (API mặc định 20, max 100; admin/seller-web list mặc định **25**) |
| `pageType` | `draft` \| `published` |
| `siteStatus` | `active` / `inactive` / `suspended` / `removed` / … (Published tabs) |
| `ownerUserId` | Filter seller |
| `platform` | vd. `tiktok` (so khớp không phân biệt hoa thường) |
| `shopId` | Shop local |
| `search` | Tên SP · MSKU/variant · 4Seller listing id · TikTok product id · id local (Guid) · shop TikTok/4Seller · seller · danh mục · URL/`SourceUrlKey` nguồn · shop NCC (Shein title/code/tips) |
| `dateFrom` | Mốc thời gian listing từ (inclusive; `PlatformCreateTime ?? PlatformUpdateTime ?? CreatedAt`) |
| `dateTo` | Mốc thời gian listing đến (inclusive) |

**Response `items[]` — auto publish (list card):**

| Field | Mô tả |
|-------|--------|
| `autoPublishQueueStatus` | Chỉ khi `pageType=draft` và system auto publish bật: `queue` (eligible nhưng không phải **FIFO head toàn shop**) · `waiting` (**một** draft eligible đầu FIFO **toàn shop** — cùng logic `PickListingIdAsync`, không phụ thuộc subset `ids[]` trong batch poll) · `processing` (run đang chạy) · `failed` (**chỉ** `AutoPublishSkipped` — khớp cổng pick; run gần nhất `failed`/`cancelled` **không** skip vẫn `queue`/`waiting`). **Không** suy từ một mình `PublishStatus=Failed` / `syncError` thiếu field 4Seller. Null = không badge. Mỗi shop chỉ một `waiting` toàn cục; shop đang `processing` → eligible còn lại chỉ `queue`. |
| `autoPublishNextPickAt` | Chỉ khi `autoPublishQueueStatus=waiting`: UTC mục tiêu countdown interval shop (`LastAutoPublishAt` + interval) — FE badge `Wait M:SS` / **Processing** (interval hết; cùng `ShopAutoPublishSchedulerBadge` như Shops list). |
| `lastAutoPublishRunStatus` | Status run auto publish gần nhất (`running`/`failed`/`success`/…) — tooltip ngắn. |
| `autoPublishSkipped` / `autoPublishSkipReason` | Draft bị skip sau lỗi business — badge Failed + Retry. Transient fail / Cancelled **không** set skip → không badge Failed, listing vẫn trong FIFO. |
| `lastAutoPublishRunId` | Id log run gần nhất. |
| `publishSource` | `auto` \| `manual` — nguồn publish (Published tab: tag **Auto** / **Manual** trên card). |
| `hasSizeChart` | `true` khi `SupplierProductInfos.SizeChartImageUrl` không rỗng (cùng cổng pick auto publish). `false` → FE hiện icon đỏ trên header `ListingCard` (tooltip: thiếu size chart / không vào hàng chờ). |

FE Draft list: badge **Queue** / **Wait M:SS** (waiting — cùng component Shops) / **Processing** (waiting, interval hết) / **Publishing** (run đang chạy) / **Failed** trên `ListingCard` header; **Failed** kèm nút retry (icon ↻, gọi `POST …/auto-publish/retry` rồi `GET …/auto-publish-queue-status` 1 id để patch badge → `queue`/`waiting`); **Publishing**/**Failed** (tag) click → `AutoPublishTimelineModal` (`GET …/auto-publish/runs`, poll **2s** khi run `running` để hiện step live / `inProgress`, modal cũng có **Retry auto publish**). **Poll ngầm 12s** qua `GET …/auto-publish-queue-status?ids[]=` khi còn `queue`/`waiting`/`processing` — patch React Query cache tại chỗ (badge đổi không spin overlay); khi hết in-flight trên trang → invalidate list một lần (draft publish xong biến mất khỏi tab). Nút **Tải lại** vẫn gọi full list. Published: tag **Auto**/**Manual**, không queue badge. Auto publish sau publish OK **không** chờ post-sync reconcile status (local optimistic ngay; sync nền cập nhật sau) — tránh extension lease chặn draft FIFO tiếp theo cùng seller. Sync demote listing `PublishSource=auto` về draft sau publish ảo → `AutoPublishSkipped` (badge Failed + Retry); pick loại `PublishStatus=Failed` **sau** lần push (silent demote / reject). Draft extension thô (`requireMessage` kiểu Weight/Dimensions, `unpublishable`, chưa `LastPushAt`) giữ `PublishStatus=Draft` + `syncError` — **không** badge Failed, vẫn vào FIFO để auto-publish autofill.

### Listing detail query

| Param | Mô tả |
|-------|--------|
| `refresh` | `true` → best-effort pull 4Seller per-id `/detail` trước khi trả DTO; mặc định `false` (chỉ local DB) |

### Listings sync body

```json
{
  "ownerUserId": null,
  "platform": "tiktok",
  "statuses": ["draft"],
  "shopId": "",
  "syncScope": "full",
  "maxRecordsPerSeller": 200
}
```

**`syncScope`** (ưu tiên hơn `syncMode` legacy):

| Scope | Dùng khi | 4Seller | Ghi DB |
|-------|----------|---------|--------|
| `full` | Draft bucket (list Draft, post-html, background draft) | Page + `batch-detail` mọi hàng | `ApplyRecord` full + variations + RawPayload |
| `status` | Published buckets (list Published, background published, sau publish) | Page only | Chỉ `pageType`/`siteStatus`/`publishStatus`/`erpStatus`/`syncError`/`tikTokProductId` — **không** đè mô tả/variant/RawPayload |

`syncMode` legacy: `light` hoặc `status` → `status`; `full` → `full`. Controllers ép `SyncSource=manual`.

**Ngân sách thời gian (chống treo).** Mỗi call 4Seller trong luồng listing sync bọc linked CTS **45s**; mỗi seller có ngân sách `60s + 1.5s × (maxRecordsPerSeller × số status)` (+ **3 phút** dự phòng khi Manual published reconcile), trần **15 phút** (Manual published **20 phút**). Seller quá hạn được ghi vào `failedSellers` / `sellers[].status=failed` và **không** kéo theo các seller khác. Log `Listing sync seller {OwnerId} finished in {ms}` và `Listing page {status} #{page} … merge {ms} ms, upsert {ms} ms` để chẩn đoán; call 4Seller ≥3s log Information, còn lại Debug.

**Phân trang theo cap.** `maxRecordsPerSeller` áp **mỗi status bucket**; `pageSize` cố định `min(cap, 100)` cho cả bucket (đổi giữa chừng sẽ lệch offset của 4Seller) và lặp trang tới khi đủ cap / trang trả ít hơn `pageSize` / đã tới `total`. Trước đây cap > 100 chỉ lấy được 100 bản ghi vì `break` ngay sau trang đầu.

`statuses` rỗng → mặc định `draft` + published statuses (`active`, `inactive`, `suspended`, `removed`, `incomplete`, `private`, `pending`, `reject`, `unsaleable`). UI list: Draft → `["draft"]` + `syncScope=full` (không gửi khoảng ngày); Published (mọi sub-tab) → đủ 9 published status + `syncScope=status` — **khi filter ngày đang chọn** (preset 7d/30d/…) nút Sync gửi `dateFrom`/`dateTo` + `maxRecordsPerSeller=500` (bỏ watermark, `createTime*` khớp list); không chọn ngày → cap **200** + watermark. Optional `dateFrom`/`dateTo` (API) → 4Seller `createTime*` (Pacific wall-clock). Optional **`shopId`** = 4Seller shop id (hoặc local shop Guid — BE map sang 4Seller id); UI Sync khi đang lọc shop gửi kèm + `ownerUserId` của shop → pull đúng shop, **không** dùng/ghi watermark delta (giống `listingIds[]` / khoảng ngày). **Không gửi `shopId`:** BE tự scope theo mọi shop local **Active** của seller có `FourSellerShopId` — gọi page 4Seller lần lượt từng shop id (cap `maxRecordsPerSeller` **mỗi shop × mỗi status**); upsert bỏ listing thuộc shop không nằm trong map (tránh orphan `ShopId` null). Seller không có shop Active+FS id → giữ hành vi cũ (kéo mọi shop trên cookie). Refresh nội dung một listing: `GET …/listings/{id}?refresh=true` (menu **Đồng bộ** card draft + nút **Sync** edit). Optional `maxRecordsPerSeller` — cap **mỗi status bucket** khi unscoped; khi đã scope Active shops / `shopId` → cap **mỗi shop × status** (null + `status` → **200**). Sau mỗi published `status` pull: reconcile stale in-flight/pending qua `listingIdList` (**chunk ≤50**): background **50**/seller × 1 vòng; Manual Sync **200**/seller × tối đa **5 vòng**, ưu tiên cửa sổ `dateFrom`/`dateTo` đang lọc, **prove** filter rồi upsert found + **xoá** local đã biến mất mọi bucket (sau grace 30 phút `LastPushAt`). Job/status trả `reconciledUpdated` / `reconciledRemoved` / `stillInFlight` (toast FE tóm tắt). Ngưỡng stale pending **1 giờ** (`LastSyncedAt`). Background: Draft `syncScope=full` cap 50/shop Active; Published `syncScope=status` createTime lookback + cap 500/shop/bucket + reconcile. Post-html scheduler draft `full` cap 20.

**Sync delta + bỏ qua dòng không đổi:** mỗi bucket status gửi `updateTimeStart` lấy từ bảng `ListingSyncWatermarks` (`OwnerId` + `Platform` + `Status`, unique) trừ **10 phút** biên an toàn, nên 4Seller chỉ trả dòng nó thực sự đổi. Trang sắp xếp giảm dần `update_time` → gặp dòng cũ hơn mốc là dừng phân trang. Mốc **chỉ ghi khi vòng phân trang kết thúc bình thường** — call 4Seller lỗi/timeout ném ra ngoài nên bucket đọc dở không nâng mốc (trang về mới-nhất-trước, nâng mốc vượt dòng chưa đọc là bỏ sót vĩnh viễn). Lượt bị cắt bởi `maxRecordsPerSeller` vẫn nâng mốc vì cap đó vốn đã chặn background sync nhìn quá N dòng mới nhất; phần đuôi chỉ được phủ khi chạy sync thủ công (cap 200). Ngoài ra **mỗi 12 giờ chạy lại một lượt không lọc** để tự chữa lành. Truyền `dateFrom`/`dateTo` **hoặc `shopId`** **hoặc `listingIds[]`** = chỉ định slice cụ thể → bỏ qua watermark (không đọc/ghi). **Quy ước múi giờ đã đo thực nghiệm** (`GET /api/v1/dev/listing-sync/update-time-probe`, chỉ Development, `?ownerId=&hoursBack=&status=`): bộ lọc so với **chính chuỗi số 4Seller trả về trong `updateTime`, không quy đổi múi giờ** — khác `createTimeStart` (phải là giờ Pacific). **Phòng hộ 4Seller:** nếu `/api/listing/{platform}/page` trả `Comm.Back.Get_Page_Failure` khi có `updateTime*`, `GetListingPageAsync` **retry một lần không gửi bộ lọc** (log `WRN`) — sync vẫn chạy được; lượt đó coi như full scan nên phân trang dừng theo cap/`pageSize` thay vì cắt sớm theo watermark. `PostRawAsync` log `DBG` body (≤500 ký tự) khi `success=false` hoặc `code≠0`. Ở phía mình, dòng có `updateTime` khớp `PlatformUpdateTime` được **bỏ qua hoàn toàn** (không `ApplyRecord`/`SyncVariations`) sau khi qua các guard đầy đủ (`RawPayload` đã có; trạng thái/giá/tồn/biến thể khớp **giá trị sẽ ghi**, gồm nhánh giữ `UnderReview` và giá suy từ variation local); dòng bị bỏ qua vẫn chạy enrichment nên link `SupplierProductInfo`/`AddedFrom` không mất. Log trang có dạng `(+{inserted}/~{updated}/={skipped}) [full|delta>=…]`, kèm `Listing skip blocked … {reason}={count}` khi có dòng không bỏ qua được. **`RawPayload` sync chỉ ghi khi ô trống** (kể cả `full`); muốn kéo lại bản gốc thì dùng `GET /admin|seller/listings/{id}?refresh=true`.

**Sync detail enrich:** `syncScope=full` = page + `batch-detail` (chunk 30, retry 1×; không per-id fallback — tránh timeout bulk). `syncScope=status` = chỉ page + `ApplyStatusRecord` (không đè mô tả/variant/RawPayload). **On-demand refresh** (`?refresh=true`, edit Sync, menu **Đồng bộ** card draft): gọi thêm page `listingIdList` + merge `failedMessage` từ page (detail thường thiếu) trước `ApplyRecord`; `FourSellerFailedMessageReader` parse alias/nested (`publishInfo`, `warningMessage`, …). **`POST …/sync-one`** (TikTok → 4Seller qua `sync-one-local-listing`) chỉ dùng luồng nội bộ (auto-publish), không phải nút list/edit thủ công. **`SyncError`:** ưu tiên `failedMessage` page/detail; khi blocked mà 4Seller không trả text → **giữ** lỗi push/sync chi tiết cũ (không ghi đè bằng placeholder hệ thống); chỉ khi không có lỗi chi tiết mới gán fallback tiếng Việt (`publishStatus=… nhưng không trả lý do chi tiết`). `ReconcileSyncErrorAfterDetailRefresh` không thay lỗi chi tiết bằng placeholder. Clear `syncError` khi 4Seller báo healthy. `sync-one` **không** xóa `syncError` trước refresh. `batch-detail` với nhiều seller trả **rỗng** → sync chỉ có ảnh page-level (1 ảnh/màu); full skuImgs nhiều ảnh/màu chỉ đến từ per-id `/detail`. `ApplyRecord` (full): chỉ ghi `DescriptionHtml` khi non-empty **và** listing chưa prep (`descriptionRewritten` / `descriptionImagesInserted` đều false). Status scope: không đè description/RawPayload. Normalize media vào RawPayload (full / first-fill): `DetailImageUrls` + `mainImage` → `mainImage` pipe + `imageList` (chỉ khi đang là URL string / rỗng); **không rewrite** mảng `images` (giữ metadata 4Seller — tránh `业务异常参数错误` khi save-draft). `ColorImageUrls` + variation images **merge** với `skuImgs` raw (object **hoặc** array Color-row; ưu tiên `img_url` pipe trước `url` đơn) → object `color → url[]` (≤ 9/màu). Variation `MainImage` lưu pipe đủ URL; push JSON chỉ ghi **một** URL vào `variation[].mainImage`. GET detail hydrate: `images[].colorValue` từ skuImgs key + `listingSkuId` khi khớp; parse đủ URL/màu; fallback variation `mainImage`. Admin edit dùng `colorValue` API (không chỉ suy từ `listingSkuId`). FE list + edit: `formatSyncErrorDisplay` — title ngắn + body đầy đủ (không ellipsis một dòng trên list).

### Listings update / push

`PUT` body (mirror ProductEdit surface):

| Field | Ghi chú |
|-------|---------|
| `productName`, `descriptionHtml` | Patch vào RawPayload |
| `searchTerms`, `productHighlights` | `string[]` (trim, bỏ rỗng). Null = giữ nguyên cột; `[]` = xóa placeholder 4Seller. Lưu `Listings.SearchTermsJson` / `ProductHighlightsJson` (jsonb). **Push** luôn ghi RawPayload `searchTerms` / `productHighlights` là **JSON array** (tạo key nếu thiếu — không stringify, khác `productAttributes`). GET: cột trước, fallback hydrate từ RawPayload. Sync fill-once khi cột còn null |
| `categoryId` / `categoryName`, `brandId` / `brandName`, `productType` | Category + brand + SingleSku/MultiSku. **`productType` hydrate:** ưu tiên `hasVariation` trong RawPayload 4Seller (`1` = MultiSku / Has Variations, `0` = SingleSku); fallback cấu trúc `variation[]` / sale-prop / `variationCount`; patch `productType` vào RawPayload khi refresh. Brand patch đồng bộ cả `brandId`/`brandName` và `brandInfo` JSON-string `{"id","name"}` (No brand = `id:"0"`). |
| `warehouseId`, `sourceUrl` / `listingSourceUrl`, `siteId` | Shipping / 4Seller set. Đổi warehouse → patch root `warehouseId` **và** collapse mỗi `variation[].stockInfoList` về **1 row** `{warehouse_id, available_stock}` (golden shape). Local `Quantity` luôn mirror thành `availableStock` (tạo key nếu thiếu) rồi ghi `stockInfoList` — thiếu key + collapse warehouse từng mặc định `available_stock: 0` dù form/auto-publish đã set qty. Không ghi cùng qty vào mọi row legacy / `globalStockInfoList` rồi SUM lúc sync — trước đây làm tồn nhân đôi mỗi lần Save/Publish/Auto Publish |
| package dims + units, `sizeChartId`, `safetyWarning` | Shipping / safety |
| `fourSellerGroupId` / `Name`, `appliedRules`, `refListingId` / `refShopId` | 4Seller set |
| `appliedPricing` | Công thức batch giá bán **local-only** (`Listings.AppliedPricingJson` jsonb ~10 số + `templateId`/`appliedAt`). Không đẩy 4Seller; không dùng cho doanh số/profit báo cáo. FE Apply Batch → `PUT` chỉ field này (skip RawPayload rewrite); Save Draft/Publish/Update cũng gửi kèm. GET detail trả lại để hydrate preview |
| `descriptionRewritten` / `descriptionImagesInserted` | Cờ prep **local-only** (`Listings` bool). Gửi kèm Save Draft / Publish / Update; GET detail hydrate nút Rewrite/Insert. Publish **chỉ bắt** `descriptionRewritten` → thiếu → 400; `descriptionImagesInserted` **tùy chọn** (chèn hoặc bỏ qua). **Khi một trong hai cờ = true:** sync/`refresh=true` **không ghi đè** `DescriptionHtml` **và `ProductName`** từ 4Seller (giữ bản AI local + patch lại vào RawPayload) — tránh mất mô tả đã rewrite/insert và tốn lại token. Sync chỉ ghi đè + reset cờ khi chưa prep. Không đẩy marketplace. **Insert Images (FE):** tối đa **3** ảnh từ gallery **Default (Main)**; mỗi ảnh 1 dòng (`<img width="800" height="800">`) sau từng section; không table, không ảnh variant màu. Listing HTML cũ cần Insert lại + Update |
| `shopId` | Local shop Guid — shop Publish (card Product Info). Remap `Listing.ShopId` + `FourSellerShopId` (= 4Seller `record.Id`), patch RawPayload `shopId` / `currentShopId`. Shop phải thuộc `listing.OwnerId` và đã Connected |
| `images[]` | `mediaType` Main/Detail/Video/SizeChart, `url`, optional `listingSkuId` (ảnh theo màu). Admin form gửi `colorValue` (Color axis); `formToMirrorListingPayload` map → `listingSkuId` theo variation attrs.Color. **Hydrate từ RawPayload:** Video ← `erpVideoUrl` ưu tiên, rồi `videoUrl` / `productVideo` / `videoPostUrl` (bỏ qua `""` — draft TikTok thường để `videoUrl` rỗng trong khi `erpVideoUrl` có MP4); SizeChart ← `sizeChart` / `sizeChartImage` / `sizeChartUrl`. **FE/BE:** URL explicit `http://` tự nâng `https://` (form hydrate + RawPayload đệ quy khi Save/Publish/Update / sanitize push) — listing cũ còn `http://img.ltwebstatic…` trong RawPayload (write-once) được chữa lúc push; Alert `syncError` hiển thị link https. **Ảnh `.gif`:** BE loại trước lưu mirror (`PatchImages`) và trước push (`SanitizePayloadForFourSellerPush`); auto publish `PrepareImages` cũng lọc — TikTok/4Seller không upload GIF (swatch SHEIN thường `.gif` → `attribute image upload fail`). Chỉ nhận diện path `.gif`, không nhầm `.jpg?…`. **Product video:** BE gỡ trước push (`StripProductVideoFromPayload` trong `SanitizePayloadForFourSellerPush` — mọi Save draft / Publish / Update); auto publish `PrepareImages` gọi `RemoveProductVideo` (xóa hàng `Video` + clear `erpVideoId`). Video vẫn có thể hiện trên form cho đến khi push; sau push OK RawPayload local không còn key video. **Refresh/sync từ 4Seller:** `NormalizeListingMediaIntoRawPayload` xóa key Video/Size Chart rỗng trong RawPayload; FE `mergeListingFormRemoteDiff` reconcile typed media theo remote (draft local không giữ chart/video đã bị 4Seller gỡ). **Patch push:** ghi URL khi có; **xóa** (`""`) `erpVideoUrl`/`videoUrl`/`productVideo`/`videoPostUrl` hoặc `sizeChart*`/`sizeChartId` khi form bỏ Video / Size Chart — tránh save-draft còn media cũ trên 4Seller |
| `attributes[]` | Specifics (`attrId`, `attrName`, `attrValue`, `valueId?`, `values?: [{valueId,valueName}]`). **Bắt buộc:** luôn có `Brand = No brand`; FE khóa dòng này, Auto map không xóa, BE bỏ mọi Brand gửi lên. **Push:** `PatchAttributes` ghi `productAttributes` JSON string TikTok `{ attributeId, attributeName, values: [{ valueId, valueName, isShow }] }` — ưu tiên `values[]` multi (`isMultipleSelected=1`, max 5); fallback `attrValue`/`valueId`; bỏ attrId không Int64 / value rỗng. **Condition (category có schema):** khi category trả `Condition type` + `Condition description` (vd. `861712`), BE tự seed **New** + `Brand new` với đúng `attributeId`/`valueId` từ `get-category-attribute-list-by-id` — lúc mở edit (`GET` detail), Auto map, sync refresh, và trước Save Draft/Publish nếu còn thiếu. Category không có 2 attr này (vd. `601265`) → không chèn. **Region of origin / CPSIA (category có schema):** seed **China** / **No** (id/`valueId` từ schema, không hardcode) khi còn trống — trước Save Draft/Publish, Auto map (`EnsureRegionAndCpsiaSpecifics`), và GET detail merge. **Washing instructions (fashion):** Care Instructions → enum kèm `valueId` (fuzzy `Hand wash only` → `Hand wash`); **không** custom valueName khi schema đã có enum (kể cả `isCustomized=1` — tránh `Cannot use custom value names… 100401`). Giá trị phải **mô tả cách giặt**: còn trống → seed `Hand wash only` → `Hand wash` → `Cold wash` → `Machine washable` → `Machine wash` (nhẹ nhất trước) lúc map / GET merge / trước push; **không bao giờ** lấy chỉ dẫn phủ định (`Do not dry clean`, `Do not wash`) hay `Dry clean` làm mặc định; category không có cách giặt nào hợp lệ → để trống + báo `missingRequired`. Fuzzy match **không đảo nghĩa**: `Do not dry clean` không map thành `Dry clean` (dù chứa nguyên chuỗi đó), còn `Machine wash, do not dry clean` vẫn ra `Machine washable`. Align với **Care instructions** (`101303`) để hai ô không nói ngược nhau (giảm 【Publish Failed】PDP Inconsistent Information). Giá trị TikTok đã thu hồi bị loại qua denylist tự học — xem *TikTok attribute value rejections*. **Auto map:** `POST .../map-specifics` value-first + AI trên schema đã lọc (không sale-prop Color/Size; **seed** Region/CPSIA/Washing khi thiếu; không conditional Carcinogen khi Prop65=No); canonicalize id/name; multi gom vào một row. **Mandatory tổng quát:** mọi attr `isMandatory=1` của category (trừ cert / sale-prop / gate chưa mở) đều được điền trước push — thiếu một dòng là 【Publish Failed】Specifics.&lt;tên&gt;. **Chốt theo schema trước mọi push** (`CanonicalizeProductAttributesAgainstSchema` — schema sống từ `ITikTokCategorySchemaProvider`, fallback `RawPayload.categoryProductAttributeList` vì bản nhúng lệch sau khi đổi category): bỏ dòng **sale-prop** (attrId trùng key `option{N}` — vd. `100000` Color), dòng **ngoài schema category** và dòng có **`requirementConditions` chưa mở**; **điền `valueId`** theo tên enum (Washing: fuzzy synonym); giá trị lạ chỉ giữ khi `isCustomized=1` **trừ Washing** (có enum → luôn bỏ custom); `isMultipleSelected=0` → 1 giá trị. Match **tên trước id**. Chống 【Unpublishable】Product attributes are unavailable. **Hydrate (`GET` detail)** bỏ dòng sale-prop echo + backfill `valueId` |
| `certifications[]` | Product Certification Yes/No: `Compliance` (Dangerous Goods), `ProductCert` (CA Prop 65 Repro), `ResponsiblePerson` (CA Prop 65 Carcinogens) — `name` = `Yes`/`No`, **mặc định `No`** (fashion). FE luôn gửi đủ 3 hàng. **Push:** BE upsert vào `RawPayload.productAttributes` (JSON string TikTok: `attributeId` `101619`/`101395`/`101400`, `valueId` Yes=`1000058` / No=`1000059`) — không phụ thuộc `productCertifications` (thường `"[]"`). Trước đây skip khi field là JSON string → cert lúc có lúc không |
| `variations[]` | Full replace: optional `id` / `fourSellerSkuId`, `sellerSku`, `salePrice`, **`initialPrice`** (giá nhập / cost — UI form `costPrice`), `originalPrice`, qty, `barcode`, `variantAttributes`, `title`, `mainImage` — hỗ trợ thêm/xóa SKU. **Không bắt đủ tích Color×Size** (ma trận thưa hợp lệ: xóa từng tổ hợp trên edit hoặc listing import). Sync **draft**: `InitialPrice` = giá 4Seller (cost), `SalePrice = 0` lần đầu; edit form cột **Giá nhập** / **Giá bán** để tính rồi Save Draft/Publish. **Khoá ghép (đã đổi):** `id` → `fourSellerSkuId` → **tổ hợp sale-prop** (`variantAttributes` normalize Color/Size/Number of Items) → `sellerSku` → `title`. Tổ hợp đứng trước `sellerSku` vì Batch SKU đổi toàn bộ mã một lượt — ghép theo mã sẽ gán chéo giá/tồn giữa các variant. **`sellerSku` phải có và duy nhất** trong body (FE chặn trước, BE trả 400 nếu thiếu/trùng) vì nó vẫn là khoá dự phòng khi ghép với payload cũ. Không được có 2 dòng cùng tổ hợp. |

Admin list `/listings/draft` | `/listings/published`: card Draft hiển thị **Giá nhập** theo khoảng `variations[].initialPrice`; card Published tiếp tục hiển thị **Giá** theo `lowPrice` / `highPrice`. Thumbnail sản phẩm hiện **Spin** giữa ảnh khi `siteStatus=pending` hoặc `publishStatus=Publishing` / `UnderReview`. Sau Publish thành công listing **rời Draft ngay** → Published/`pending` (`Publishing` optimistic); Publish/Update push đặt `PublishStatus=Publishing` — sync sau cập nhật lại trạng thái 4Seller. Draft/Published nằm ở sidebar menu con; trang Published giữ sub-tab `siteStatus` (`?siteStatus=`); FE sau Publish `navigate` → `/listings/published?siteStatus=pending` và `invalidateAdminListingsCache` (BroadcastChannel → tab list cũ refetch; `refetchOnWindowFocus` dự phòng).

Luồng FE edit: **Edit** (hover/focus prefetch JS chunk + `GET` local detail → `sessionStorage` bridge cho tab mới) → ưu tiên stash/`localStorage` draft (`listing-edit-draft:{id}`) nếu có (backfill Video/SizeChart từ GET local nếu draft thiếu), rồi `GET /admin/listings/{id}` (local DB `AsNoTracking`, không chờ 4Seller; React Query `staleTime` 60s — chỉ spin khi chưa có data). **UI perf:** DB local đã có variations → bind **đồng bộ ngay** (không `startTransition`); 4Seller silent refresh có điều kiện (`listingNeedsFourSellerRefresh` — thiếu raw/ảnh; `hydratedForId`, generation guard, spec refs chống stale Color/Size; silent không spin nút Sync; local đủ → skip pull, vẫn mark remote done); auto-pricing sau remote done; merge khi có pull. Product Info paint ngay; Variations/gallery/Variant Image mount 1 frame sau first paint (`useDeferredMount` rAF + macrotask, không `requestIdleCallback`) + skeleton; Description tách `ListingDescriptionCard` + `React.lazy(HtmlEditor)`; `Form.preserve` giữ value khi section chưa mount. **Save Draft / Publish / Update:** `persist()` bật spinner (`setSaving(true)`) + yield 1 frame **trước** `form.validateFields()` / build payload (form lớn, validate đồng bộ nặng) → nút phản hồi tức thì; validation fail (inline hoặc `message.error` qua helper `fail`) tự tắt spinner. Shop-metadata bắt đầu ngay với `listing.shopId` (không chờ form hydrate). Card **Product Info** có combobox shop Publish (góc phải header) — bắt buộc trước Save Draft / Publish; gửi `shopId` local Guid. **Đổi shop** → reset Brand ẩn = `No brand` (`id=0`), xóa warehouse cũ, gọi `GET .../shop-metadata?shopId=` (cookie owner; brands+warehouses **song song** sau 1× is-login) → auto gán warehouse `isDefault` (fallback kho hiệu lực đầu tiên); **không hiện Brand / Warehouse picker**; Publish yêu cầu metadata shop sẵn sàng + `warehouseId` ẩn. **Size Chart / Product video:** file picker → `POST .../cos-upload` (4Seller COS) → URL CDN + `sizeChartId`/`erpVideoId` từ `localMaterialId`. Card **Description**: nút **AI Rewrite** → `POST .../rewrite-description` → ghi thẳng editor rồi `PUT .../description-prep` (HTML + cờ Rewrite). **Insert Images** (≤5 ảnh Default, 1 ảnh/dòng sau section) / **Hoàn tác** cũng gọi `description-prep`. Dưới editor: **Search terms** / **Product highlights** (`Select tags`, không bắt buộc) — Save Draft / Publish / Update gửi `searchTerms`/`productHighlights` (`string[]`, kể cả `[]`) → patch RawPayload JSON array. **Publish checklist:** cờ DB `descriptionRewritten` bắt buộc (thiếu → FE + API chặn Publish); `descriptionImagesInserted` tùy chọn — seller có thể chèn ảnh Default hoặc bỏ qua. FE còn chặn Publish khi **thiếu Size Chart** hoặc **còn ảnh lỗi** (ô đỏ: trùng `syncError` / `<img>` không tải) — `Modal.warning` + cuộn tới ô, không gọi thêm API. Sync đổi mô tả 4Seller → reset cờ. **Listing đã đăng lên shop** (`pageType=published`): ẩn **Save Draft**, đổi **Publish → Update** (gọi `/admin/listings/{id}/update` — chỉ cập nhật thông tin, không đổi Active/Inactive; giữ tab mở); thêm nút **Deactivate** khi `siteStatus=active` và **Activate** khi `siteStatus=inactive` (đều có `Modal.confirm`; đây là cách duy nhất đổi trạng thái listing). **Deactivate** thành công → `window.close()` (+ fallback `navigate` `/listings/published`), cùng UX đóng tab với **Save Draft** / **Publish**. Nút **Duplicate** (topbar edit + menu card list): `POST .../duplicate` → 4Seller copy + upsert draft local (giữ trang hiện tại). Nút **Sync** trên edit + menu **Đồng bộ** trên card draft: `GET …/listings/{id}?refresh=true` (kéo full detail từ 4Seller). Trang list `/listings` cũng có action **Activate** / **Deactivate** / **Duplicate** trong dropdown mỗi listing (Activate chỉ khi `siteStatus=inactive`, Deactivate chỉ khi `siteStatus=active`). Restore công thức batch pricing từ `GET` detail `appliedPricing` (DB) ưu tiên hơn `localStorage` `listing-applied-pricing:{id}` (cache optimistic) để preview giá sau giảm / promo / profit — **chỉ tham khảo UI**, không tính doanh số/profit báo cáo. **Sync từ 4Seller:** tự chạy nền sau hydrate local + nút **Sync** trên topbar (cùng `GET .../{id}?refresh=true`) → three-way merge form (chỉ patch field 4Seller đổi so với snapshot local DB, giữ edit user đang gõ; backfill Video/SizeChart thiếu từ remote; **reconcile typed media** — remote không còn chart/video thì form xóa theo; không đổi thì không re-bind). **Save Draft** → ghi `localStorage` ngay (kể cả khi push 4Seller fail) + `POST .../save-draft` (server vẫn lưu local DB sau 4Seller OK). **Publish** → trước khi push: nếu có variant `salePrice` ≤ `costPrice` + $10 thì `Modal.confirm` cảnh báo (ô giá bán tô đỏ khi ≤ giá nhập, vàng khi &gt; giá nhập nhưng ≤ +$10; vẫn cho Publish tiếp); rồi `POST .../publish` + xóa `localStorage` draft (local → `pending`/`UnderReview`, toast “đang chờ duyệt”; sync sau sẽ cập nhật status/`syncError`). Body form (`UpdateListingMirrorDto`): server refresh base 4Seller → patch giá trị form vào RawPayload → **`SanitizePayloadForFourSellerPush`** (khớp shape browser: `skuImgs` = JSON **string** Color-rows `[{attrName,img_url,custom_value}]` không phải object `color→url[]`; `isGlobal:0` thay null; `id` string; `productAttributes`/`brandInfo`/`option1|2|Value`/`productCertifications` stringify nếu đang object/array; bỏ `salePropImageMap` object; variation `mainImage` 1 URL + `attrs`/`keyId`/`type`) → push 4Seller → mới lưu local. Fail push → bỏ form edits trên DB (chỉ ghi `syncError`); browser draft vẫn giữ. Variation payload **phải mang `id` local** (và `fourSellerSkuId` khi có) — form giữ id qua hidden `Form.Item` + mount field cho row collapsed; `ApplyVariationReplace` **chỉ upsert, không xóa orphan**. Trước push, `ListingSyncService` validate payload (category, shop, warehouse — hydrate từ `stockInfoList` nếu thiếu, SKU/giá/tồn, ảnh, một mảng variant duy nhất) — lỗi trả HTTP 400 với `message` + `errors[]` (field-level). Push gửi toàn bộ `RawPayload` (đã deep-patch). TikTok detail dùng key **`variation`** — patch chỉ ghi **một** key variant và **xóa** các key trùng (`variationList`/`skuList`/…) để tránh `业务异常参数错误`; patch SKU giữ shape gốc + `stockInfoList[].available_stock` / `warehouse_id`. **Product Certification:** luôn upsert 3 Yes/No vào `productAttributes` (kể cả khi field đang là JSON string); `productCertifications` JSON string `"[]"` giữ nguyên. Lỗi 4Seller `业务异常参数错误` được dịch kèm gợi ý nguyên nhân. Thiếu raw → lỗi nghiệp vụ (đồng bộ lại trước). Cookie push = cookie của **owner listing**. UI `ListingEditPage`: `Modal.error` hiển thị `errors[]` chi tiết; `syncError` trên card Status. Golden shape: `docs/4seller-save-draft-golden-shape.md`.

**Khối sale-prop dựng lại từ variations (`ListingSalePropSync`).** Payload push là bản clone detail 4Seller trước đó, nên mọi thứ form đã bỏ (một màu, cả trục Size) sẽ **sống sót** nếu chỉ prune. Vì vậy trước khi push, BE dựng lại toàn bộ khối này từ tập variation thật:

- **Slot trục:** giữ nguyên slot mà payload đã khai (root `option{N}` = `{attrId: "Color"}`), trục mới mặc định Color→`option1`, Size→`option2`, Number of Items→`option3`. Cùng một map slot dùng cho cả per-SKU và root nên không bao giờ lệch nhãn Color/Size.
- **Per-SKU:** `option1/2/3` **luôn** được ghi lại theo `variantAttributes`, và **xóa hẳn** khi trục đó không còn (trước đây chỉ ghi thêm → SKU vẫn mang `option2 = "1XL"` sau khi bỏ trục Size). `attrs` luôn dựng lại = đúng các option label của chính dòng đó.
- **Root maps:** `option{N}` + `option{N}Value` dựng lại từ nhãn đang dùng — giữ `valueId` cũ cho nhãn đã tồn tại, tra `GET /api/meta/tiktok/get-category-attribute-list-by-id` cho nhãn/trục mới (**chỉ gọi khi thật sự thiếu id**), 4Seller không trả thì lấy **chính nhãn làm `valueId`**. Trục không còn SKU nào dùng → xóa cả `option{N}` và `option{N}Value`.
- **Hai map không cùng container:** `option{N}` là **object** `{"100000":"Color"}` còn `option{N}Value` là **mảng có thứ tự** `[{"1001063":"Black"},{"0XL":"0XL"}]` — cả hai đều là JSON string. Gửi `option{N}Value` dạng object thì `save-draft` vẫn trả success nhưng **trang edit 4Seller quay vòng mãi**. Giá trị TikTok chưa resolve dùng chính nhãn làm key (cùng kiểu placeholder với `{"Color":"Color"}`); có schema thì id số **thắng** key dạng nhãn. Map không đổi thì giữ nguyên chuỗi gốc, không ghi lại.
- **Giữ container `option{N}Value` gốc qua cả hai pass push.** `BuildPushPreviewAsync` / `PushAsync` gọi `CaptureValueShapes` trên RawPayload **trước** `PatchRawPayload`, truyền shape đó vào mọi lần `RebuildRootMaps` (kể cả `CompleteSalePropMapsAsync` khi schema nâng id TikTok cho Color/Items). Nhãn Color/Items có id không phải số (`{"White":"White"}`) được coi là gap để tra schema; Size tùy biến (`0XL`, `1XL`…) giữ key = nhãn.
- **attrId phải là id số.** Detail 4Seller echo lại `option1 = {"Color":"Color"}` (key là tên trục, không phải attribute id TikTok) — key không parse được thành số bị coi như **thiếu**, nên schema được tra để lấy `{"100000":"Color"}` đúng golden shape; schema không có thì mới giữ lại placeholder cũ để không mất trục. Payload sau push được lưu lại nên lần sau không phải tra nữa.
- **Đổi tổ hợp thì bỏ danh tính SKU:** khi phải tái dùng một raw row cho tổ hợp khác, BE xóa `id` / `keyId` để 4Seller tạo SKU mới thay vì đổi tên SKU đang sống.
- **Validate trước push** (400 + `errors[]`): mọi option label phải có trong `option{N}Value` sau rebuild, `attrs.Count` = số option non-empty của dòng, không dòng nào còn `option{N}` của trục đã xóa, không 2 dòng cùng tổ hợp, không thiếu / không trùng `sellerSku`.
- **`sellerSku` bắt buộc ở cả Save Draft**, không riêng Publish: BE chỉ ghi `sellerSku` vào payload khi non-empty, nên variant mã rỗng lên 4Seller thành SKU không MSKU → `Variant #N: thiếu sellerSku (MSKU)`. Combo mới (tick thêm chip Color/Size) chưa có mã, vì thế `validateVariationMatrix` ở FE chặn ngay và nhắc chạy **Batch SKU** thay vì để rơi xuống 400 của BE.

**`GET /api/v1/dev/listing-sync/push-preview/{listingId}?includePayload=false`** (`DevListingSyncDiagnosticsController`, `[AllowAnonymous]`, ngoài Development trả **404**) — dry-run payload: chạy đúng pipeline patch → rebuild sale-prop → sanitize nhưng **không** gọi 4Seller và **không** ghi DB (`AsNoTracking`). Trả `axisSlots`, `saleProps` (`option1`, `option1Value`, … — `option{N}Value` phải là chuỗi mảng), `skuImgs`, `variations[]` `{ index, skuId, sellerSku, option1..3, attrs, originalPrice, availableStock }`, `localVariations[]` để đối chiếu, `validationErrors[]`. `includePayload=true` trả thêm JSON đầy đủ.

**`GET /api/v1/dev/listing-sync/specifics/{listingId}`** (dev-only, ngoài Development **404**) — các dòng Specifics mà `RawPayload` của listing đang mang, mỗi dòng trả `field` (`productAttributes` / `attributes` / `attributeList`) + JSON thô, kèm `denylistCheck[]`: với mỗi dòng denylist cùng category, `sentValueId` là id mà payload này mang cho giá trị đó. Dùng để phân biệt hai ca của lỗi *cannot use custom value names*: có `sentValueId` nghĩa là payload đủ id (lỗi ở chỗ khác), không có nghĩa là mapping chưa resolve được id. Trả kèm `pageType` / `siteStatus` / `publishStatus` / `syncError` để đọc trạng thái cùng lúc.

**`GET /api/v1/dev/listing-sync/category-nodes?ids=601152,842632,903824&site=US`** (dev-only) — soi cây category đã mirror: mỗi node trả `isLeaf` / `level` / `path` / `parentExternalId` / `isActive` / `lastSyncedAt`, kèm `missing[]` (id không có trong bảng) và `childCounts[]`. Dùng khi nghi lỗi Specifics thật ra là lỗi category: TikTok chỉ nhận **leaf**, id không phải leaf làm 4Seller trả schema attribute chung chung và TikTok từ chối id trong đó.

Ghi chú UI (không đổi API): card **Variant Image** trên `ListingEditPage` — block **Default (mặc định)** + ảnh theo màu; kéo ảnh trong khối đổi thứ tự; kéo ảnh màu thả vào Default để thêm vào gallery mặc định; kéo handle ⋮⋮ trên khối màu đổi thứ tự Color (màu đầu = Main/Default variant) · thả khối màu lên **Default** để copy bộ ảnh màu đó thành ảnh mặc định sản phẩm. Khối Default cảnh báo khi chưa đủ 9 ảnh (`n/9` + Alert “phải đủ 9 ảnh mới lên Good”) — không chặn Save/Publish. Khi `syncError` chứa URL ảnh (publish image upload fail), FE lọc URL → khớp **content id trước `_thumbnail_Nx`** → bôi đỏ **chỉ khi identity đó vẫn còn ≥2 bản trong cùng scope** (Default / từng màu); xóa hết bản trùng thì badge biến mất, Alert chuyển info “Đã hết ảnh trùng — Lưu & Publish lại”. Tile `<img>` không tải (`onError`) cũng bôi đỏ. **Publish** chặn khi còn ảnh trùng hoặc ảnh không tải — `Modal.warning` giống thiếu Size Chart, cuộn tới ô đỏ. Nhấn URL trong Alert vẫn nhảy tới ảnh. Auto-dedupe hydrate cũng theo content-id (không chỉ tên file). **Perf xóa ảnh:** `Form.useWatch("images")` nằm trong `ImagesWatchIsolator` + optimistic/`startTransition` — tránh re-render cả trang (Variations/Specifics) khi bấm nút xóa. **Perf kéo ảnh:** `onDragOver` dùng `setVariantPhotoDragOverSafe` (chỉ setState khi đổi tile) — native `dragover` ~60fps nếu setState mỗi lần sẽ làm UI lag. **Live reorder UI:** kéo ảnh/gallery/khối màu → siblings trượt (`translateX/Y`) mở chỗ chèn, số thứ tự đổi theo slot; hit-test theo rect snapshot trước transform (tránh nháy đổi chỗ khi live-sort).

## Seller Dashboard Endpoints

Tổng quan ops cho seller-web (`/`) + trang thống kê `/stats/orders`, `/stats/revenue`, `/stats/seller-performance` (shared `@drop/stats`). Permission: `seller.dashboard.view`. Reuse `DashboardService` — **luôn ép `OwnerUserId` = JWT** (client không thể lọc seller khác). Cùng công thức aggregate / timezone VN như Admin Dashboard. UI seller: KPI Orders/Revenue/Cost/Profit/Margin (không Chi tiêu thẻ); trạng thái đơn + listings + shops; trends; heatmap ngày×shop rồi heatmap giờ, **mỗi card một hàng full-width**; finance theo shop; top 10 sản phẩm — bỏ pie/xếp hạng cross-seller. `/stats/orders` dùng `include=kpis,orderStatus,shopsFinance&metrics=orders` (không `sellers`) + `trends?includeListed=false&metrics=orders`. `/stats/revenue` dùng `include=kpis,shopsFinance` + `trends?includeListed=false&metrics=revenue`. `/stats/seller-performance` dùng `GET /dashboard/seller-performance` (self-scoped; UI tắt ranking/filter seller).

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/dashboard/summary` | `seller.dashboard.view` | Snapshot kỳ hiện tại (scoped JWT). Query giống admin (`period` / `fromDate`+`toDate` / `include` / `metrics` / `shopId` / `platform`) trừ `ownerUserId` bị ghi đè. Dùng bởi Dashboard + `/stats/orders` + `/stats/revenue` |
| GET | `/dashboard/trends` | `seller.dashboard.view` | Series orders/revenue/cost/profit/listed. `/stats/orders` truyền `includeListed=false` + `metrics=orders`; `/stats/revenue` truyền `includeListed=false` + `metrics=revenue` |
| GET | `/dashboard/order-heatmap` | `seller.dashboard.view` | Heatmap ngày × seller/shop (seller dim thường 1 hàng = chính mình) |
| GET | `/dashboard/order-hourly-heatmap` | `seller.dashboard.view` | Heatmap giờ 0–23 (`timezone=us\|vn`) |
| GET | `/dashboard/seller-performance` | `seller.dashboard.view` | Hiệu suất vận hành của seller hiện tại (cùng DTO admin). `ownerUserId` bị ghi đè JWT. Dùng bởi `/stats/seller-performance` |

## Seller Order Endpoints

Quản lý đơn của seller đăng nhập. Reuse `OrderSyncService`. **Luôn ép `OwnerUserId` = JWT**; thao tác theo id kiểm tra `Order.OwnerId` — không thuộc → 404. Không có DELETE local / Shein-sync (admin-only).

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/orders` | `seller.orders.view` | List scoped owner (`statusCounts` = `null`; kèm `nextCursor` keyset) |
| GET | `/orders/status-counts` | `seller.orders.view` | Badge đếm workflow tab, scope owner (`ScopeListToCurrentUser`); cùng filter list, bỏ paging/`cursor`/tab/`pendingFilter` |
| GET | `/orders/{id}` | `seller.orders.view` | Chi tiết + line items |
| GET | `/orders/{id}/preview` | `seller.orders.view` | Hover preview nhẹ (items + mua hàng + doanh thu) — dùng tooltip mã đơn trên `/fulfil/transactions`; 404 nếu không thuộc owner |
| GET | `/orders/shipping-services` | `seller.orders.view` | Carrier Mark Shipped (cookie JWT user; query `platform`, `refresh=true` force live) — cùng cache `PlatformShippingServices` TTL 24h |
| POST | `/orders/sync` | `seller.orders.manage` | **202** — sync 4Seller chỉ owner hiện tại (job detached); poll `GET /orders/sync/status/{jobId}` |
| GET | `/orders/sync/status/{jobId}` | `seller.orders.manage` | Tiến độ job; chỉ đọc được job mình start |
| POST | `/orders/{id}/mark-ship` | `seller.orders.manage` | Mark Shipped trên 4Seller — cùng core admin; lỗi cũng persist `autoMarkShip*` trên link carrier trước khi trả 4xx |
| POST | `/orders/{id}/sync-track` | `seller.orders.manage` | Sync track đơn sở hữu — cùng core admin `RunForOrderAsync` (fetch SHEIN detail + cost + Mark Ship) |
| POST | `/orders/{id}/sync-platform-status` | `seller.orders.manage` | Sync trạng thái sàn đơn sở hữu — cùng core admin `SyncPlatformStatusAsync` |
| PUT | `/orders/{id}/cost` | `seller.orders.manage` | Cập nhật phí NCC |
| PUT | `/orders/{id}/milestones` | `seller.orders.manage` | Mốc fulfil (`fulfilPurchasedAt` / `trackingAddedAt` / `clearFulfilPurchasedAt`; Transit/Delivered/Refund bị từ chối). Lần đầu `fulfilPurchasedAt` bởi user ≠ owner → notify seller như admin |
| PUT | `/orders/{id}/settlement` | `seller.orders.manage` | Đánh dấu thanh toán / chưa thanh toán |
| PUT | `/orders/{id}/usps-processed` | `seller.orders.manage` | Đánh dấu / bỏ đánh dấu xử lý USPS (`applyFee` mặc định true; `false` = chỉ stamp; clear stamp cũng clear `uspsScanBatchId`) |
| POST | `/orders/usps-scan/push` | `seller.orders.manage` | Đẩy track scan USPS (PrintPackShip create→send) — cùng core admin; **scoped owner** JWT (đơn người khác → skip `not_owned`) |
| PUT | `/orders/{id}/thank-card-processed` | `seller.orders.manage` | Đánh dấu / bỏ đánh dấu ThankCard (`applyFee` mặc định true; `false` = chỉ stamp) |
| PUT | `/orders/{id}/refund-processed` | `seller.orders.manage` | Đánh dấu / bỏ đánh dấu ops xử lý Refund (`refundProcessedAt`; không ledger) |
| PUT | `/orders/{id}/notes` | `seller.orders.manage` | Buyer / Seller / Internal notes → 4Seller + local |
| PUT | `/orders/{id}/message` | `seller.orders.manage` | Legacy → issue-tag workflow (giống admin) |
| GET | `/issue-tags` | `seller.orders.view` | Thư viện tag lỗi |
| POST | `/issue-tags` | `seller.orders.manage` | Tạo tag thư viện |
| PUT | `/issue-tags/{tagId}` | `seller.orders.manage` | Đổi nhãn thư viện |
| DELETE | `/issue-tags/{tagId}` | `seller.orders.manage` | Xóa tag custom (chặn system) |
| PUT | `/orders/{id}/issue-tags` | `seller.orders.manage` | Replace tag trên đơn sở hữu |
| PUT | `/orders/{id}/issue-status` | `seller.orders.manage` | Processing / Done — không bắt buộc đã gắn tag |

UI seller-web `/orders`: mirror admin (card + timeline + drawer + tabs workflow + sync/mark-ship/cost/notes/settlement; milestones chỉ `fulfilPurchasedAt` qua extension/Cập nhật phí); bỏ filter Seller và xóa đơn; shop filter từ `GET /shops`. (FE đã gỡ icon chat / `ChatSlidePanel` / bell; nút **Tag lỗi** trên card mở `UpdateMessageModal`, gắn tag dùng `./issue-tags`.)

## SignalR (fulfil-queue realtime)

Hub: `/hubs/notifications` (đường dẫn giữ nguyên để tương thích client; **chỉ phục vụ fulfil-queue** — đã gỡ toàn bộ inbox/chat/presence: không còn `/notifications*`, `/admin/notifications/system`, `GET/POST /orders/{id}/messages`, `GET/POST /admin/orders/{id}/messages`, event `notificationCreated` / `presenceChanged`, bảng `OrderMessages` / `DirectMessages` / `UserNotifications`; migration `20261001120000_DropChatAndNotifications`). JWT qua query `access_token`; extension fulfil thêm `fulfilSession=<Chrome profile UUID>` để track phiên. Event duy nhất: **`orderClaimChanged`** → `{ items[{ orderId, state, claimedByUserId, claimedByName, claimedAt, expiresAt, sessionCount, sessionIds, startedAt }], serverTime }` — broadcast group **`fulfil-queue`** (hub tự `AddToGroup` khi JWT có permission `admin.orders.view`). `state`: `held` \| `released` \| `purchased`. Nguồn: claim / release / stamp mua / logout `DELETE …/fulfil-claims/session` / orphan session (hub disconnect > **90s** — `IFulfilSessionTracker` + sweep tick 15s) / sweep TTL (`FulfilClaimExpirySweepService` ~1 phút). Extension KBT Fulfil side panel nghe `orderClaimChanged` (poll `GET …/fulfil-claims` ~30s chỉ fallback khi hub chưa Connected). Thông báo ra ngoài chỉ còn **Telegram** (`NotifyOrderMessageAsync` khi gắn issue tag). Issue tag (`…/issue-tags`, `…/issue-status`, `PUT …/message` legacy) vẫn hoạt động, chỉ không còn ghi `OrderMessages` / inbox / realtime.

## Seller Shop Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/shops` | `seller.shops.view` | Danh sách shop của seller hiện tại, theo `createdAt` ASC (tie-break `Id`); kèm `publishLimitPublished` / `publishLimitTotal` (số listing + limit thật từ TikTok `publish_limit`, sync extension) và `activeListingCount` = alias `publishLimitPublished` (không đếm listing local) + **`todayOnlineSeconds`** (tổng giây online **ngày VN hôm nay** từ `ShopOnlineSessions`; phiên đang mở tính đến UTC now; UI cột Online = `Xh Ym`) + **hồ sơ danh mục** (`categoryId`/`categoryName`, `ageMin`/`ageMax`, `priceMinUsd`/`priceMaxUsd`, `preferQuickShip`/`preferLocal`/`preferUsWarehouse`/`preferSheinClub`, `minRating`/`minSoldCount`/`maxVariantCount`, `bodySizeCode`, `styleCodes`) + subset profile TTS (`shopCode`, … trên detail) + per-shop auto publish (`autoPublishEnabled`, interval/min-draft-age) và **scheduler aggregate** khi system auto publish bật: `autoPublishSchedulerStatus` (`processing` \| `waiting`, null = không badge), `autoPublishNextPickAt` (UTC — mục tiêu countdown interval), `autoPublishEligibleDraftCount` (FIFO eligible — FE hiện `Queue +N` khi >1). **Không** trả `onHold*` (tiền ở `GET /shops/finance`) |
| GET | `/shops/finance` | `seller.shops.view` | Overview tài chính shop JWT-scoped. Query `status` (mặc định `Active`, `all` = mọi trạng thái), `from`/`to` (UTC inclusive, **chỉ** ledger tiền đã về; omit = all-time). Response `{ summary, items[] }`: On hold* + DELI = snapshot TTS trên Shop; `moneyReceived` = SUM Confirmed Credit−Debit theo `ShopId`; `pendingConfirmationAmount` = Pending hiện tại (không lọc ngày). `acquisitionCost` / `summary.acquisitionCostTotal` = giá mua shop từ sổ cái hệ thống (`Source=ShopAcquisition`, all-time, không theo `from`/`to`); seller chỉ đọc. UI seller-web `/shops/finance` (shared `@drop/shop-finance`, `SELLER_SHOP_FINANCE_CONFIG`). Click **Tiền đã về** → Drawer `GET /receiving-bank-accounts/transactions?shopId=` (cùng `from`/`to`) |
| GET | `/shops/scraper-options` | `seller.shops.view` | Shop **Active** + có `fourSellerShopId` cho Browser Extension: `profileName`, `name`, platform avatar, `productCategories` (`{ label, value }[]` TTS), **hồ sơ danh mục** (`categoryId`/`categoryName`, tuổi/giá, QuickShip/Local/US Warehouse/`bodySizeCode`/`styleCodes`/`minRating`/`minSoldCount`/`maxVariantCount`/`preferSheinClub`), `activeCount` (siteStatus=active; không còn `publishedCount`). Extension gọi lúc login / mở panel **không** phụ thuộc cookie 4Seller. |
| GET | `/shops/{id}` | `seller.shops.view` | Chi tiết shop (kèm hồ sơ danh mục + TikTok profile: EIN, address, owner, shipping, `productCategories` TTS, listing limit TTS, TZ, verified…) + điểm Health Center (`ahrScore`, `shopPerformanceScore`, `productSatisfactionScore`, `fulfillmentScore`, `customerServiceScore`, `tikTokShopScoreSyncedAt` — snapshot từ `POST /shops/tiktok-shop-scores/sync`). **Không** trả `onHold*` — tiền ở `GET /shops/finance` |
| PUT | `/shops/{id}` | `seller.shops.edit` | Cập nhật shop được cấp. **Không** ghi hồ sơ danh mục / `ProductCategories` (tránh đè khi lưu form ops). Đổi `status` → ghi `ShopOperationalPeriods` + Telegram cảnh báo đổi trạng thái (source `seller`). Đổi `fourSellerShopId` → tự rematch `Order.ShopId` cho đơn đã sync theo id đó (xem `POST /admin/shops`). |
| GET | `/age-ranges` | `seller.shops.view` | Lookup khoảng tuổi preset (seed): `{ id, code, label, ageMin, ageMax, painPoint, desire, productGroups, sortOrder }` — UI Select hồ sơ danh mục; shop vẫn lưu `ageMin`/`ageMax` (không FK). |
| PUT | `/shops/{id}/catalog-profile` | `seller.shops.edit` | Hồ sơ danh mục vận hành: `categories` **bắt buộc** (1–20, `{ id, name }`, active `TikTokCategories` cùng `shop.Region` — **parent hoặc leaf**). Server ghi path hiển thị từ catalog; `CatalogCategories` jsonb; `CategoryId`/`CategoryName` denormalize (id đầu + tên nối ` · `). Legacy body `categoryId` (+ `categoryName`) vẫn nhận khi `categories` rỗng. Tuổi/giá/cờ QS/Local/US WH/Shein Club + `bodySizeCode` + `styleCodes` + `minRating`/`minSoldCount`/`maxVariantCount` (UI Select khoảng tuổi từ `GET /age-ranges` → map `ageMin`/`ageMax`; Select body/size hardcode `regular|plus|midsize|curvy|petite|tall`; Select style **multiple** hardcode `elegant|casual|vintage|sexy|boho|cute|glamorous|vacation|modest|party` (đúng 10 facet Style Shein đã có `attr_id`), `[]` = clear; bộ lọc sản phẩm: rating ≥ / sold ≥ / variants < với **0 = không lọc**; Checkbox QS/Local/US Warehouse/Shein Club chọn nhiều). Response slim `ShopCatalogProfileDto` (`categories` + id/name + flags). Không đụng ops / `ProductCategories`. Đổi `Region` → xóa toàn bộ category (tuổi/giá/cờ/style/bộ lọc giữ). **Auto publish:** dùng `PUT …/auto-publish` (không gửi qua catalog-profile từ UI Shops). |
| PUT | `/shops/{id}/auto-publish` | `seller.shops.edit` | Cấu hình auto publish per-shop: `{ autoPublishEnabled, autoPublishIntervalSeconds?, autoPublishMinDraftAgeMinutes? }`. Bật yêu cầu Settings `listing.auto_publish.enabled=true` + shop `Active`. **`autoPublishMinDraftAgeMinutes` ≥ 5** (mặc định hệ thống `listing.auto_publish.default_min_draft_age_minutes`, seed/migration clamp giá trị cũ &lt; 5). **Timing publish:** khoảng cách giữa các lần thử = `autoPublishIntervalSeconds` (hoặc default `listing.auto_publish.default_interval_seconds` khi shop không override); background scheduler adaptive poll 5–30 giây, **không** dùng `listing.auto_publish.scheduler_interval_minutes` (legacy, bỏ qua). **Eligibility slot:** khi đã có `publishLimitTotal`, shop bị bỏ khỏi pick nếu `publishLimitPublished` ≥ total (cùng số cột Listing Shops / Shop Tasks từ TTS `publish_limit`; **không** COUNT listing local `siteStatus=active`); **thiếu total vẫn pick**. 4Seller chấp nhận publish → tăng lạc quan `publishLimitPublished` (+1, cap total) cho đến lần sync profile TTS ghi đè số thật. Fail `Listing.Back.Day_Limit` → `AutoPublishBlockedUntil` tới nửa đêm VN (không skip listing). **Dispatch tách khỏi pipeline:** mỗi vòng chỉ pick + bump `LastAutoPublishAt`, pipeline chạy nền — shop interval hết có thể pick listing kế tiếp không chờ shop khác. Concurrency system chỉ còn `listing.auto_publish.max_concurrent_publishes` (trần pipeline publish toàn VPS); mỗi vòng quét **toàn bộ** shop eligible, backfill ngay khi slot free qua `IListingAutoPublishDispatchTrigger`. Response slim `ShopAutoPublishDto` (`id` + 3 field). UI ShopsPage: icon ⚡ trên bảng → modal compact (không nằm form Sửa / Danh mục). |
| GET | `/shops/auto-publish/system-config` | `seller.shops.view` | Giống admin — gate UI per-shop switch. |
| GET | `/shops/auto-publish-scheduler-status` | `seller.shops.view` | **Poll nhẹ badge scheduler** trên Shops list. Query `ids[]` (Guid, tối đa 200). Chỉ shop `autoPublishEnabled` thuộc JWT; dùng `ShopAutoPublishSchedulerStatusResolver` — **không** join listing/variation. Response `{ items[{ id, autoPublishSchedulerStatus, autoPublishNextPickAt, autoPublishEligibleDraftCount }] }`. FE patch cache list 12s khi còn `processing`/`waiting` — không reload full list / không spin overlay bảng. |
| GET | `/shops/tiktok-unified-sync/config` | `seller.shops.edit` | **Extension Sync All / Sync Fast:** Settings nhóm **Sync All TTS (Extension)** → `{ info, finance, keywords, analytics, healthCenter, shopScore }` (`extension.tiktok_unified_sync.step_*`, boolean, mặc định `true`; thiếu key → bật). Tách biệt Auto Sync `extension.tiktok_*_sync.enabled`. Bước tắt → extension `skipped` + `POST /shops/tiktok-sync/log`, không scrape. Bước Nhiệm vụ (`refreshDailyTaskPlan`) không có toggle — luôn chạy cuối Sync All. |
| GET | `/shops/tiktok-profile/sync-config` | `seller.shops.edit` | **Extension Auto Sync profile:** Settings → `{ enabled, intervalSeconds }` (`extension.tiktok_shop_sync.enabled` + value/unit). |
| PUT | `/shops/tiktok-profile` | `seller.shops.edit` | **Extension sync:** body ≈ `TikTokShopInfo` (`sellerId` bắt buộc). Khớp `(OwnerId=JWT, ExternalShopId=sellerId)`; không thấy → 404. Ghi đè field profile khi có giá trị; **`accountEmail`/`accountMobile` → `Email`/`NewPhoneNumber`**: chỉ fill khi trống hoặc nâng cấp từ giá trị mask (`*`, vd. `i***o@…` / `+1****4409`) — **không** ghi đè contact thật, **không** lưu chuỗi mask, **không** đụng `TiktokPassword`; set `tikTokProfileSyncedAt`; tính `dayLive` từ `createTimeMs`; lưu **`settlementBankAccountNumber`** (masked) + method/beneficiary/type/status — **không** đè `newBankNumber` thẻ nhận tiền; lưu snapshot Finance **`onHoldAmount`** / **`onHoldInTransitAmount`** / **`onHoldAwaitingSettlementAmount`** / **`onHoldReserveAmount`** (cùng lúc khi có `onHoldAmount`) và **`deli`** từ `bill_finish_period_in_days`; **tự set `shopUrl`** = `https://www.tiktok.com/shop/store/{slug}/{sellerId}`. **Ops `Status`** (`Active` · `Deactivated` · `Suspended`): `identityVideoVerificationLock: true` (products/list key `post_onboarding_identity_video_verification_reason`) → luôn **`Suspended`** (kể cả TTS 2 Active / 3 Deactivated). Không thì **worse-of** `sellerStatus` (`seller.status`) và `shopStatus` (`shop_status`): nếu **một trong hai** là **3**/`4` → `Deactivated`, còn lại **2** → `Active`. Flag false/thiếu **không** unsuspend; shop `Suspended` + TTS vẫn = 2 giữ nguyên. Ghi `ShopOperationalPeriods` khi Active↔Deactivated; đổi status còn bắn Telegram (setting `extension.tiktok_shop_sync.notify_status_change`, kèm shop code / sellerId / region / TTS status / URL) (tái sử dụng kỳ đang mở; không 500 nếu unique open-period). **Không** tạo shop. Sau lưu: nếu email/mobile/`settlementBankAccountNumber` **đã có cũ** và khác mới → Telegram (Settings toggle); nếu đuôi 4 số bank mới không khớp `ReceivingBankAccounts` active → Telegram last4. Chat: `extension.tiktok_shop_sync.telegram_chat_id` (mặc định `-1004331688585`). |
| GET | `/shops/tiktok-payouts/sync-config` | `seller.shops.edit` | **Extension Finance Auto Sync:** Settings → `{ enabled, intervalSeconds, maxDate, notifyNewPayment }` (`extension.tiktok_payout_sync.*`). `maxDate` = ISO `yyyy-MM-dd` (null nếu chưa set). |
| GET | `/shops/tiktok-payouts/cursor` | `seller.shops.edit` | Cursor incremental: query `externalShopId` hoặc `sellerId` → `{ latestOccurredAt, latestExternalTransactionId }` (TikTokPayout ledger mới nhất của shop owner). |
| POST | `/shops/tiktok-payouts/sync` | `seller.shops.edit` | **Extension Finance:** body `{ externalShopId|sellerId, payments[{ paymentId, amount, currency?, successTime?, bankCardMask?, note?, orders[{ tradeOrderId, settlementAmount?, earningAmount?, fees?, settlementTime? }] (settlementTime=bill_period ms — Statement date Pacific) }] }` (max 200). Upsert `ReceivingBankTransaction` `Source=TikTokPayout`, `ExternalTransactionId=paymentId`, Credit; `ConfirmationStatus=Confirmed` khi last-4 `bankCardMask` khớp thẻ shop, **Pending** nếu lệch/thiếu (admin duyệt / Không duyệt; re-sync không ghi đè Rejected/Confirmed). Persist `PayoutDetailsJson` từ `orders[]`; shop phải gắn thẻ nhận tiền. Mark `Order.IsSettlementPaid` + `TikTokPaymentId` khi `PlatformOrderId` khớp (không tạo SettlementPaid ledger — tránh double-count). Telegram payout mới nếu setting bật. |
| GET | `/shops/tiktok-opportunities/sync-config` | `seller.shops.edit` | **Extension Auto Sync keywords:** Settings → `{ enabled, intervalSeconds }` (`extension.tiktok_opportunity_sync.*`). |
| GET | `/shops/tiktok-opportunities/sync-context` | `seller.shops.edit` | Niche scrape: query `externalShopId` hoặc `sellerId` → khớp `(OwnerId=JWT, ExternalShopId)`. `{ shopId, hasCatalog, categoryInfos[{ cateId, level }], categoryInfoGroups[{ cateId, level }[]], catalogNames[] }`. Mỗi phần tử `CatalogCategories` → **một group** (node đã chọn, `cateId` = TikTok `ExternalId`, `level` = `TikTokCategory.Level + 1`). Extension scrape **từng group** `POST seller lead/list` rồi gộp OR — không nhồi nhiều leaf vào một `category_infos` (TikTok hiểu đó là 1 path); **không** AND `tag_code` hot_search+low_competition; `lead_list`/`data` null hoặc `{}` = 0 keyword group đó (bỏ qua, scrape group sau — chỉ fail khi 0 keyword và không group nào parse được). `categoryInfos` = group đầu (compat). Category thiếu trong catalog site `shop.Region` bị bỏ. `hasCatalog=false` khi niche rỗng hoặc 0 category resolve được — extension **không** scrape / **không** xóa snapshot. |
| POST | `/shops/tiktok-opportunities/sync` | `seller.shops.edit` | **Extension Keywords:** body `{ externalShopId|sellerId, items[{ leadId, leadName, picUrl?, picUrls[]?, searchVolume, onlineProducts?, onlineProductsRaw?, searchVolumeRingRatio?, onlineProductsRingRatio?, orderCount?, orderRaw?, orderRingRatio?, gmv?, gmvRaw?, gmvRingRatio?, categories[{ cateId, cateName, level? }]?, level1CateId/Name?, level2CateId/Name?, level3CateId/Name?, tagCodes[], leadTagTexts[]?, leadSourceTag?, leadClusterSourceTag?, opportunityType? }] }` (max 500; `picUrls` max 8; `categories` max 20; `leadTagTexts` max 8 từ `lead_demands[].lead_tag_text` — string hoặc object i18n `{ text, starling_key }`, extension lấy text hiển thị). Canonical `ProductOpportunities` unique `LeadId` toàn hệ thống + `ProductOpportunityShopLinks` theo shop: trùng `lead_id` **không insert** row mới (refresh metrics tại chỗ); shop chỉ thêm/gỡ link — **không gỡ link** keyword user đã bookmark (`ProductOpportunitySavedKeywords`) dù không còn trong payload scrape mới. Response `{ shopId, inserted, linked, skipped }`. Stamp `Shops.ProductOpportunitiesSyncedAt`. Extension chỉ POST khi scrape **đủ mọi trang** thành công. Backend **từ chối** (không đụng link) nếu shop chưa gán niche / 0 category resolve được. |
| POST | `/shops/tiktok-listing-analytics/sync` | `seller.shops.edit` | **Extension Analytics:** body `{ externalShopId\|sellerId, runId?, preset? (`last7days`), rangeStart?/rangeEnd? (`yyyy-MM-dd` theo giờ shop Pacific), items[{ productId, productName?, mainImage?, currency?, gmv?, orders?, impressions?, clicks?, ctr? (phân số), addToCartUnits?, addToCartBuyers?, gmvRatio?/ordersRatio?/impressionsRatio?/clicksRatio?/ctrRatio?/addToCartUnitsRatio?/addToCartBuyersRatio? (phân số, % so **kỳ 7 ngày trước cùng độ dài** — extension tính từ `stats_v3.total_prev` của Compass), rawStats? (`stats_v3.total` đã nén money object còn `amount`) }] }` (max **3000** item). **Mỗi item = tổng cửa sổ `last7days`**, không phải số một ngày. Upsert `ListingAnalyticsSnapshots` theo unique **(ShopId, TikTokProductId, SnapshotDate)** — `SnapshotDate` do **server** quyết định = ngày **Việt Nam** (không tin client), nên bấm Analytics nhiều lần trong ngày chỉ **ghi đè** số liệu + `SyncedAt` chứ không thêm dòng. Sync mỗi ngày lịch = cửa sổ 7 ngày lùi 1 ngày. `ListingId` link cơ hội theo `TikTokProductId` (ưu tiên listing cùng shop, sau đó listing sync gần nhất của owner); chưa mirror thì để null và lần sync sau tự link. `productId` trùng trong cùng payload → tính vào `skipped`. Response `{ shopId, snapshotDate, inserted, updated, linked, skipped }`. Sau sync ghi `TikTokShopSyncLogs` khi có `runId`. |
| POST | `/shops/tiktok-health-records/sync` | `seller.shops.edit` | **Extension Health Center:** body `{ externalShopId\|sellerId, runId?, items[{ recordId, recordType (`violation`\|`warning`), reason?, detail?, categoryType?, actions[{ title, actionStartTime?, actionEndTime? (unix seconds từ `action_informations`) }], recordStatus, createTime (unix seconds) }] }` (max **200**). Upsert `TikTokShopHealthRecords` unique **(ShopId, RecordId)** — record mới insert, record đã có update status/actions; payload trùng `recordId` trong cùng request → `skipped`. Extension scrape `POST growth_center/shop/violation/records/list` + `warning/records/list` (paginate, cap 100/loại). Response `{ shopId, inserted, updated, skipped }`. Ghi `TikTokShopSyncLogs` type `health_center` khi có `runId`. |
| POST | `/shops/tiktok-shop-scores/sync` | `seller.shops.edit` | **Extension Shop Score:** body `{ externalShopId\|sellerId, runId?, ahrScore? (0–1000), performanceScore?, productSatisfactionScore?, fulfillmentScore?, customerServiceScore? (0–5) }` — cần **ít nhất một** điểm, không thì 400. Ghi **snapshot** lên `Shops` (`AhrScore`, `ShopPerformanceScore`, `ProductSatisfactionScore`, `FulfillmentScore`, `CustomerServiceScore`, `TikTokShopScoreSyncedAt`); điểm null **giữ nguyên** giá trị cũ (extension gửi partial khi một request TikTok fail). SPS clamp 0–5 + làm tròn 1 chữ số (cột `numeric(3,1)`), AHR clamp 0–1000. Shop chưa gán cho owner → 404 kèm tên owner đang giữ seller id. Extension scrape `GET account/ahr/score` (`data.score`) + `GET seller/experience-score/score-overview` (`overview.score` + `metric.metricGroups[]` khớp `code` `020100`/`020200`/`020300`, fallback `groupName`). Response `{ shopId, updatedFields[] }`. Ghi `TikTokShopSyncLogs` type `shop_score`. |
| POST | `/shops/tiktok-sync/log` | `seller.shops.edit` | **Extension:** ghi log `skipped`/`failed` khi chưa gọi sync endpoint (thiếu tab seller-us, thiếu `sellerId`, …). Body `{ runId, externalShopId?, syncType (`profile`\|`payout`\|`opportunity`\|`analytics`\|`health_center`\|`shop_score`), status (`skipped`\|`failed`), errorMessage? }`. Response `TikTokShopSyncLogDto`. Log **không** tự xóa (thống kê sau). |
| GET | `/shops/tiktok-sync/logs` | `seller.shops.view` | Lịch sử sync extension của owner JWT. Query `externalShopId?`, `syncType?`, `page` (mặc định 1), `pageSize` (mặc định 50, max 100). Response `{ items[], total, page, pageSize }` — mỗi item: `runId`, `syncType`, `status` (`completed`\|`failed`\|`skipped`), `syncedAt`, `errorMessage?`, `shopId?`, `externalShopId`. |
| POST | `/shops/tiktok-online/heartbeat` | **AllowAnonymous** | **Extension (ngầm):** đếm phiên online khi còn tab `seller-us.tiktok.com`. Body `{ externalShopId, clientSessionId, startedAt, lastSeenAt }` (ISO UTC). Khớp shop theo `ExternalShopId` (ưu tiên `Active`); **không** JWT / không OwnerId. Mở phiên hoặc đẩy `LastSeenAt` (monotonic). Phiên mở khác shop cùng lúc → đóng (`Explicit`). `LastSeenAt` cũ hơn 3 phút → đóng (`HeartbeatTimeout` tại `LastSeenAt`). Qua nửa đêm VN → đóng ngày cũ (`DayBoundary`) + mở phiên mới với `clientSessionId` mới (extension phải nhận `clientSessionId` trong response). Response `ShopOnlineSessionDto` `{ id, shopId, externalShopId, clientSessionId, startedAt, lastSeenAt, endedAt?, closeReason?, isOpen }`. |
| POST | `/shops/tiktok-online/close` | **AllowAnonymous** | **Extension:** đóng phiên. Body `{ externalShopId, clientSessionId, endedAt, reason? }` (`TabClosed` \| `BrowserRestartReconcile` \| `HeartbeatTimeout` \| `DayBoundary` \| `Explicit`). Idempotent nếu đã đóng. `endedAt` clamp ≥ `StartedAt` và ≥ `LastSeenAt`. Chrome kill không gọi được close → local `kbt_tts_online_session.lastSeenAtMs` + `onStartup` reconcile `BrowserRestartReconcile`. |
| GET | `/product-opportunities` | `seller.product_opportunities.view` | Paged keyword **unique `lead_id`** của JWT (`OwnerId` luôn ép, bỏ `ownerId` query) qua EXISTS shop-link. Khi lọc `shopId`, list **cộng bookmark không có snapshot** (đề xuất «Shop này bán gì») — cùng `ProductOpportunitySavedKeywords` với GET saved-keywords của extension. Query `shopId`, `q`, `saved` (true = chỉ keyword **đã lưu của shop đang lọc**; không lọc shop = bookmark của shop đang link), `leadTag` (CSV hoặc lặp; OR — keyword có bất kỳ `lead_tag_text`), `page`/`pageSize`, `sort` (`volume` mặc định / `online` / `order` / `gmv` / `name` / `synced`), `sortDir` (`desc` mặc định cho volume/online/order/gmv/synced; `asc` mặc định cho name). `online`/`order`/`gmv` null luôn xếp cuối. Response `{ items, total, pageCurrent, pageSize, lastSyncedAt, leadTagOptions[] }`. Item: `picUrl` + `picUrls[]`, `categories[]`, `leadTagTexts[]`, `isSaved` **theo shop của dòng**, `shopCount` (số shop trong scope đang link), volume/product/order/gmv (+ raw + ring ratio). Không lọc shop: shop đại diện = link sync gần nhất. UI bảng ẩn cột Order/GMV (cùng giá trị trên mọi keyword). Mobile: chỉ Keyword + Volume + Product (danh mục · profile · shopName gộp meta dưới keyword); không checkbox/bulk-delete. `leadTagOptions` = distinct nhãn trong phạm vi shop/owner/`q`/`saved` (không hẹp theo `leadTag` đang chọn). `lastSyncedAt` = max `Shops.ProductOpportunitiesSyncedAt` theo shop/owner (không phụ thuộc `q`). UI seller-web `/product-opportunities` (shared `@drop/product-opportunities`, `SELLER_PRODUCT_OPPORTUNITIES_CONFIG`). |
| POST | `/product-opportunities/bulk-delete` | `seller.product_opportunities.view` | Gỡ keyword đã chọn. Body `{ ids: Guid[], shopId? }` (1–200). Seller: unlink shop JWT (`shopId` nếu đang lọc, không thì mọi shop của seller). Keyword gốc chỉ xóa khi **0 shop** còn link. Cùng transaction: gỡ bookmark `ProductOpportunitySavedKeywords` **của shop bị unlink** (shop khác giữ bookmark riêng); gỡ keyword khỏi jsonb `ShopKeywordResearches` của **shop bị unlink** (`listNow`/`upcoming` — giữ `analysisPoints` / weather / categories). Response `{ deleted }`. |
| PUT | `/product-opportunities/{id}/saved` | `seller.product_opportunities.view` | Lưu / bỏ lưu keyword **theo shop**. Body `{ saved: bool, shopId? }`. `shopId` = shop đang lọc (UI luôn gửi); thiếu thì shop đại diện (link sync gần nhất). Bảng `ProductOpportunitySavedKeywords` unique `(ShopId, KeywordNormalized)` — **không** mất khi sync snapshot; hai shop cùng `lead_id` bookmark độc lập. Bỏ lưu: chỉ xóa bookmark của shop đó; snapshot + item «Shop này bán gì» **giữ** (GET hydrate `isSaved=false`). Response `{ saved }`. |
| PUT | `/product-opportunities/saved-keywords` | `seller.product_opportunities.view` | Lưu / bỏ lưu **theo chữ keyword** khi không có dòng snapshot (đề xuất AI). Body `{ shopId, keyword, saved }`. Shop phải thuộc JWT. Unique `(ShopId, KeywordNormalized)` — không đè bookmark shop khác. Bỏ lưu: xóa bookmark shop đó; **nếu shop không còn snapshot keyword đó** thì gỡ khỏi `ShopKeywordResearches` `listNow`/`upcoming` (đề xuất AI không để orphan trong jsonb). Response `{ saved }`. |
| GET | `/product-opportunities/saved-keywords` | `seller.product_opportunities.view` | **Browser Extension Tìm sản phẩm:** list nhẹ keyword đã lưu **của đúng shop** (`?shopId=`). Shop phải thuộc JWT. Chỉ row `ProductOpportunitySavedKeywords.ShopId = shopId` (không inherit bookmark shop khác dù cùng chữ / cùng snapshot). Join snapshot shop (`picUrl`, `leadTagTexts`, `searchVolume`) ∪ `rationale`/`fit`/`researchLane` từ `ShopKeywordResearches` nếu keyword trùng (`listNow` = List ngay, `upcoming` = Đón đầu; trùng cả hai → `listNow`). Snapshot chỉ lấy các keyword đã bookmark (không load cả bảng shop). Không trả order/gmv/`picUrls[]`. Response `{ items: [{ keyword, picUrl?, leadTagTexts[], searchVolume, rationale?, fit?, researchLane?, mustPhrases: [{ phrase, anyOf[] }]?, shouldPhrases?, minDistinctiveHits }] }` (max 200, sort volume desc). `mustPhrases`/`shouldPhrases`/`minDistinctiveHits` stamp lúc GET theo **cổng độ phủ** (`KeywordMatchPhrases`, không tin AI `mustTokens`). must = tối đa 2 từ nhu cầu (material → print → silhouette → construction → length) + loại SP; modifier thừa + hardware/style vào should (điểm). Cổng: loại SP (nếu có) + ≥ `ceil(N/2)` trên tập đã cắt (N≤2 → thường ≥1 từ nhu cầu) — không AND-all. `anyOf` = đồng nghĩa denim = jean, off-shoulder = bardot, ruffled = ruffle. Điểm title = trọng số cụm khớp / tổng (loại SP + định danh chưa phân loại = 2, length = 1.5, style/hardware = 1). **Sort by Shein** (Recommend / Most Popular / …) là tùy chọn client extension (`kbt_shein_search_sort` → Recommend: `source=insideFilter` không `sort`; còn lại `source=sort`+`sourceStatus=4`+`sort=N` trên pdsearch/BFF — không gắn `sort` khi còn `insideFilter`), không field API. Extension gom list: trang 1 mở `pdsearch` bằng URL trình duyệt (SPA tự ký); trang 2+ `history.replaceState` `?page=N` (không load trang) rồi XHR MAIN-world `get_products_by_keywords` (overlay `keywords`/`page`, Shein tự ký theo URL mới) — không `tabs.update` từng trang. Gom hết trang → lọc catalog (rating/sold/Local thiếu field thì bỏ qua, không loại) → chấm toàn bộ pool (title 40% không loại cứng) → cắt `limit`. |
| GET | `/product-opportunities/shop-research` | `seller.product_opportunities.view` | Bản **Shop này bán gì** đã lưu (`?shopId=`). Shop phải thuộc JWT. **Không** gọi OpenAI. `data=null` khi chưa từng phân tích. Hydrate `isSaved` **theo shop** + rematch id/volume theo keyword snapshot hiện tại; strip đuôi `vol N` khỏi `rationale` (trùng Vol trên hàng drawer). Mỗi item có `keyword` (cụm TTS gốc) + `rank` (1-based trong lane, restamp sau hydrate/xóa) + `score` 0–100 / `scoreBreakdown` `{ audience (0–45), season (0–5), demand (0–30), growth (0–15), competition (0–5) }` (null trên payload cũ hoặc `audienceSuggest` đến khi Phân tích lại; `growth` thiếu trên payload trước đợt đổi công thức — UI ẩn dòng đó) + `searchQuery` / `sheinUrl` (câu tìm nguồn Shein — prefix `plus size` / `petite` / `tall` / `curvy` từ **`BodySizeCode` shop hiện tại**, không cần Phân tích lại; không gắn sort/price trên URL) + `mustTokens` / `mustPhrases` / `shouldPhrases` / `minDistinctiveHits` (GET/POST stamp cổng độ phủ: tối đa 2 từ nhu cầu + loại SP, ngưỡng `ceil(N/2)`, không tin AI; chip trên drawer). `analyzedAt`; `snapshotChanged=true` khi snapshot mới hơn lúc phân tích (vẫn trả bản lưu). `source` = `snapshot` \| `audienceSuggest` (payload cũ không có field → `snapshot`). |
| POST | `/product-opportunities/shop-research` | `seller.product_opportunities.view` | **Shop này bán gì** — body `{ shopId }`. Shop phải thuộc JWT + đã gán `CatalogCategories`. **Có snapshot:** BE chấm toàn bộ snapshot: loại generic ngắn (≥3 token, ≥2 token đặc trưng); audience (garment family + tuổi/body/style; clamp ≤0 khi sheer/mesh/bodycon/boho lệch style) là **cổng chặn** — `aud ≤ 0` bị loại khỏi shortlist, **không** còn là khóa xếp hạng. Rank + badge dùng **cùng một** điểm blend `score` 0–100 = khớp ngách 45 + nhu cầu 30 + tăng trưởng gần đây 15 + mùa 5 + cạnh tranh 5, trừ phạt lệch tệp (≤40) / trái mùa (≤15) / search co lại (≤10) / trùng listing (10). Nhu cầu = percentile volume trong **chính snapshot shop** (mid-rank) 65% + log tuyệt đối (trần 1M) 35% + nudge lead-tag ≤0.10 → đơn điệu theo volume, không bão hòa. Tăng trưởng từ `searchVolumeRingRatio` (+60% là tối đa; ring âm mất điểm và bị trừ). Audience cộng điểm theo bucket có trần (guidance 12 / body 12 / style 14) nên keyword nhồi tính từ không vượt keyword volume cao đúng ngách. Shortlist `aud>0` rồi **khóa** `listNow`/`upcoming` theo `Score`→`Volume`→`LeadName`. Mỗi item stamp `rank` + `score` + `scoreBreakdown` (`audience`/`season`/`demand`/`growth`/`competition`) cho tooltip drawer. Context model có `SKIP_THESE` hai nhóm không id: `off-audience` và `on-niche but scored lower` để `analysisPoints` giải thích keyword volume cao bị bỏ. OpenAI viết `analysisPoints` + `rationale`/`fit` bám công thức nhu cầu (temperature 0.2), không được đổi id. Backend **stamp `mustTokens`/`mustPhrases`/`minDistinctiveHits` theo cổng độ phủ** (tối đa 2 từ nhu cầu + loại SP, `ceil(N/2)`; không AND-all; không tin output model cho cổng title) rồi persist trong payload. **Snapshot rỗng, toàn generic, hoặc shortlist `aud>0` rỗng:** không 400 — rơi xuống nhánh đề xuất tệp khách (`source=audienceSuggest`), giữ `snapshotCount`/`droppedGeneric`/`candidatesScored` để drawer giải thích, warning *Chưa có snapshot…* / *Snapshot chỉ còn keyword generic…* / *Snapshot không còn keyword khớp ngách…*. **Không snapshot (và fallback trên):** OpenAI đề xuất keyword TikTok Search theo tệp khách (category/tuổi/body/style + AgeRange + mùa/sự kiện + listing sample), không ghi `ProductOpportunities`. Validate 2–8 từ, loại generic/`IsGeneric`, không trùng listing; shop `plus`/`petite`/`tall` bắt buộc token size tương ứng (`plus size` / `petite` / `tall`); xin model thừa +4 keyword vì lọc generic. `listNow` mục tiêu `min(max(5, remainingSlots hoặc 5), 8)` (không slot → 5); `upcoming` ≤ 5. Đủ ≥5 list ngay thì trả; sau retry vẫn trả phần còn lại (không 400 vì thiếu đúng 8+5). Chỉ 400 khi 0 keyword hợp lệ **sau** nhánh đề xuất, hoặc shop chưa gán danh mục. Thiếu age/body/style vẫn chạy kèm `warnings[]`. Response thêm `source`. Mỗi keyword item có `searchQuery` + `sheinUrl` (hydrate từ `bodySize` live, giống GET). Ghi đè 1 row `ShopKeywordResearches` theo shop (jsonb). UI Drawer: GET trước; thiếu bản lưu thì POST một lần; **Phân tích lại** mới gọi OpenAI. Drawer hiện `#rank` + badge `score` (tooltip breakdown); lỗi POST hiện trên drawer cả khi còn bản lưu cũ. Bookmark đề xuất dùng `PUT …/saved-keywords`. |
| POST | `/shops/{id}/what-to-sell-today` | `seller.shops.view` | **Hôm nay bán gì?** — bestsellers (7→30 ngày) kèm **nhãn momentum** (`RISING`/`FLAT`/`FALLING`/`NEW`, so kỳ trước cùng độ dài) + `avgSalePrice` (từ `OrderItem.ItemPrice`) + **listing active 0 đơn** (`NO_SALES`, cấm gợi ý lặp) + sample listing + `productCategories` (labels TTS) + `todayUtc` + `season`/`upcomingEvents` (`UsRetailCalendar`, cửa sổ 21–56 ngày); band giá = TB Giá nhập top SP (chỉ meta UI/`sheinPriceBandUsd` context, **không** gắn vào URL) → OpenAI: `analysisPoints[]` (3–5 ý) + **`searchesTrend`×2** (bám SP đang win) + **`searchesUpcoming`×3** (mùa/sự kiện, khác nhóm trend) → URL Shein dạng organic RecentSearch/`historyWord` (`pdsearch/{kw}/?ici=…RecentSearch…&source=historyWord&src_module=search`, **không** `sort`/`min_price`/`max_price`, **không** `src_identifier`/`src_tab_page_id` — 2 tham số này Shein đối chiếu với log lượt xem list của phiên đang mở nên link tự dựng luôn ăn `/risk/action/limit`). Thiếu keyword → **gọi lại OpenAI 1 lần** rồi mới báo lỗi. Extension: đúng 1 shop; click keyword rebuild URL phía client (cache cũ cũng sạch price). |

### Listing limit (`publish_limit`) — TikTok preload → `PUT /shops/tiktok-profile`

Extension bước **Info** (chỉ trong **Sync all**, không chạy khi **Sync fast**) gọi TikTok `GET /api/v1/product/product_creation/preload` trên tab `seller-us.tiktok.com` và map:

| TikTok (`data.publish_limit`) | Body extension / API | Cột DB `Shops` |
|-------------------------------|----------------------|----------------|
| `total_limit` | `publishLimitTotal` hoặc `publishLimit.totalLimit` | `PublishLimitTotal` |
| `published_num` | `publishLimitPublished` hoặc `publishLimit.publishedNum` | `PublishLimitPublished` |

`interval`, `toast` và các field preload khác **không** persist. UI cột **Listing** (`/shops`, `/shop-tasks`) hiện `—` khi `PublishLimitTotal` null (không lấy từ Analytics hay sync listing 4Seller).

**Namespace `/api/v1/product/*` — thang ưu tiên (không tự ký request):** gateway product trả `code=10000` **không kèm data/message** khi request không mang được CSRF thật + chữ ký `webmssdk` (`X-Bogus` / `_signature` / `msToken`) mà isolated world không sinh nổi; `seller/`, `pay/` chỉ cần cookie nên không bị. Thứ tự lấy số, dừng ở chặng đầu có `data`:

1. **`page-harvest`** — MAIN world `public/sellerProductBridge.js` hớt luôn response `product_creation/preload` của **chính trang** khi seller mở trang tạo sản phẩm → `SELLER_PRELOAD_CAPTURED` → background merge vào `kbt_tts_shop_info`; bước Info sau đó cũng đọc lại được từ store harvest (mục dưới). Không tốn request nào, không bị chặn. Extension **không** tự điều hướng tab để tạo tình huống này.
2. **`bridge`** — gọi qua `window.fetch` **của page** (đọc tại thời điểm gọi để lấy lớp bọc ngoài cùng của TikTok) với header lấy từ template request thật của trang. Content script ping MAIN (500ms) trước; im lặng thì bỏ chặng này. Timeout 6s (bridge) / 7s (content).
3. **`isolated …`** — fetch từ content script như trước: `GET+csrf` → `GET+shop_region` (`shop_region=US&user_is_login=true`) → `POST{}`. Bridge **đã trả lời** mà vẫn trượt thì chỉ thử `GET+csrf` (request có chữ ký của page còn bị từ chối thì biến thể isolated không khá hơn); MAIN im lặng mới quét cả thang. Ngân sách tổng ≤9s để bước Info không vượt timeout 14s của background.

**Chính sách header của bridge:** allowlist header **theo phiên** (`x-secsdk-csrf-token`, `x-tt-oec-region`, `x-tt-store-region`, `x-tt-entry-source`, `x-use-boe`/`x-use-ppe`, `x-tt-env`, `content-type`); **loại bỏ** chữ ký một-lần (`x-bogus`, `x-gorgon`, `x-khronos`, `x-argus`, `x-ladon`, `x-ss-stub`, `_signature`) và nhóm browser-managed (`cookie`, `origin`, `referer`, `sec-*`, `user-agent`, `content-length`…); URL tái dùng bị xóa `X-Bogus` / `_signature` / `msToken` khỏi query. **Vì sao không replay chữ ký:** chúng gắn với đúng một URL/thời điểm — bê sang URL khác là tự khai sai phiên, đúng thứ risk-gate của TikTok bắt được; để trang tự ký thì không phải đoán. Request do extension tự gọi được đánh dấu trong `window.__kbTtsForgedUrls` nên **không** bị `pageIdleHook.js` đếm vào inflight, **không** bị lấy làm template (header trơ sẽ ghi đè token phiên tốt) và **không** bị hớt lại như số của trang.

**Chẩn đoán khi thiếu limit:** parser chỉ loại payload khi `code` **có mặt và ≠ 0** (có version preload trả thẳng `{ data }` không kèm `code`). Message bước **Info** và `TikTokShopSyncLogs.ErrorMessage` (log Profile, kể cả `completed` — qua `syncNote` trong body `PUT /shops/tiktok-profile`) luôn nói **nguồn**: `Listing 12/1000 (page-harvest)` / `(bridge)` / `(isolated GET+shop_region)` / `(nguồn cũ)` khi số là của lượt trước; trượt thì liệt kê từng chặng: `preload bị từ chối — bridge: code=10000 · isolated GET+csrf: … · POST{}: …`, `preload không có publish_limit (key data: …)`, `publish_limit.total_limit=… không đọc được`, `HTTP …`, timeout. Khi lượt fetch chùm không có limit, background **chỉ gọi lại đúng** `product_creation/preload` (message `FETCH_PRODUCT_PRELOAD`) rồi merge vào `kbt_tts_shop_info` — không chạy lại cả 8 request `seller/common/get`. Preload lỗi thì `PublishLimit*` cũ trong DB **được giữ** (body không gửi field null).

### Hớt JSON thụ động (`page-harvest`) — 10 endpoint không tham số

`public/sellerProductBridge.js` (MAIN world) hook fetch/XHR **chỉ đọc** và lưu response của các endpoint dưới đây vào `sessionStorage['kbt_tts_harvest']` (`{ raw, at, sellerId }`, cap ~400KB/entry và ~2MB tổng — vượt cap thì chỉ giữ trong RAM để không chiếm quota của TikTok). Bước sync hỏi store **một lần cho cả lượt** qua message `kbt-tts-harvest-get`; entry còn **≤10 phút**, đúng `sellerId` (stamp từ cookie `oec_seller_id_unified_seller_env` lúc hớt) và qua `isUsable` thì dùng luôn payload đó — **không gửi request nào** — và nhãn nguồn là `page-harvest`. Ngược lại chạy đúng đường cũ (`bridge` → `isolated`).

| Khoá | Path | `isUsable` |
|---|---|---|
| `seller_common` | `/api/v3/seller/common/get` | `data.seller.seller_id` |
| `passport` | `/passport/account/info/v2/` | `message=success` + `data` |
| `onboard` | `/api/v1/seller/onboard/v2/config/get` | `data.customized_config.product_categories` |
| `preload` | `/api/v1/product/product_creation/preload` | `data.publish_limit` |
| `pay_onboard` | `/api/v1/pay/onboard_info/get` | `data.enterprise` + body request đúng `{user_type:1,biz_type:1}` |
| `settlement` | `/api/v1/seller/settlement/account/get` | `data` |
| `statement_stat` | `/api/v1/pay/statement/stat/info` | `amount_stat_type=1` + `statement_version=1` |
| `products_list` | `/api/v1/product/local/products/list` | `tab_id=19` |
| `ahr` | `/api/v1/account/ahr/score` | `account_type=2` + `account_level=1` + `data.score` |
| `ses` | `/api/v1/seller/experience-score/score-overview` | `ses_version=beta` + `shop_id` rỗng + `data.overview.score` \| `data.metric.metricGroups` |

Chỉ nhận payload lành (`code` vắng hoặc `= 0` **và** có `data`); payload lỗi **giữ nguyên** entry cũ. Các chốt query trên không phải trang trí: `tab_id≠19` không mang `suspend_reason` (suy ra sai thành "không bị khóa" — cờ đó đẩy shop sang `Suspended`), `amount_stat_type≠1` làm "to settle" mang nghĩa khác, `account_level`/`shop_id` khác là điểm của scope/shop khác. Request do chính extension gọi (`window.__kbTtsForgedUrls`) **không** bị hớt. Nhóm endpoint **có tham số** (payout list, health records, Compass analytics, `lead/list`) không hớt: payload phụ thuộc trang/khoảng ngày/category nên phải khớp fingerprint mới dùng lại được.

Message bước **Info** và bước **Shop Score** ghi tỉ lệ dùng harvest: `· Listing 12/1000 (page-harvest) · harvest 6/8`; phần này cũng đi vào `syncNote` → `TikTokShopSyncLogs.ErrorMessage` của log Profile.

UI seller-web `/shops`: dùng shared `@drop/shops` (`SELLER_SHOPS_CONFIG`) — mirror UI admin (tabs status, view/edit modal, PageHeader, cột **Listing**; cột **Danh mục**/Bank tắt mặc định — gán danh mục qua nút Actions) nhưng không create/delete/reorder/seller filter/receiving-bank; form khóa platform+region, thanh toán dùng `newBankNumber`; **Ngách không còn trên form sửa** — nút **Danh mục** mở modal Cascader (site = `shop.region`). **Không** cột/payload On hold trên `/shops` — tiền nằm trang **Tài chính shop** `/shops/finance` (shared `@drop/shop-finance`). FE: `createShopApi({ basePath: '/shops' })`.

## Seller Shop Task Endpoints

Nhiệm vụ hệ thống theo shop — bảng nhiệm vụ luôn ép scope `OwnerId` = JWT. Trạng thái **suy ra tự động** từ dữ liệu đã có (`TikTokShopSyncLogs` trong ngày VN, `Shop.PublishLimit*`, `ShopTaskCompletions`), không tick tay. UI seller-web `/shop-tasks` (shared `@drop/shop-tasks`, `SELLER_SHOP_TASKS_CONFIG`). FE: `createShopTaskApi({ basePath: '/shop-tasks' })`.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/shop-tasks/board` | `seller.shop_tasks.view` | Bảng nhiệm vụ một ngày VN — **mọi trạng thái shop** (Active / Deactivated / Suspended). Query `date?` (`yyyy-MM-dd`, mặc định hôm nay), `shopId?`, `search?` (tên shop / profile / TikTok seller id); `ownerId` bị bỏ qua — luôn ép JWT. Response `{ date, summary{ shopCount, completedShopCount, totalTaskCount, completedTaskCount, attentionTaskCount, publishLimitPublishedSum, publishLimitTotalSum, delistCountSum, reviewCountSum }, shops[], warnings[] }`. UI optimize suy KPI **Cần list** = `max(0, publishLimitTotalSum − publishLimitPublishedSum)` (không field API riêng); sub **Lãng phí X.X shop** trên KPI Cần list / Cần hủy = số listing ÷ (`publishLimitPublishedSum / shopCount`) khi ≥ 0.1. Mỗi shop: `shopId`, `shopName`, `profileName?`, `avatarUrl?`, `externalShopId?`, **`shopStatus`** (ops `Active`/`Deactivated`/`Suspended` — UI Tag cạnh tên khi không Active), `ownerId`/`ownerFullName`/`ownerUserName`, `publishLimitPublished`/`publishLimitTotal`/`remainingSlots`, **`todayOnlineSeconds`** (tổng giây online theo **ngày board** VN từ `ShopOnlineSessions`; phiên mở + board=hôm nay → đến UTC now; UI cột Online sau Sync = `Xh Ym`), `completedCount`/`totalCount`/`attentionCount`, `deliveredOrderCount`/`refundOrderCount` (đếm đơn theo tab `DELIVERED`/`REFUND` — cùng `WorkflowTab` như `/orders`, toàn thời gian, không lọc theo `date` của board), `hasProductDiscount`/`hasFlashDeal` (bool — có activity ONGOING trên mirror `ProductDiscountActivities` theo `ShopId`; Flash = `discountType=FLASHSALE`; một batch query, không N+1), `productDiscountBeginTimeUtc`/`productDiscountEndTimeUtc`/`flashDealBeginTimeUtc`/`flashDealEndTimeUtc` (UTC của activity ONGOING **sắp hết hạn nhất** theo loại — earliest `EndTimeUtc`; null khi không có; UI cột Discount/Flash hiện số giờ còn lại + tooltip bắt đầu/kết thúc giờ VN), `healthSummary?` (`activeViolationCount`, `activeWarningCount` — record active `recordStatus=1` **chưa** đánh dấu xử lý; `handledViolationCount`, `handledWarningCount`; `pendingRecent[]` / `handledRecent[]` — mỗi dòng gồm `recordId`, `recordType`, `reason`, `detail`, `categoryType`, `actions[{ title, actionStartTime?, actionEndTime? }]`, `createTime`, `handledAt?`; icon cảnh báo mở modal cần xử lý, icon record mở modal đã xử lý), điểm Health Center snapshot `ahrScore?`/`shopPerformanceScore?`/`productSatisfactionScore?`/`fulfillmentScore?`/`customerServiceScore?` (thêm cột vào `Select` sẵn có — **không** query mới; UI hiện chip `SPS`/`AHR` dưới tên shop), `tasks[]`. Mỗi task: `key` (`sync_extension`\|`url_visit`\|`listing_fill`\|`delist_cleanup`), `definitionId?` (chỉ `url_visit`), `title`, `group` (`daily`\|`optimize`), `status` (`done`\|`pending`\|`attention`\|`blocked`\|`failed`), `detail`, `current?`/`target?`, `url?`, `completedAt?`. **Chỉ 4 truy vấn phẳng** (shops / sync log gộp theo `ExternalShopId`+`SyncType` / definitions / completions) + batch health summary + **một** GROUP BY đơn theo `ShopId`+`WorkflowTab` cho `deliveredOrderCount`/`refundOrderCount` + batch cờ PD/Flash Deal + batch `ShopOnlineSessions` — không N+1, không chạy delist scoring. Violation active (chưa handled) cộng thêm vào `attentionCount`. |
| POST | `/shop-tasks/health-records/handled` | `seller.shop_tasks.view` | Đánh dấu một health record đã xử lý trên UI. Body `{ shopId, recordId }`. Shop phải thuộc JWT. Ghi `TikTokShopHealthRecords.HandledAt` — record biến khỏi icon cảnh báo, chuyển sang icon record / tab đã xử lý. Idempotent nếu đã handled. |
| GET | `/shop-tasks/extension/daily` | `seller.shops.edit` | **Extension:** nhiệm vụ truy cập URL của hôm nay cho `?externalShopId=`. Shop phải thuộc JWT (mọi trạng thái ops — Active / Deactivated / Suspended); không thuộc JWT hoặc chưa có shop → trả `items: []`. Response `{ date, shopId?, items[{ id, name, url, urlMatchPattern, viewDurationSeconds, done }] }` — `urlMatchPattern` backend tính sẵn (origin + path khi admin bỏ trống); `viewDurationSeconds` = số giây seller phải ở lại trang sau khi load xong (`0` = tính ngay). Chỉ đọc: extension **không** tự mở trang. |
| POST | `/shop-tasks/extension/visit` | `seller.shops.edit` | **Extension:** báo người dùng vừa mở đúng trang của nhiệm vụ. Body `{ definitionId, externalShopId, visitedUrl?, status (`done`\|`failed`), errorMessage? }`. Backend **re-validate** `visitedUrl` khớp `UrlMatchPattern` (so origin + path, bỏ query, case-insensitive) — không khớp thì ghi `failed` dù client báo `done`. Upsert `ShopTaskCompletions` unique `(ShopId, DefinitionId, TaskDate)` theo ngày VN nên đi lại trang nhiều lần trong ngày là idempotent; đã `done` thì lần `failed` muộn **không** ghi đè. |

Bốn nhiệm vụ và cách suy trạng thái (`ShopTaskStatusRules`, unit test `ShopTaskStatusRulesTests`):

- `sync_extension` (daily) — `done` khi đủ 4 `TikTokShopSyncLog.Status=completed` (`profile`/`payout`/`opportunity`/`analytics`) trong ngày VN; thiếu thì liệt kê bước còn thiếu. Shop chưa có `ExternalShopId` → `blocked`. Extension **phải** gọi endpoint sync tương ứng (kể cả `items`/`payments` rỗng) để backend ghi log — checklist extension xanh local **không** đủ; board chỉ đọc `TikTokShopSyncLogs`.
- `url_visit` (daily, N nhiệm vụ admin tạo) — `done` khi có `ShopTaskCompletion` `done` trong ngày; `failed` giữ nguyên lý do lần gần nhất. Người dùng tự tới trang (bấm nhiệm vụ trong extension hoặc đi bằng menu TikTok), extension quan sát và báo về — **không** có bước tự động mở trang.
- `listing_fill` (daily, **chỉ hiển thị**) — `done` khi `PublishLimitPublished >= PublishLimitTotal`; thiếu `publish_limit` → `blocked`. **Không** vào `completedCount`/`totalCount` hay tổng hợp «nhiệm vụ hàng ngày» — tiến độ % chỉ tính đồng bộ + URL.
- `delist_cleanup` (optimize) — `attention` khi hết slot; UI board optimize hiện chip «Cần hủy N · Cần xem xét M» trực tiếp trên cột Nhiệm vụ (cùng wording summary; đỏ khi N>0, vàng khi chỉ M>0) — không gói thành `1/1` vì status vẫn `done` khi còn slot; click mở modal in-place (`ShopDelistListingsModal` — lọc nhanh Tất cả / Cần hủy / Xem xét theo `verdict`). Drawer vẫn deep-link `/listings/analytics?shopId=…&delist=1`. Việc chấm điểm từng listing vẫn nằm ở `GET /listings/analytics/delist-suggestions`.

## Admin Shop Task Endpoints

Cùng bảng nhiệm vụ nhưng phạm vi **mọi shop** (mọi trạng thái ops), cộng CRUD nhiệm vụ truy cập URL. UI admin-web `/shop-tasks` (shared `@drop/shop-tasks`, `ADMIN_SHOP_TASKS_CONFIG`). FE: `createShopTaskApi({ basePath: '/admin/shop-tasks' })`.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/shop-tasks/board` | `admin.shop_tasks.view` | Bảng nhiệm vụ mọi shop (Active / Deactivated / Suspended). Query `date?`, `ownerId?`, `shopId?`, `search?`. Response giống `/shop-tasks/board` (kể cả `shopStatus` / `todayOnlineSeconds` / cột Online). |
| POST | `/admin/shop-tasks/health-records/handled` | `admin.shop_tasks.view` | Đánh dấu health record đã xử lý (mọi shop). Body `{ shopId, recordId }`. |
| GET | `/admin/shop-tasks/definitions` | `admin.shop_tasks.view` | Danh sách nhiệm vụ truy cập URL (gồm cả `isActive=false`), sort `sortOrder` → `name`. |
| POST | `/admin/shop-tasks/definitions` | `admin.shop_tasks.manage` | Tạo. Body `{ name, url, urlMatchPattern?, description?, viewDurationSeconds?, sortOrder?, isActive }`. Backend tự gán `sortOrder` = cuối danh sách (bỏ qua giá trị client). `viewDurationSeconds` 0–600 (mặc định `0`). `url`/`urlMatchPattern` bắt buộc **https** và host thuộc allowlist `seller-us.tiktok.com` — trùng `host_permissions` của extension nên content script mới đọc được URL đang mở. |
| PUT | `/admin/shop-tasks/definitions/{id}` | `admin.shop_tasks.manage` | Cập nhật (cùng body). `sortOrder` trong body **bị bỏ qua** — chỉ đổi thứ tự qua reorder. |
| PUT | `/admin/shop-tasks/definitions/reorder` | `admin.shop_tasks.manage` | Sắp xếp lại toàn bộ nhiệm vụ. Body `{ orderedIds: string[] }` — phải gồm mọi id hiện có, không trùng; phần tử đầu = `sortOrder` 0. |
| DELETE | `/admin/shop-tasks/definitions/{id}` | `admin.shop_tasks.manage` | Xoá nhiệm vụ; `ShopTaskCompletions` cascade theo. |

## Seller Receiving Bank Endpoints

Lịch sử tiền về theo shop — luôn ép scope theo shop `OwnerId` = JWT. Catalog thẻ vẫn admin-only. Seller tạo Manual **Credit** → `ConfirmationStatus=Pending` (Admin duyệt mới vào stats). UI: shared `@drop/receiving-banks` (`SELLER_RECEIVING_BANKS_TX_CONFIG`); menu **Sales Channels → Lịch sử tiền về** (`/receiving-banks/transactions`). FE: `createReceivingBankApi({ basePath: '/receiving-bank-accounts' })`.

| Method | Endpoint | Permission | Mô tả |
|--------|----------|------------|-------|
| GET | `/receiving-bank-accounts/options` | `seller.shops.view` | Dropdown thẻ active gắn ≥1 shop của seller (filter bảng) |
| GET | `/receiving-bank-accounts/transactions` | `seller.shops.view` | Ledger rows shop của seller (`?receivingBankAccountId` / `shopId` / `orderId` / `source` / `from` / `to`); loại Manual `ShopId=null`; kèm `confirmationStatus`, `payoutOrders[]` khi `TikTokPayout` |
| POST | `/receiving-bank-accounts/transactions` | `seller.shops.view` | Thêm tiền về: `shopId` bắt buộc; thẻ derive từ `Shop.ReceivingBankAccountId` (lỗi nếu shop chưa gắn thẻ); `Direction=Credit`; `Source=Manual`; `ConfirmationStatus=Pending` |
| PUT | `/receiving-bank-accounts/transactions/{id}` | `seller.shops.view` | Sửa Pending Manual của shop mình (Credit only; card derive từ shop) |
| DELETE | `/receiving-bank-accounts/transactions/{id}` | `seller.shops.view` | Xóa Pending Manual của shop mình |

## Seller Fulfil Endpoints

Lịch sử mua hàng (fulfil transactions) — luôn ép scope theo đơn `OwnerId` = JWT (+ dòng Manual không gắn đơn do chính seller tạo). Catalog accounts/cards vẫn admin-only. Seller **chỉ** CRUD `Source=Manual`; CostUpdate xem-only (không sửa/xóa / không bulk-delete). UI: shared `@drop/fulfil` (`SELLER_FULFIL_TX_CONFIG`); menu **Sales Channels → Lịch sử mua hàng** (`/fulfil/transactions`); filter Shop + tài khoản + RangePicker mặc định 30 ngày. FE: `createFulfilApi({ basePath: '/fulfil' })`.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/fulfil/accounts/options` | `seller.orders.view` | Dropdown tài khoản fulfil active (filter / form Manual) |
| GET | `/fulfil/cards/options` | `seller.orders.view` | Dropdown thẻ (`?accountId=`) |
| GET | `/fulfil/transactions` | `seller.orders.view` | List **phân trang** — force `OwnerUserId=JWT`; filter `accountId` / `cardId` / `orderId` / `shopId` / `keyword` / `from` / `to`; per-row snapshot `couponAmount`/`walletAmount`/`membershipAmount`/`pointsAmount`/`revenueAmount`/`profitAmount`; `summary` KPI cùng filter (orderCount / card / cost / coupon / wallet / club / points / profit — Profit 1 lần/đơn)
| GET | `/fulfil/transactions/{id}` | `seller.orders.view` | Chi tiết (404 nếu ngoài scope) |
| POST | `/fulfil/transactions` | `seller.orders.manage` | Tạo Manual; nếu có `orderId` phải thuộc owner |
| PUT | `/fulfil/transactions/{id}` | `seller.orders.manage` | Sửa Manual trong scope (CostUpdate → 400) |
| DELETE | `/fulfil/transactions/{id}` | `seller.orders.manage` | Xóa Manual trong scope (CostUpdate → 400) |

## Seller Platform Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/platforms/options` | `seller.products.view` | Dropdown nền tảng active (cùng catalog admin Platforms) — filter Listing 4Seller |

## Seller 4Seller Endpoints

Backend proxy gọi `https://www.4seller.com` — cookie đọc từ `ApplicationUser.FourSellerCookie` của user đang đăng nhập (user tự cấu hình tại Hồ sơ cá nhân; admin cũng có thể gán tại Quản lý Người dùng). Chỉ lấy cookie khi `FourSellerCookieLoginStatus=logged_in`; sau đó `GET /api/user/is-login` — thất bại → status `expired` + lỗi nghiệp vụ. Chỉ thao tác trên shop thuộc seller đăng nhập.

**FE:** UI `/fourseller` dùng shared `@drop/fourseller` (`SELLER_FOURSELLER_CONFIG`) — mirror admin (PageHeader, filters, OAuth modal + Descriptions) nhưng không seller filter/owner column; hiện cookie hint + nút **Cập nhật** khi đã Connected. Client: `createFourSellerApi({ basePath: '/fourseller' })`.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| POST | `/fourseller/shops/{shopId}/auth-redirect-url` | `seller.shops.edit` | Tạo OAuth authorize URL để kết nối shop với 4Seller |
| POST | `/fourseller/shops/{shopId}/check-connection` | `seller.shops.edit` | Kiểm tra shop đã kết nối trên 4Seller; tự cập nhật `fourSellerShopId` và `fourSellerConnectedAt` khi khớp |
| POST | `/fourseller/shops/sync` | `seller.shops.edit` | **Check login** trước; fail → `400`. Rồi đồng bộ `get-tidy-list`: khớp & cập nhật shop đã cấp cho seller; **không tạo shop mới**. Khi cookie thấy shop TikTok (`sellingPartnerId`) đã cấp cho seller khác → chỉ **relink** `FourSellerShopId` (giữ `PreviousFourSellerShopId`, **không** đổi `OwnerId`/tên/region). Rematch `Order.ShopId` theo id hiện tại **và** previous. Relink idempotent: shop đã đúng id thì không ghi lại, `relinkedCount` chỉ đếm lần id thực sự đổi. Response: `updatedCount` / `relinkedCount` / `ordersRelinkedCount` / `unmatchedShops[]` (`reason`: `not_connected` \| `no_local_shop` \| `ambiguous_external_id` \| `fourseller_id_taken`). FE: `POST /auth/four-seller-cookie/check-login` trước nút Đồng bộ |
| POST | `/fourseller/crawl/post-html` | `seller.products.manage` | **Browser Extension:** proxy `get-crawl-url-repeat` (best-effort) + `postHtml-file-shop`. Body: `{ url, html, shopIdList, site?, siteList?, reviewCount?, averageRating?, platform?, stock?, shippingDays?, shippingDayPercent?, shippingDayPercentDesc?, storeCode?, storeLogo?, storeTitle?, soldByTips?, shipsFromTips?, shopRank?, storeAttention?, storeUrl?, isLocalProduct?, isSupportQuickShip?, sizeGuideJson?, addedFrom? }`. Metric NCC + **`sizeGuideJson`** (Shein v2: `unit: inch`, `chartRows` từ `sizeInfoInch` + US map + HIP từ `base_code_info_inch`, `measureNote`, `productMeasureGuide`/`bodyMeasureGuide`) **không** forward 4Seller — upsert `SupplierProductInfos` theo `SourceUrlKey` (`SizeGuideJson` jsonb) `addedFrom` (`recommended` \| `manual`, optional) **không** forward 4Seller — upsert `ListingAddOrigins` **chỉ khi post-html OK** (OwnerId + SourceUrlKey + FourSellerShopId) rồi copy `Listings.AddedFrom` write-once khi sync có `SourceUrl` và listing `CreatedAt` ≥ staging − 5 phút (không gắn listing legacy). Lỗi staging không làm fail HTTP add. Thiếu `storeTitle` → parse từ `soldByTips` (`Sold by …`). `shopRank` / `storeAttention` (follow) lưu trên seller. SLA = `shipInfo.dayPercentDesc` (ưu tiên cửa sổ 7 ngày) hoặc cộng `dayPercents` (bỏ `>N`); **không** lấy `shippingDayPercentDesc` (~80.34% sàn). `isLocalProduct` = `shipInfo.localMadeTips === "Local"` (fallback `productInfo.isLocalProduct`); `isSupportQuickShip` = bất kỳ `sku_list[].mall_stock[].isSupportQuickShip` (boolean) — **không** dùng `sku.isSupportQuickShip` (thường string `"0"`). Thiếu `platform` thì server suy từ host URL (`shein.com`→`SHEIN`). Logo `//cdn…` được chuẩn hoá `https:`. Cookie 4Seller gắn server-side; response `{ ok, message }`. Limit body ~50MB. **Sau khi OK:** **không await** sync — `IPostHtmlDraftSyncScheduler` debounce **12s/seller** rồi `SyncAsync` draft (`pageSize=3`). Nếu `SyncGate` đang bị background/manual sync giữ → `SkippedBusy` + retry mỗi 8s tối đa ~3 phút (không bỏ pull). |
| POST | `/fourseller/supplier-product/size-chart-image` | `seller.products.manage` | **Extension — gắn ảnh Size Chart đã upload Bunny.** Body `{ sourceUrl, publicUrl, fileId? }`. Validate `publicUrl` thuộc host `bunny.cdn_hostname`. Upsert `SupplierProductInfos.SizeChartImageUrl` theo `SourceUrlKey` (row phải tồn tại sau `post-html`). Listing detail hydrate → `sizeChartCaptureUrl`. |
| POST | `/fourseller/listings/{platform}/page` | `seller.products.view` | Proxy listing page 4Seller theo cookie user (platform path, e.g. `tiktok`). |
| POST | `/fourseller/listings/{platform}/delete` | `seller.products.manage` | Xóa listing trên 4Seller (`POST /api/listing/{platform}/delete`). Body: `{ "listingIds": [832428730714, …] }`. |

Response format giống [Admin 4Seller Endpoints](#admin-4seller-endpoints).

## Seller Listings Endpoints

Seller listings mirror admin (`createListingApi` basePath `/listings`); ownership forced từ JWT. UI dùng chung `@drop/listings` với `SELLER_LISTINGS_CONFIG` (`seller.products.manage`, không seller filter / ownerName). Store listing duy nhất: `Listings` + `ListingVariations` (catalog `Products` / `ShopProducts` đã gỡ — migration `20260729164929_DropProductsCatalog`).

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/listings` | `seller.products.view` | List mirror local **scoped JWT** (`OwnerUserId` force server-side). Query: `pageCurrent`, `pageSize`, `pageType` (`draft`/`published`), `siteStatus`, `platform`, `shopId`, `search`, `dateFrom`, `dateTo`. Response kèm `statusCounts` + cùng field shop NCC (`storeTitle` / `shopRank` / `storeAttention`) / stock nguồn / `addedFrom` như admin list. Seller-web Draft/Published. |
| GET | `/listings/auto-publish-queue-status` | `seller.products.view` | Poll nhẹ badge auto publish — shape giống admin; chỉ listing draft thuộc JWT. Query `ids[]`. |
| GET | `/listings/{id}?refresh=` | `seller.products.view` | Chi tiết listing owned (local; `refresh=true` kéo 4Seller). Kèm **`sizeGuideJson`** và **`sizeChartCaptureUrl`** (Bunny URL từ extension) từ supplier snapshot khi có. |
| GET | `/listings/analytics` | `seller.products.view` | **Listing analytics** snapshot Compass — shape giống `/admin/listings/analytics` nhưng `OwnerUserId` **luôn ép từ JWT** (query `ownerUserId` bị bỏ). Query còn lại: `date`, `shopId`, `q`, `page`/`pageSize`, `sort`, `sortDir`. |
| GET | `/listings/analytics/dates` | `seller.products.view` | Ngày snapshot của seller JWT (`?shopId=`) → `{ dates[], latest }`. |
| GET | `/listings/analytics/delist-suggestions` | `seller.products.view` | **Đề xuất hủy list** — shape giống `/admin/listings/analytics/delist-suggestions`, `OwnerUserId` **luôn ép từ JWT** (áp cho cả query snapshot/shop nên `shopId` + `date` của client không đọc được shop seller khác). |
| POST | `/listings/sync` | `seller.products.manage` | **202 Accepted + `jobId`** (job nền, giống admin). Sync 4Seller → local **chỉ seller hiện tại** (`OwnerUserId` = JWT). Body: `platform`, `statuses[]`, `shopId?`, `dateFrom`/`dateTo`, **`syncScope`** (`full` \| `status`; chỉ `draft` → server suy `full`), `syncMode` legacy (`light` → `status`), `maxRecordsPerSeller` (`status` mặc định 200). |
| GET | `/listings/sync/status/{jobId}` | `seller.products.manage` | Tiến độ job (shape giống admin). Chỉ **người tạo job** đọc được — job của user khác trả `404`. |
| PUT | `/listings/{id}` | `seller.products.manage` | Cập nhật local mirror (form). |
| PUT | `/listings/{id}/description-prep` | `seller.products.manage` | Lưu mô tả + optional `productName` + optional `searchTerms` / `productHighlights` + cờ Rewrite (bắt buộc khi Publish) / Insert Images (tùy chọn) — không push 4Seller. |
| PUT | `/listings/{id}/source-url` | `seller.products.manage` | Local-only URL nguồn NCC (canonical; patch RawPayload keys; link snapshot NCC nếu có). Ownership JWT. |
| POST | `/listings/{id}/save-draft` | `seller.products.manage` | Save Draft → 4Seller + local. |
| POST | `/listings/{id}/publish` | `seller.products.manage` | Publish → 4Seller + local. |
| POST | `/listings/{id}/update` | `seller.products.manage` | Push update listing đã published. |
| POST | `/listings/rewrite-description` | `seller.products.manage` | AI rewrite mô tả (+ dòng hashtag SEO) + SEO title + `searchTerms[]` + `productHighlights[]` (không persist). |
| GET | `/listings/rewrite-config` | `seller.products.view` | `{ titleMode, allowRepeat, allowSpecificsRepeat, autoGenerateSizeChart }` — cùng Settings hệ thống với admin. |
| POST | `/listings/{id}/map-specifics` | `seller.products.manage` | Map description → Specifics schema (cùng `MapAsync` admin). 0 cặp Key:Value → 200, vẫn seed attribute bắt buộc của category (rỗng nếu category không bắt buộc gì), không 4xx. |
| GET | `/listings/{id}/shop-metadata?shopId=` | `seller.products.view` | Brands + warehouses 4Seller cho shop. |
| GET | `/listings/{id}/brand-suggest` | `seller.products.view` | Gợi ý brand. |
| POST | `/listings/{id}/cos-upload` | `seller.products.manage` | Upload Size Chart / video lên 4Seller COS. |
| POST | `/listings/{id}/generate-size-chart` | `seller.products.manage` | Tạo Size Chart tự động (Grok + COS) — mirror admin; ownership JWT. |
| GET | `/listings/{id}/move-source` | `seller.products.view` | Move-source / category-match (bind category). |
| GET | `/listings/{id}/recent-categories` | `seller.products.view` | Recent TikTok categories. |
| POST | `/listings/{id}/bind-category` | `seller.products.manage` | Bind recent-use category. |
| DELETE | `/listings` | `seller.products.manage` | Xóa theo filter list (force owner) — 4Seller rồi local. |
| POST | `/listings/bulk-delete` | `seller.products.manage` | Xóa theo ids thuộc owner (`{ "ids": [guid...] }`). |
| POST | `/listings/bulk-deactivate` | `seller.products.manage` | Hủy list hàng loạt ids thuộc owner — chỉ Active; trả `{ deactivatedCount, skippedCount }`. |
| POST | `/listings/{id}/activate` | `seller.products.manage` | Activate listing owned (ownership check → 404 nếu không phải của mình). |
| POST | `/listings/{id}/deactivate` | `seller.products.manage` | Deactivate listing owned. |
| POST | `/listings/{id}/duplicate` | `seller.products.manage` | Duplicate listing owned → draft mới. |
| POST | `/listings/{id}/sync-one` | `seller.products.manage` | **Luồng nội bộ** (auto-publish): TikTok → 4Seller qua `sync-one-local-listing`. FE thủ công dùng `GET …/listings/{id}?refresh=true`. |
| GET | `/listings/drafts` | `seller.products.view` | Trang listing **local** cho Browser Extension (`OwnerId`=JWT). Query: `pageType` (`draft` mặc định / `published`), `fourSellerShopIds` (csv 4Seller shop id — gộp shop đang chọn), `pageCurrent` (1-based), `pageSize` (mặc định 20, max 50). Paging **trên DB** (`Count` + `Skip`/`Take`, không trả hết). Extension **không** gọi lúc mở panel / vừa login khi tab Search (mặc định) — chỉ khi mở Draft/Publish. Response slim `{ items, total, pageCurrent, pageSize }` (không `statusCounts`). Sort draft: `PlatformCreateTime ?? CreatedAt`; published: `PlatformUpdateTime ?? LastSyncedAt`. Item kèm `reviewCount`/`averageRating` + shop NCC (`storeTitle` / `shopRank` / `storeAttention`) / SLA / `sourceStock` / `isLocalProduct` / `isSupportQuickShip` (join `SupplierProductInfos` → `SupplierSellerInfos`; thiếu title thì parse `soldByTips`) + `addedFrom`. |
| GET | `/listings/tab-counts` | `seller.products.view` | Badge tab extension (**không** rows). Query `fourSellerShopIds` (csv). `{ draftTotal }` = COUNT local `pageType=draft`; `{ publishLimitPublished, publishLimitTotal }` = SUM `Shop.PublishLimitPublished`/`PublishLimitTotal` (TTS `publish_limit`, cùng số Listing `156/200` trên modal shop). Gọi lúc mở panel / đổi shop — không cần click Draft/Publish. |
| GET | `/listings/added-source-shops` | `seller.products.view` | Cặp `{ sourceUrl, fourSellerShopId }` của listing còn trong DB (owner JWT) — Extension **gộp** vào cache `kbt_added_product_ids` khi mở panel / **Làm mới** / **Tải lại trang** / sau xóa draft (giữ mark local trong grace 10 phút vì draft sync sau post-html là debounce). |
| DELETE | `/listings/{id}` | `seller.products.manage` | Xóa listing thuộc owner (mọi `pageType`; 4Seller + local mirror). Extension chỉ gọi từ tab **Draft** (tab Publish không cho xóa). Seller-web list vẫn xóa được cả published. |
| GET | `/product-sources/options` | `seller.products.manage` **hoặc** `admin.product-sources.view` | Dropdown nguồn hàng active (Seller scraper + Fulfil extension) |
| GET | `/categories/tree?site=&activeOnly=` | `seller.products.view` | Cây category TikTok **full** (nặng — ưu tiên lazy `children`/`path` trên edit) |
| GET | `/categories/children?site=&parentId=&activeOnly=` | `seller.products.view` | **Lazy load**: con trực tiếp của `parentId` (rỗng = gốc) — Cascader `loadData` |
| GET | `/categories/path?site=&externalId=&activeOnly=` | `seller.products.view` | **Seed lazy Cascader**: `{ pathIds[], options[] }` pre-expand tới leaf — listing đã có category hiển thị đúng path |
| GET | `/categories?site=&search=&activeOnly=&take=` | `seller.products.view` | Danh sách flat (tìm theo tên/ID, không phân biệt hoa thường). `take` tối đa 200 — picker modal Danh mục shop; không truyền `take` thì trả hết. Khi có `search`, mỗi dòng kèm `pathLabel` (tên dọc path) |
| POST | `/categories/recommend` | `seller.products.manage` | Gợi ý category AI từ title (body: `site`, `title`, optional `fourSellerShopId`) — cookie JWT user; cùng blocked-remap + field `replacedFrom*` như admin |
| GET | `/variation-spec-templates/options?specType=` | `seller.products.manage` | Template Color/Size/Number of Items (read-only) cho picker Variations |
| GET | `/shipping-templates` | `seller.products.manage` | Danh sách shipping template **của user đăng nhập** (`CreatedById` = JWT) — seller chỉ thấy template của mình. Admin-web dùng chung endpoint (role Admin bypass permission). FE admin-web + seller-web cache `localStorage` `shipping-templates-cache:{userId}` + React Query key `['shippingTemplates', userId]` `staleTime` 5 phút — select/Quản lý mở không chờ round-trip; đổi account không lẫn cache. |
| GET | `/shipping-templates/{id}` | `seller.products.manage` | Chi tiết template (chỉ owner) |
| POST | `/shipping-templates` | `seller.products.manage` | Tạo template riêng cho user (`name`, `packageWeight/Length/Width/Height`, `weightUnit`, `dimensionUnit`, `isDefault?`) — unique `(CreatedById, Name)`; `isDefault=true` clear default cũ |
| PUT | `/shipping-templates/{id}` | `seller.products.manage` | Cập nhật template (chỉ owner; gồm `isDefault`) |
| DELETE | `/shipping-templates/{id}` | `seller.products.manage` | Xóa template (chỉ owner) |
| GET | `/pricing-templates` | `seller.products.manage` | Danh sách pricing template **của user đăng nhập** (`CreatedById` = JWT) — seller chỉ thấy template của mình. FE admin-web + seller-web cache `localStorage` `pricing-templates-cache:{userId}` + React Query key `['pricingTemplates', userId]` `staleTime` 5 phút — Batch giá bán mở modal không chờ round-trip; đổi account không lẫn cache. |
| GET | `/pricing-templates/{id}` | `seller.products.manage` | Chi tiết template (chỉ owner) |
| POST | `/pricing-templates` | `seller.products.manage` | Tạo template tính giá bán riêng cho user — unique `(CreatedById, Name)` |
| PUT | `/pricing-templates/{id}` | `seller.products.manage` | Cập nhật template (chỉ owner) |
| DELETE | `/pricing-templates/{id}` | `seller.products.manage` | Xóa template (chỉ owner) |

### Shipping Template body

```json
{
  "name": "Small box US",
  "packageWeight": 0.5,
  "packageLength": 10,
  "packageWidth": 8,
  "packageHeight": 4,
  "weightUnit": "lb",
  "dimensionUnit": "inch",
  "isDefault": true
}
```

`weightUnit`: `lb` \| `kg`. `dimensionUnit`: `inch` \| `cm`. `isDefault`: template mặc định (tối đa 1 / user) — list xếp Default lên đầu. `ListingEditPage`: chọn template → autofill Shipping; nút **Quản lý** CRUD template riêng từng user; tự chọn Default khi weight/dims trống.

### Pricing Template body

```json
{
  "name": "Shein → TT US",
  "supplierShipping": 3.99,
  "tax": 1,
  "discountPercent": 15,
  "fastSalePercent": 15,
  "adsFee": 3,
  "tikTokShipping": 5.99,
  "platformFeePercent": 6,
  "targetMode": "margin",
  "marginPercent": 50,
  "profitAmount": 10,
  "isDefault": true
}
```

`targetMode`: `margin` \| `profit`. `isDefault`: template mặc định (tối đa 1 / user) — list xếp Default lên đầu; `ListingEditPage` tự điền `salePrice` còn trống theo Default (`costPrice` ↔ `ListingVariation.initialPrice`). Cost **không** lưu trên template.

**Công thức Giá bán (client-side):**

```
TotalImport  = Cost + SupplierShipping + Tax
PromoRate    = max(Discount%, FastSale%) / 100
TargetProfit = margin ? Margin% * TotalImport : ProfitUSD
SalePrice    = (TotalImport + AdsFee - TikTokShipping + TargetProfit)
               / ((1 - PromoRate) * (1 - PlatformFee%/100))
```

Listing edit: cột Giá bán → **Batch** mở calculator (templates đã prefetch/cache local); chọn template → preview từng SKU → Apply.

**Đã proxy (seller):** `POST /fourseller/listings/{platform}/page` + `.../delete` (browse + xóa listing trên 4Seller).

**Listing mirror (Draft/Published):** bảng `Listings` + `ListingVariations` — store listing duy nhất; `POST /admin/listings/sync` + `ListingDraftSyncBackgroundService` / `ListingPublishedSyncBackgroundService` (settings `fourseller.listing_sync.draft.*` / `.published.*`) + push `save-draft` / `publish` / `update` / `batch-deactivate` qua `ListingSyncService` — xem section Admin Listings phía trên.

**Product Discount / Flash Deal mirror:** bảng `ProductDiscountActivities`; `POST …/promotions/product-discounts/sync` + `POST …/promotions/flash-deals/sync` + `ProductDiscountSyncBackgroundService` (settings `fourseller.product_discount_sync.*` và `fourseller.flash_deal_sync.*`, mỗi lane On/Off + `interval_hours`, đếm chu kỳ sau khi sync xong) — xem section Admin / Seller Product Discount & Flash Deal.

## Admin Platform Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/platforms` | `admin.platforms.view` | Danh sách nền tảng |
| GET | `/admin/platforms/options` | `admin.shops.manage` | Dropdown nền tảng active (dùng khi tạo/sửa shop) |
| GET | `/admin/platforms/{id}` | `admin.platforms.view` | Chi tiết nền tảng |
| POST | `/admin/platforms` | `admin.platforms.manage` | Tạo nền tảng |
| PUT | `/admin/platforms/{id}` | `admin.platforms.manage` | Cập nhật nền tảng |
| DELETE | `/admin/platforms/{id}` | `admin.platforms.manage` | Xóa nền tảng (không xóa được nếu còn shop) |

### Create / Update Platform Body

```json
{
  "code": "TikTok",
  "name": "TikTok Shop",
  "description": "optional",
  "avatarUrl": "https://.../tiktok.png",
  "isActive": true
}
```

Response / list item gồm thêm `avatarUrl` (optional). Options (`/admin/platforms/options`, `/platforms/options`) cũng trả `avatarUrl`.

## Supplier Seller Lists (shop NCC)

Catalog shop nguồn (`SupplierSellerInfos`) — **chỉ đọc**. Ghi vẫn qua `POST /fourseller/crawl/post-html`. UI chung `@drop/supplier-lists` (admin `/supplier-lists`, seller `/supplier-lists`).

**FE shared client:** `createSupplierSellerApi(http, { basePath: '/admin/supplier-sellers' | '/supplier-sellers' })`.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/supplier-sellers` | `admin.supplier-sellers.view` | Paged list shop NCC + KPI kỳ. Query: `search`, `platform`, `ownerUserId`, `pageCurrent`, `pageSize`, `period` (`day`\|`week`\|`month`\|`30d`, mặc định `30d`), `fromDate`+`toDate` (yyyy-MM-dd VN, ghi đè period). Admin thấy mọi shop (kể cả 0 listing). Sort: `itemRevenue` desc → `listingCount` desc → title |
| GET | `/admin/supplier-sellers/{id}` | `admin.supplier-sellers.view` | Chi tiết shop + KPI + `products[]` phân trang (`productPageCurrent` / `productPageSize`). Cùng query kỳ / `ownerUserId` như list — KPI detail phải khớp dòng list. Mỗi product kèm listings slim (`id`, `productName`, `pageType`, `siteStatus`, `mainImage`, `shopName`, `ownerName`) |
| GET | `/supplier-sellers` | `seller.supplier-sellers.view` | Giống admin list; JWT ép `OwnerUserId`. Chỉ shop có ≥1 listing của seller |
| GET | `/supplier-sellers/{id}` | `seller.supplier-sellers.view` | Chi tiết scoped seller; 404 nếu shop không gắn listing của họ. `ownerName` trên listing = null |

**KPI kỳ (cột list + detail):** `productCount`, `listingCount`, `orderCount` (distinct đơn money-eligible có ≥1 line match), `itemQty`, `itemRevenue` = Σ `ItemPrice × Quantity` của line match.

**Công thức doanh thu:** cùng `WhereMoneyEligible` dashboard (shop Active + có thẻ nhận tiền, exclude cancel/refund). Thời điểm `OrderCreateTime ?? CreatedAt`, cửa sổ VN nửa-mở `[from, to+1day)`. Gán đơn → shop NCC qua **OwnerId + SellerSku** (exact, case-insensitive) → `ListingVariation` → `Listing.SupplierProductInfo` → `SupplierSellerInfo`. Không dùng `SellerNetAmount` cấp đơn (tránh double-count khi 1 đơn nhiều NCC). Line không match listing/NCC không vào số liệu shop.

`storeTitle` trống → parse từ `soldByTips` (`Sold by kcruab-kp` → `kcruab-kp`). `storeUrl` trống → build Shein store home từ `storeCode`.

## Admin Product Source Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/product-sources` | `admin.product-sources.view` | Danh sách nguồn hàng |
| GET | `/admin/product-sources/options` | `admin.products.manage` | Dropdown nguồn hàng active (scraper / Fulfil extension / admin UI) |
| GET | `/admin/product-sources/{id}` | `admin.product-sources.view` | Chi tiết nguồn hàng |
| POST | `/admin/product-sources` | `admin.product-sources.manage` | Tạo nguồn hàng |
| PUT | `/admin/product-sources/{id}` | `admin.product-sources.manage` | Cập nhật nguồn hàng |
| DELETE | `/admin/product-sources/{id}` | `admin.product-sources.manage` | Xóa nguồn hàng |

### Create Product Source Body

```json
{
  "code": "SHEIN",
  "name": "Shein",
  "logoUrl": "https://example.com/shein-logo.png",
  "domains": "shein.com, us.shein.com",
  "description": "optional",
  "isActive": true
}
```

`domains`: danh sách host của site nguồn (phân tách bằng dấu phẩy hoặc xuống dòng). Browser Extension dùng để nhận diện trang sản phẩm (hiện nút "Thêm sản phẩm") và gán icon nguồn cho danh sách draft — **thiếu Domains thì FAB không hiện** trên PDP (seed mặc định `shein.com` / `temu.com` / `cjdropshipping.com`, backfill lúc restart API; extension còn fallback theo `Code`). Trường này cũng có trong `ProductSourceOptionDto` (`GET /product-sources/options`, `GET /admin/product-sources/options`). Publish limit / total listing **không** ảnh hưởng hiện nút thêm.

## Admin Variation Spec Templates

Preset Color / Size / Number of Items dùng trên form Variations (có `SortOrder`).

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/variation-spec-templates?specType=` | `admin.products.manage` | Danh sách template (filter optional) |
| GET | `/admin/variation-spec-templates/options?specType=` | `admin.products.manage` | Template active cho picker |
| GET | `/admin/variation-spec-templates/{id}` | `admin.products.manage` | Chi tiết |
| POST | `/admin/variation-spec-templates` | `admin.products.manage` | Tạo (`specType`, `value`, `sortOrder?`, `isActive`) |
| PUT | `/admin/variation-spec-templates/{id}` | `admin.products.manage` | Cập nhật (`value`, `sortOrder`, `isActive`) |
| PUT | `/admin/variation-spec-templates/reorder` | `admin.products.manage` | Sắp xếp (`specType`, `orderedIds[]`) |
| DELETE | `/admin/variation-spec-templates/{id}` | `admin.products.manage` | Xóa |

`specType` ∈ `Color` \| `Size` \| `Number of Items`. Unique `(SpecType, Value)`.

## Admin Settings Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/settings` | `admin.settings.view` | Danh sách cấu hình hệ thống |
| GET | `/admin/settings/{id}` | `admin.settings.view` | Chi tiết cấu hình |
| POST | `/admin/settings` | `admin.settings.manage` | Tạo cấu hình |
| PUT | `/admin/settings/{id}` | `admin.settings.manage` | Cập nhật cấu hình |
| DELETE | `/admin/settings/{id}` | `admin.settings.manage` | Xóa cấu hình |
| POST | `/admin/settings/shein-detail-sync/test-add-track-notify` | `admin.settings.manage` | Gửi một tin **TEST** dạng `📦 Add track · Auto Sync detail (TEST)` vào Chat ID `shein.order_detail_sync.telegram_chat_id`. Không cần bật toggle notify, không stamp đơn. Dev anonymous: `POST /api/v1/dev/diagnostics/telegram/add-track-test`. |
| GET | `/admin/settings/storage-stats` | `admin.settings.view` | Phân bổ dung lượng DB: heap / TOAST / index theo bảng + size & `idx_scan` từng index |

### Storage Stats

`GET /admin/settings/storage-stats?table=Listings&top=40`

Chỉ đọc catalog Postgres (`pg_class`, `pg_stat_user_tables`, `pg_stat_user_indexes`) — không chạy `pgstattuple`, không khoá gì. `table` lọc danh sách index về một bảng; `top` giới hạn mỗi danh sách (1–200, mặc định 30).

Tồn tại để không lặp lại chẩn đoán sai: export size theo bảng chỉ cho **tổng**, nên `Listings` 630 MB dễ bị quy cho cột `RawPayload`, trong khi heap+TOAST chỉ 40 MB và 590 MB còn lại là index.

```json
{
  "databaseBytes": 1043333120,
  "totalTableBytes": 190971904,
  "totalIndexBytes": 703594496,
  "tables": [
    {
      "tableName": "Listings",
      "totalBytes": 660602880, "tableBytes": 42106880, "toastBytes": 8192,
      "indexBytes": 618496000, "indexCount": 12,
      "liveTuples": 41233, "deadTuples": 9871,
      "updateCount": 1284551, "hotUpdateCount": 3120,
      "lastVacuum": null, "lastAutoVacuum": "2026-09-05T18:22:10Z", "lastAnalyze": "2026-09-05T18:22:11Z"
    }
  ],
  "indexes": [
    {
      "tableName": "Listings", "indexName": "IX_Listings_SiteStatus", "method": "btree",
      "bytes": 92274688, "scans": 0, "isUnique": false, "isPrimary": false,
      "definition": "(\"SiteStatus\")", "dropCandidate": true
    }
  ],
  "generatedAt": "2026-09-06T02:41:00Z"
}
```

Đọc số: `hotUpdateCount / updateCount` thấp nghĩa là gần như mọi UPDATE của bảng ghi lại **toàn bộ** index của nó. `dropCandidate` = `scans == 0 && !isUnique && !isPrimary` — vẫn phải đối chiếu query trước khi xoá, vì `pg_stat_*` reset khi restart Postgres. Quy trình REINDEX/VACUUM đi kèm nằm ở `backend/scripts/db-index-maintenance.sql`; API **không** tự chạy các lệnh đó.

Seed (nhóm **Dọn dữ liệu cũ**): `data_retention.analytics_snapshot_days` (number, mặc định `90` — `ListingAnalyticsSnapshots`, so theo ngày VN), `data_retention.tiktok_sync_log_days` (number, mặc định `30` — `TikTokShopSyncLogs`), `data_retention.refresh_token_days` (number, mặc định `7` — `RefreshTokens` đã revoke **hoặc** đã hết hạn). Đặt `0` để tắt dọn bảng tương ứng. `DataRetentionBackgroundService` chạy 1 lượt/24h (delay 5 phút sau khởi động), mỗi bảng một câu `DELETE … WHERE`. Không bảng nghiệp vụ nào có retention: `Listings`, `ListingVariations`, `SupplierPurchases`, `Orders`, sổ cái fulfil/ngân hàng và mọi `RawPayload` đều giữ nguyên.

### Create Setting Body

```json
{
  "group": "Bunny Config",
  "key": "bunny.access_key",
  "label": "Access Key",
  "value": "secret-value",
  "type": "password",
  "description": "Bunny CDN Access Key"
}
```

**Loại giá trị (`type`)**: `text`, `number`, `boolean`, `password`

Seed mặc định (nhóm **Bunny Config**): `bunny.access_key`, `bunny.storage_zone`, `bunny.storage_hostname`, `bunny.cdn_hostname`, `bunny.max_file_size_mb`

Seed (nhóm **Đồng bộ đơn hàng mới**): `fourseller.order_sync.new.enabled` (boolean, mặc định `true`), `.new.interval_minutes` (number, mặc định `5`, clamp 1–1440), `.new.lookback_days` (number, mặc định `2`, clamp 1–7), `.new.batch_size` (number, mặc định `15`, clamp 1–100), `.watchdog.enabled` (boolean, mặc định `true`), `.watchdog.overdue_minutes` (number, mặc định `15`, clamp 5–240). Seed (nhóm **Đồng bộ trạng thái đơn hàng**): `fourseller.order_sync.status.enabled` (boolean, mặc định `true`), `.status.interval_hours` (number, mặc định `12`, clamp 1–168), `.status.lookback_days` (number, mặc định `30`, clamp 1–60), `.status.batch_size` (number, mặc định `10`, clamp 1–100). UI: 2 card trên admin `/settings` (toggle + chu kỳ + lookback + batch; thêm dòng runtime từ `GET /health`: lastFinished / nextDue / overdue / lastSummary). DataSeeder migrate một lần từ 3 key legacy (`fourseller.order_sync.enabled` / `.interval_minutes` / `.lookback_days`) rồi xoá hàng cũ. Auto sync: cửa sổ ngày nghiệp vụ theo giờ VN; payload `dateQueryCreate*` = cùng khoảng tuyệt đối theo America/Los_Angeles.

Seed (nhóm **Đồng bộ đơn sau bán (4Seller)**): `fourseller.after_sales.trigger.enabled` (boolean, mặc định `false`) / `.trigger.interval_minutes` (number, mặc định `30`, clamp 1–1440) — yêu cầu 4Seller kéo hoàn/trả mới từ TikTok Shop; `fourseller.after_sales.pull.enabled` (boolean, mặc định `false`) / `.pull.interval_minutes` (number, mặc định `15`, clamp 1–1440) — kéo danh sách hoàn/trả về chuẩn trạng thái đơn local; `fourseller.after_sales.notify_enabled` (boolean, mặc định `true`); `fourseller.after_sales.telegram_chat_id` (text, mặc định `-1004462111161`); `fourseller.after_sales.watermark` (text — **nội bộ, job tự ghi**, JSON `{ ownerId: platformUpdateTimeUtc }`; xoá để quét lại từ đầu, lượt kế tiếp sẽ không gửi thông báo). UI card **Đồng bộ đơn sau bán (4Seller)** trên admin `/settings` (không hiện ô watermark).

### Timezone contract (DB / UI / 4Seller / SHEIN)

| Layer | Zone | Notes |
|-------|------|--------|
| PostgreSQL `timestamptz` + API ISO | UTC | Instant chuẩn |
| Business calendar, dashboard, Telegram, admin-web display/presets | `Asia/Ho_Chi_Minh` | Ngày/tuần/tháng nghiệp vụ; `formatDateTimeVn` coi naive ISO (không `Z`) là UTC |
| 4Seller **returned** timestamps (`OrderCreateTime`/`PaidTime`/`PlatformCreateTime`/…) | UTC | Naive strings đã là UTC → parse giữ nguyên digits (`ParseFourSellerNaiveAsUtc`) |
| SHEIN `addTime` / `pay_time` / milestones (unix) | UTC | Absolute epoch → UTC; **không** lấy wall Eastern/Pacific rồi +7 ra VN |
| SHEIN naive wall-clock (us.shein.com) | `America/New_York` → UTC | Chỉ chuỗi không offset |
| 4Seller order create-date **filters** (`dateQueryCreate*`, `createTime*`) | `America/Los_Angeles` | Chỉ format outbound; DST PST/PDT |
| Migration `20260721010000_CorrectFourSellerTimestampsPacificToUtc` | — | **No-op**: 4Seller times là UTC nên không cần shift (bản Pacific trước đó sai, sẽ +7/+8h lệch) |

Golden SHEIN: `addTime: 1784531136` → `2026-07-20T07:05:36Z` → hiển thị VN `20-07-2026 14:05` (Eastern wall +7 = `10:05` là sai). `unix <= 0` = chưa có mốc, không phải 1970.

Seed (nhóm **Listing Sync — Draft**): `fourseller.listing_sync.draft.enabled` (boolean, mặc định `true`), `fourseller.listing_sync.draft.interval_minutes` (number, mặc định `15`, clamp 1–1440) — UI card **Đồng bộ Draft 4Seller**; job `ListingDraftSyncBackgroundService` (newest drafts, `syncScope=full`, max 50/**Active shop**/seller — cùng rule Manual Sync: chỉ shop local Active có `FourSellerShopId`). Seed (nhóm **Listing Sync — Published**): `fourseller.listing_sync.published.enabled` (boolean, mặc định `true`), `fourseller.listing_sync.published.interval_minutes` (number, mặc định `60`, clamp 1–1440), `fourseller.listing_sync.published.lookback_days` (number, mặc định `10`, clamp 1–30) — UI card **Đồng bộ Published 4Seller**; job `ListingPublishedSyncBackgroundService` (status-only, createTime lookback, cap 500/**Active shop**/bucket). Chu kỳ mỗi loop đếm từ lúc sync hoàn tất. DataSeeder migrate một lần từ 4 key legacy rồi xoá. Probe createTime: `GET /api/v1/dev/listing-sync/create-time-probe` (Development).

Seed (nhóm **Product Discount Sync**): `fourseller.product_discount_sync.enabled` (boolean, mặc định `true`), `fourseller.product_discount_sync.interval_hours` (number, mặc định `2`). Seed (nhóm **Flash Deal Sync**): `fourseller.flash_deal_sync.enabled` (boolean, mặc định `true`), `fourseller.flash_deal_sync.interval_hours` (number, mặc định `2`). UI admin `/settings`: một card **Đồng bộ Product Discount / Flash Deal** (On/Off + chu kỳ giờ từng loại). Job `ProductDiscountSyncBackgroundService`: hai lane độc lập; chu kỳ tính từ lúc lần sync tự động hoàn tất. Permissions: `admin.promotions.view|manage`, `seller.promotions.view|manage`.

Seed (nhóm **Listing Rating**): `listing.rating.success_min` (number, mặc định `4.4`), `listing.rating.warning_min` (number, mặc định `4`) — màu ngôi sao trên `ListingCard` (admin) và browser-extension tab **Search** / Draft / Publish (cùng ngưỡng mặc định): ≥ success → xanh (`#52c41a`), ≥ warning và &lt; success → vàng (`#faad14`), &lt; warning → đỏ (`#ff4d4f`). UI card **Cảnh báo rating listing** trên admin `/settings`.

Seed (nhóm **Listing Rewrite**): `listing.rewrite.title_mode` (text, mặc định `strip_brands`) — `strip_brands` = AI nhận diện rồi xóa từ thương hiệu/sàn **cụm đầy đủ** (`SHEIN BAE` không chỉ `SHEIN`; C# vẫn nuốt line in-house nếu model chỉ trả gốc sàn), giữ wording gốc; **nếu tiêu đề còn dưới 40 ký tự** thì rẽ nhanh tạo SEO mới (`Women …` cho hàng nữ, nhồi keyword đồng bộ mô tả); `rewrite` = AI viết lại Machine-first SEO 150–250 ký tự từ title gốc + synonym cùng món (không bịa thuộc tính; trần sanitize 255). `listing.rewrite.allow_repeat` (boolean, mặc định `false`) — tắt khóa nút Rewrite sau lần đầu; bật cho phép viết lại khi đã có title/mô tả AI. Seed (nhóm **Listing Specifics**): `listing.specifics.allow_repeat` (boolean, mặc định `false`) — tắt khóa Auto Specifics khi đã map (ngoài Brand/cert); bật cho chạy lại. Seed (nhóm **Listing Size Chart**): `listing.size_chart.auto_generate_enabled` (boolean, mặc định `false`) — bật nút ⚡ Grok AI trên block Size Chart (`ListingEditPage`); prompt/API key cấu hình `Xai:*` trong appsettings (không Settings DB). Áp dụng khi `POST …/rewrite-description` / nút Auto Specifics (admin + seller). UI card **Rewrite & Auto Specifics** trên admin `/settings`. `GET …/rewrite-config` trả `{ titleMode, allowRepeat, allowSpecificsRepeat }`.

Seed (nhóm **Phí xử lý đơn**): `order.handling.buy_track` (number, mặc định `0.5` USD), `order.handling.thank_card` (number, mặc định `1` USD) — phí BuyTrack (USPS scan) / ThankCard cộng vào `Orders.totalCost` khi đánh dấu; UI card **Phí xử lý đơn** trên admin `/settings`.

Seed (nhóm **USPS Track Scan**): `order.usps_scan.auto_enabled` (boolean, mặc định `false`), `order.usps_scan.delay_hours` (number, mặc định `2`, 1–168 — **tuổi tối thiểu** của mã VC tính **chỉ** từ `TrackingAddedAt`, chỉ để chặn track vừa thêm; *thời điểm* đẩy do khung giờ US quyết định), `order.usps_scan.chunk_size` (number, mặc định `10`, 0–100 — mỗi chu kỳ lấy **hết** eligible; `N>0` gửi từng gói N mã; `N=0` gửi hết một lần, vẫn chia theo `PrintPackShip:MaxTrackingPerBatch` nếu vượt), `order.usps_scan.interval_minutes` (number, mặc định `10`, 1–1440 — đếm **sau** khi gửi hết chunk).

Khung giờ làm việc bưu điện Mỹ (nhân viên chỉ scan trong giờ hành chính): `order.usps_scan.window_enabled` (boolean, mặc định `true`; `false` = đẩy suốt ngày đêm như trước, chỉ còn tuổi tối thiểu), `order.usps_scan.weekday_start` / `order.usps_scan.weekday_end` (text `HH:mm`, mặc định `08:00` / `17:00`), `order.usps_scan.saturday_enabled` (boolean, mặc định `true`), `order.usps_scan.saturday_start` / `order.usps_scan.saturday_end` (text `HH:mm`, mặc định `08:00` / `12:00`), `order.usps_scan.cutoff_margin_minutes` (number, mặc định `30`, 0–240 — ngừng đẩy N phút trước giờ đóng). **Giờ lưu trong Settings là giờ US Miền Đông** (`America/New_York`, `AppTimeZones.Eastern`) để khung giữ nguyên ý nghĩa nghiệp vụ khi Mỹ đổi DST; **Chủ nhật luôn nghỉ**. UI `/settings` hiển thị quy đổi **giờ VN** tính sống (`etRangeToVnLabel`) + dòng trạng thái mở/đóng: `08:00–17:00` ET ≈ `20:00 → 05:00 (+1)` VN mùa đông, `19:00 → 04:00 (+1)` VN mùa hè. Đệm >= độ rộng khung ⇒ khung không bao giờ mở (job log warning).

Job `UspsScanAutoPushBackgroundService` đọc **một lượt** toàn bộ `order.usps_scan.*` (`UspsScanAutoSettingsReader`) rồi chuyển snapshot sang `IUspsScanAutoPushOrchestrator.RunOnceAsync(config, ct)` → `IUspsScanPushService.PushAsync` (cùng gate với nút tay; bỏ qua đơn đã In-transit/Delivered và REFUND/CANCELLED). UI card **USPS Track Scan tự động** trên admin `/settings` (cạnh Phí xử lý đơn). Không endpoint mới.

Seed (nhóm **4Seller Proxy**): `fourseller.proxy.enabled` (boolean, mặc định `false`), `fourseller.proxy.pool` (text, mỗi dòng 1 proxy `scheme://user:pass@host:port`), `fourseller.proxy.cooldown_minutes` (number, mặc định `10`, tối thiểu 1), `fourseller.session.validate_ttl_minutes` (number, mặc định `15`, tối thiểu 1 — TTL cache kết quả is-login thành công) — UI card **Proxy 4Seller** trên admin `/settings`. Điều khiển pool failover + master toggle + session cache; proxy riêng từng seller lưu ở `AspNetUsers.FourSellerProxy` (trang Người dùng). Xem mục **Proxy 4Seller (chống chặn IP 567)**.

Seed (nhóm **Telegram**): toggle/schedule trong Settings — `telegram.notify_new_order` (boolean, mặc định `true` — **chỉ đơn mới**), `telegram.notify_cancel_refund` (boolean, mặc định `true` — Hủy/Refund), `telegram.notify_usps` (boolean, mặc định `true` — USPS BuyTrack), `telegram.notify_warehouse_track` (boolean, mặc định `true` — track kho Shein), `telegram.notify_order_message` (boolean, mặc định `true` — Lưu Thông báo trên đơn), `telegram.notify_stale_no_tracking` (boolean, mặc định `true` — digest 1 lần/ngày tab Đã mua quá N giờ chưa tracking thật, không gồm hoàn/hủy; cùng giờ `telegram.fulfil_summary.hour`), `telegram.notify_stale_no_tracking.hours` (number, mặc định `72`, 1–720), `telegram.notify_stale_no_tracking.last_sent_date` (nội bộ), `telegram.orders_chat_id` (text, mặc định `-1004406324922` → fallback `Telegram:ChatId` nếu rỗng — **chỉ** đơn mới + daily summary + seller summary; DataSeeder one-shot sửa rỗng / ID chết `-5204324132` → `-1004406324922`), `telegram.cancel_refund_chat_id` (text, mặc định `-1004462111161` — **nhóm xử lý đơn**: Hủy + USPS + track kho + tổng hợp fulfil + thông báo message + đơn N giờ chưa track), `telegram.daily_summary.enabled` / `.hour` / `.last_sent_date`, `telegram.seller_summary.enabled` / `.hour` / `.last_sent_date`, `telegram.fulfil_summary.enabled` (boolean, mặc định `true`), `telegram.fulfil_summary.hour` (text `HH:mm`, mặc định `08:00`), `telegram.fulfil_summary.last_sent_date` (nội bộ). **BotToken** trong `appsettings.json` section `Telegram` (không seed Settings). Không có endpoint Telegram riêng — toggle/chat id qua CRUD `/admin/settings` + card **Thông báo Telegram** trên admin `/settings` (label **Chat ID báo đơn / summary** + **Chat ID xử lý đơn**). Job nền `OrderDailySummaryBackgroundService` (in-process cùng API — **API phải đang chạy**) gọi `TrySendDailySummaryAsync` + `TrySendSellerSummaryAsync` + `TrySendFulfilSummaryAsync` + `TryNotifyStaleNoTrackingAsync` + `TrySendPendingNewOrdersAsync`; lịch dùng `TelegramSummarySchedule` (ưu tiên ngày hôm nay khi đã tới `HH:mm`; nếu miss — đặc biệt `23:59` chỉ còn ~1 phút trước nửa đêm — **catch-up ngày hôm qua** cho tới khi tới slot hôm nay). Dev-only: `POST /api/v1/dev/diagnostics/telegram/seller-summary?date=yesterday|today|yyyy-MM-dd` force-send (bỏ qua lịch/`last_sent`). Thông báo gắn upsert `OrderSyncService`: đơn mới → `orders_chat_id` (gate `notify_new_order`; **bắt buộc** `ShopId` đã resolve — không gửi khi Profile/Shop còn trống; stamp `Orders.TelegramNewOrderNotifiedAt` sau gửi; sweep `TrySendPendingNewOrdersAsync` gửi bù ≤24h khi shop resolve muộn / lỗi mạng, expire stamp nếu quá 24h vẫn thiếu shop; migration backfill stamp=`CreatedAt` cho đơn cũ); hủy (`CANCELLED`) → `cancel_refund_chat_id` (gate `notify_cancel_refund`; HTML + link preview; bỏ qua thiếu Title/giá). Sync gán chủ đơn theo `OrderSyncShopIndex` (map `FourSellerShopId` toàn hệ thống — trang cookie seller A trả đơn shop seller B thì insert đúng `OwnerId`/`ShopId` của B). **Refund không còn đi qua sync đơn** — xem nhóm `fourseller.after_sales.*` bên dưới. Format **đơn mới** (compact mobile preview): `🚀 <b>{seller}</b> vừa có đơn hàng mới!` / `• <b>$giá</b> • {tên SP≤38}` / `• {ProfileName} • {ShopName}` / `• {d/M HH:mm} • <code>{mã}</code>` (giờ VN) / footer động lực — không separator. Format Hủy/Refund thêm dòng `Đặt: dd/MM/yyyy • Hủy|Refund: dd/MM/yyyy • <b>N ngày</b>` (ngày lịch VN; mốc `CanceledAt`/`RefundedAt`) và `Shein: ✅ Đã mua` / `❌ Chưa mua` (`FulfilPurchasedAt` hoặc có `SupplierPurchases`). **Đơn sau bán** (`NotifyAfterSalesOrdersAsync`, gate `fourseller.after_sales.notify_enabled`, chat `fourseller.after_sales.telegram_chat_id`, mặc định `-1004462111161`): header `💸 … vừa có đơn bị refund!` / `↩️ … yêu cầu hoàn đã được đóng.` / `🛎️ … có đơn cần xử lý hoàn/trả!` + block Hủy/Refund ở trên + `• Sau bán: {platformReturnStatus}` / `• Lý do` / `• Tiền hoàn` / `• Mã trả hàng`; chỉ gửi khi lượt đó đổi thật `PlatformOrderStatus` / `RefundedAt` / `CancelReason`, và lượt chạy đầu tiên (chưa có watermark) không gửi. **Realtime USPS / track kho** (`NotifyFulfilHandlingAsync`): sau SHEIN sync / Mark Ship (manual + auto + reconcile) — gộp 1 tin / đơn vào `cancel_refund_chat_id`; header `📮|🎁|📬 <b>{seller}</b> — Cần xử lý fulfil!` + items/shop/giá + dòng `📮 USPS • <code>…</code>` và/hoặc `🎁 Kho • <code>…</code>`; dedup `Orders.TelegramUspsNotifiedAt` / `TelegramWarehouseNotifiedAt` (stamp sau gửi thành công). **Thông báo message** (`NotifyOrderMessageAsync`): sau `PUT …/message` khi có nội dung → `cancel_refund_chat_id`; format `⚠️ <b>{seller}</b> — Thông báo xử lý đơn` + items/shop/giá + `📢 {message}` + footer xử lý. **Đơn N giờ chưa track** (`TryNotifyStaleNoTrackingAsync`): cùng lịch `telegram.fulfil_summary.hour` (GMT+7, `TelegramSummarySchedule` + `.last_sent_date`); tab `PURCHASED` (đã mua, chưa carrier last-mile, **không** hoàn/hủy/giao); tuổi ≥ N giờ (`telegram.notify_stale_no_tracking.hours`, mặc định 72); 1 digest/ngày vào `cancel_refund_chat_id` — header `⏰ Đơn {N}h chưa có tracking thật (dd/MM/yyyy)` + `Tổng X đơn` + mỗi đơn 3 dòng `• seller • ProfileName • title` / `• Order : mã • {elapsed}h` / `• Created : d/M HH:mm` (giờ VN, `OrderCreateTime`) (chunk 4000 ký tự). Không gửi nếu danh sách rỗng. Tổng hợp Seller: mọi user `IsActive` có role Seller; stats theo `OwnerId`; header `🏆 SELLER SUMMARY🏆`; format `{icon} {name} : L<b>{n}</b> • O<b>{n}</b> • R<b>${x}</b>` — top 3 Revenue > 0 nhận `🥇`/`🥈`/`🥉`, còn lại `⚡️`; cuối `📊 Total`. Listed = listing `PageType=published` trong ngày; Order/Revenue = đơn trong ngày theo Ordered Time, loại cancel; Revenue = `SellerNetAmount` fallback. **Tổng hợp xử lý đơn hàng** (`TrySendFulfilSummaryAsync` → `cancel_refund_chat_id`): header `📦 Xử lý đơn hàng (dd/MM/yyyy)`; dòng seller (`OwnerId`): `• {name}: Đã mua N • Chờ mua N • 12h N` — Đã mua = `FulfilPurchasedAt` trong ngày summary; Chờ mua = snapshot tab `PENDING_PROCESS`; 12h = Chờ mua tuổi > 12 giờ; bỏ seller cả 3 = 0. Dòng người xử lý (`PaidByUserId`): `• {name}: X/Y` — X = đơn họ mua trong ngày, Y = tổng đơn đã mua trong ngày; bỏ block nếu Y = 0.

Hệ thống đọc cấu hình Bunny từ bảng `Settings` (không dùng `appsettings.json`). Mapping:

| Setting key | Dùng cho |
|-------------|----------|
| `bunny.access_key` | AccessKey upload/delete Bunny |
| `bunny.storage_zone` | Tên storage zone |
| `bunny.storage_hostname` | Storage API hostname only (vd: `sg.storage.bunnycdn.com`). Nếu dán kèm `https://` / `https//` backend tự strip scheme. |
| `bunny.cdn_hostname` | CDN pull zone public URL (vd: `xxx.b-cdn.net` hoặc `https://xxx.b-cdn.net`) |
| `bunny.max_file_size_mb` | Giới hạn upload file (MB) |

Cookie 4Seller **không** còn trong Settings — lưu per-user trên `AspNetUsers.FourSellerCookie` (xem Admin Users).

### Proxy 4Seller (chống chặn IP 567)

4Seller đứng sau EdgeOne (WAF Tencent); IP server bị gắn cờ → trả **HTTP 567 "Restricted Access"** cho toàn domain (không phải cookie hết hạn). Để né, mọi call 4Seller có thể đi qua **proxy residential**.

- **Egress:** `IFourSellerHttpClientProvider` (singleton) giữ pool `HttpClient` theo từng proxy (`SocketsHttpHandler`, `UseCookies=false`, `PooledConnectionLifetime` 5', tái dùng lâu dài). Parser nhận `ip:port`, `ip:port:user:pass`, hoặc `[scheme://][user:pass@]host:port` (**scheme mặc định http** — HTTP CONNECT tunnel cho HTTPS; prefix `https://` cho HTTPS proxy; prefix `socks5://` cho SOCKS5). Connect timeout proxy **12s** (direct 30s). `FourSellerApiService` resolve client theo seller qua `_activeProxyUrl` (đặt khi resolve cookie). Upload COS (URL tuyệt đối) luôn đi **direct**.
- **Chọn proxy (sticky tuyệt đối):** `IFourSellerProxyManager` (singleton) — khi seller có `AspNetUsers.FourSellerProxy` thì **luôn** dùng proxy đó (kể cả đang cooldown), **không** rơi vào pool và **không** fallback IP trực tiếp (nhảy IP mid-session có thể làm 4Seller kill cookie). Pool (`fourseller.proxy.enabled` + `fourseller.proxy.pool`) chỉ cho seller **không** có proxy riêng. Toggle Settings chỉ bật **pool dự phòng**.
- **Phát hiện chặn / lỗi mạng:** HTTP **567** → cooldown (`fourseller.proxy.cooldown_minutes`) + `FourSellerBlockedException` → Check login `blocked:true`. Proxy connect fail → cooldown (log reason `567` / `connect` / `407`) → thử scheme đối (http↔https) nếu chưa ghi scheme tường minh; seller sticky → `FourSellerUnavailableException` (không gọi thẳng); seller không sticky → retry 1 lần không proxy. **KHÔNG** đánh cookie `expired` khi blocked / unverified.
- **Phân loại is-login:** `FourSellerSessionProbe` — `LoggedIn` (`code:0`+`data:true`); `Expired` chỉ khi `code:0` và `data` false/null (confirm probe lần 2 trước khi latch); mọi thứ khác (`non-2xx`, non-JSON, `code≠0`, timeout) → `Unverified` + `FourSellerUnavailableException` / response `unverified:true`. Cache session process-wide (`IFourSellerSessionCache`, TTL `fourseller.session.validate_ttl_minutes`, mặc định 15p) giảm burst is-login từ DI scope song song.
- **Cấu hình:** proxy riêng từng seller ở trang Người dùng (`fourSellerProxy`); pool dự phòng + toggle + cooldown + TTL is-login ở Settings nhóm **4Seller Proxy**. Settings được cache ~30s trong manager.

## Admin KBT CRM Endpoints

Đồng bộ tổ / user / shop từ KBT CRM (`GET {kbtcrm.base_url}/agent/org`, header `x-agent-secret`), thay cho import Excel. Cấu hình ở Settings nhóm **Đồng bộ CRM (kbtcrm)**: `kbtcrm.sync.enabled`, `kbtcrm.sync.interval_minutes`, `kbtcrm.base_url` (prod bắt buộc `/api-proxy`), `kbtcrm.agent_secret`. Job nền `KbtCrmOrgSyncBackgroundService` chạy theo chu kỳ khi bật.

| Method | Endpoint | Permission | Mô tả |
|---|---|---|---|
| POST | `/admin/kbtcrm/sync` | `admin.shops.manage` | Kéo `/agent/org` ngay. Trả `KbtCrmSyncResultDto`: `crmGroups/crmUsers/crmShops`, `usersCreated/usersUpdated/usersDeactivated`, `shopsCreated/shopsUpdated/ownersTransferred`, `skipped[]` (lý do bỏ qua), `finishedAt`. 400 khi thiếu base_url/secret. |

Quy tắc map:
- User theo `username` (lowercase). Tài khoản tổ (`tai_khoan_to`) → role Seller, `TeamOwnerId` null, là **chủ dữ liệu** của tổ. Thành viên `vai_tro=seller` → role Seller, `TeamOwnerId` = tài khoản tổ. `con_lam=false` → `IsActive=false` + thu hồi token. Vai trò khác (admin/sub-admin/fulfill/designer) bỏ qua; user có role Admin bên này không bị CRM sửa.
- Mật khẩu tài khoản tạo mới: `<username>@Kbt2026` (CRM không chia sẻ hash). Email thiếu → `<username>@kbtcrm.local`.
- Shop theo `shop_code` (TikTok, upper-case) ↔ `Shop.ShopCode`. Tạo mới với Platform TikTok, Region US, Status Active, `Name` = `shop_name` (fallback `ten`), `ProfileName` = `ten`. Chủ shop = tài khoản tổ của `assignee`, fallback `quan_ly[]`. Đổi tổ → `IShopService.TransferOwnerAsync` (cascade đơn/listing + kỳ vận hành, một transaction). Shop local không có trong CRM giữ nguyên. Platform ≠ tiktok bỏ qua. `shop_status` là text tự do của TikTok, không enum.

## Admin 4Seller Endpoints

Backend proxy gọi `https://www.4seller.com` — cookie đọc từ `ApplicationUser.FourSellerCookie` của user đang gọi API (JWT), không gọi trực tiếp từ browser. Chỉ resolve cookie khi `FourSellerCookieLoginStatus=logged_in`; rồi validate `GET /api/user/is-login` (`code:0` + `data:true`). Chỉ latch `expired` khi is-login xác nhận chắc chắn (`code:0` + `data≠true`, confirm lần 2); lỗi mạng/567/transient → `FourSellerUnavailableException` / `FourSellerBlockedException` (giữ status). Cache session TTL tránh probe trùng.

**FE:** UI `/fourseller` dùng shared `@drop/fourseller` (`ADMIN_FOURSELLER_CONFIG`) — seller filter + cột owner; client `createFourSellerApi({ basePath: '/admin/fourseller' })` (+ `getShopList` cho crawl).

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| POST | `/admin/fourseller/shops/{shopId}/auth-redirect-url` | `admin.shops.manage` | Tạo OAuth authorize URL để kết nối shop với 4Seller |
| POST | `/admin/fourseller/shops/{shopId}/check-connection` | `admin.shops.manage` | Kiểm tra shop đã kết nối trên 4Seller (gọi `/api/shop/get-list`, khớp theo `platformShopName` hoặc `sellingPartnerId`) |
| POST | `/admin/fourseller/shops/sync` | `admin.shops.manage` | **Multi-cookie song song** (giống order/listing sync): users cookie + `logged_in` → mỗi tài khoản `get-tidy-list` → cập nhật shop cùng OwnerId; **relink xuyên owner** khi `sellingPartnerId` khớp shop local của seller khác (chỉ stamp `FourSellerShopId` + `PreviousFourSellerShopId`, không đổi owner). Rematch `Order.ShopId` theo id hiện tại **và** previous (không đổi `Order.OwnerId`). `SyncGate` chặn bulk chồng. Không cookie logged_in → `400`. Response: `syncedAccounts` / `failedAccounts` / `updatedCount` / `relinkedCount` / `ordersRelinkedCount` / `unmatchedShops[]` |
| GET | `/admin/fourseller/shops` | `admin.products.view` | Danh sách shop từ 4Seller (`record.Id` dùng cho crawl `shopIdList`) |
| POST | `/admin/fourseller/crawl/urls` | `admin.products.manage` | Tạo `ScrapeBatch` (CreatedById=JWT) + gửi URL → 4Seller; trả `batchId` |
| POST | `/admin/fourseller/crawl/drafts` | `admin.products.view` | Claim pending URLs → list `ScrapeDrafts` (mineOnly mặc định); Admin: `mineOnly:false` / `createdById` |
| POST | `/admin/fourseller/crawl/save-to-platform` | `admin.products.manage` | Chỉ draft thuộc user (Admin bypass) → `save-to-platform` |
| GET | `/admin/fourseller/crawl/schedule?scheduleCode=` | `admin.products.view` | Poll tiến trình schedule → `get-schedule` |
| POST | `/admin/fourseller/crawl/delete-drafts` | `admin.products.manage` | Xóa draft owned (Admin bypass) → 4Seller + xóa local |

### Product Scrape (Crawl)

Admin UI: `/product-scrape`. Mỗi user dùng **cookie 4Seller riêng** (`ApplicationUser.FourSellerCookie`). Refresh / list “Của tôi” gọi thẳng `get-move-draft-page` rồi upsert `ScrapeDrafts` `(CreatedById, FourSellerMoveDraftId)`. Filter Admin “Tất cả” đọc local.

1. **Scrape:** URL canonicalize (bỏ query/hash) → `POST get-crawl-url-repeat`. FE chỉ cần HTTP 200 / `success` — **không** poll draft pool. `data:[]` = accept (không throw); `data:[string]` = confirm; `data:[{id…}]` = claim. Draft mới: user bấm **Tải lại**.
2. **Tải lại / Refresh (không batchId):** proxy `get-move-draft-page` + upsert local; bảng mặc định **Của tôi**; Admin: Tất cả (local) / theo user (cookie user đó).
3. **Đẩy shop / Xóa** — backend enforce ownership (Admin bypass).
4. Không import vào catalog local (catalog `Products` đã gỡ); draft nằm trên 4Seller + `ScrapeDrafts`.

**Submit URLs** body: `{ "urls": ["https://us.shein.com/..."] }` → `{ "batchId", "accepted", "count", "fourSellerResponse?" }`. `accepted:true` kể cả khi 4Seller trả `data:[]`.

**Drafts** body: `{ "pageCurrent": 1, "pageSize": 50, "searchValue": null, "batchId": null, "abandonPending": false, "mineOnly": true, "createdById": null }` — có `batchId` → claim pending + list remote khớp URL batch; không `batchId` → Refresh. `abandonPending` + `batchId` = bỏ pending không khớp, đánh batch Failed nếu không claim được.

**Save to platform** body: `{ "moveDraftId": [70847980711], "shopIdList": [226276] }` — response `{ "scheduleCode": "…" }`.

**Delete drafts** body: `{ "moveDraftIds": [70848930711, 70848450711] }`.

**Schedule** query: `scheduleCode` — response `{ "total", "succeed", "failed", "failedList" }`.

### Auth Redirect URL Response

```json
{
  "success": true,
  "message": "",
  "data": {
    "authorizeUrl": "https://auth.tiktok-shops.com/oauth/authorize?..."
  },
  "errors": []
}
```

Body gửi tới 4Seller được map tự động từ shop: `platformCode` (lowercase), `shopName`, `siteList` (từ `region`), `shopifyShop` (từ `shopUrl` hoặc placeholder).

### Check Connection Response

```json
{
  "success": true,
  "data": {
    "isConnected": true,
    "matchedBy": "platformShopName",
    "matchedShop": { ... },
    "shopSynced": true,
    "fourSellerConnectedAt": "2026-07-09T14:41:31Z"
  }
}
```

Khi `isConnected: true`, backend tự cập nhật shop: `fourSellerShopId` = 4Seller **internal** `record.Id`, `externalShopId` = `sellingPartnerId`, và `fourSellerConnectedAt` từ `createTime` của 4Seller.

## Admin Category Endpoints

`TikTokCategories` là **catalog category chuẩn toàn hệ thống**, phân theo `Site` (US/UK/…). Không lưu theo shop.

Đồng bộ từ 4Seller `POST /api/meta/tiktok/get-category-list`. **Không** dùng cookie JWT admin đang đăng nhập — backend chọn **bất kỳ** user có cookie status `logged_in` (ưu tiên admin hiện tại nếu đủ điều kiện), gọi `/api/shop/get-list`, chọn shop TikTok **Connected** (ưu tiên `MarketplaceId` = site), dùng **`record.Id`** làm `shopId` (không dùng `sellingPartnerId` / `Shop.FourSellerShopId`). Nếu cookie/shop fail thì thử cookie logged_in khác. Catalog vẫn dùng chung theo site.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| POST | `/admin/categories/sync` | `admin.categories.manage` | Đồng bộ cây category hệ thống theo site |
| POST | `/admin/categories/recommend` | `admin.categories.view` | AI gợi ý category TikTok từ title (proxy 4Seller `/api/ai/generate-category-tk`) |
| GET | `/admin/categories/tree` | `admin.categories.view` | Cây category **full** cho Cascader (`site`, `activeOnly`). ⚠️ Tải cả cây (nặng) — chỉ dùng khi thật sự cần toàn bộ. UI edit đã chuyển sang lazy (`children`/`path`) |
| GET | `/admin/categories/children` | `admin.categories.view` | **Lazy load**: chỉ trả **con trực tiếp** của `parentId` (rỗng = node gốc). Query `site`, `parentId?`, `activeOnly`. Trả `CategoryTreeNodeDto[]` (`value`, `label`, `isLeaf`). Dùng cho `Cascader loadData` |
| GET | `/admin/categories/path` | `admin.categories.view` | **Seed lazy Cascader**: trả `{ pathIds[], options[] }` — cây gốc **đã pre-expand** dọc theo đường tới `externalId` (dùng cột `Path`), nhánh khác vẫn thu gọn. Cho listing có category đã lưu hiển thị đúng path mà không tải cả cây. Query `site`, `externalId`, `activeOnly` |
| GET | `/admin/categories` | `admin.categories.view` | Danh sách flat (`site`, `search`, `activeOnly`, `take?` max 200). Search không phân biệt hoa thường (tên / externalId). Khi có `search`, trả thêm `pathLabel` (tên dọc path) cho picker |

`POST /admin/categories/recommend` body: `{ "site": "US", "title": "Product name...", "ownerUserId?": "guid", "fourSellerShopId?": 12345 }`. **Cookie/proxy 4Seller:** khi `ownerUserId` có giá trị (admin edit listing → owner listing), backend dùng cookie/proxy của seller đó; khi không gửi (admin Shops page) → fallback cookie `logged_in` bất kỳ (ưu tiên admin JWT nếu eligible). `fourSellerShopId` bỏ qua auto-pick shop Connected đầu tiên. Request gửi 4Seller là form-urlencoded `{shopId, site, title}` tới `/api/ai/generate-category-tk` (đã đối chiếu với UI 4Seller — không cần field nào khác). Response trả `categoryId`, `categoryName`, `nodePath`, `pathIds[]` (để prefill Cascader), `isLeaf`, và **kết quả kiểm chứng theo catalog đã sync**: `isValidated` (category tồn tại trong `TikTokCategories` cùng site, `isActive`, là leaf), `validationWarning?` (lý do khi false — trả 200 để nút AI vẫn dùng được, FE hiện `message.warning`), `categoryPath?` (path external id từ catalog). **`pathIds` được bù từ `TikTokCategory.Path`** khi 4Seller không trả `nodePathId` — trước đây thiếu là Cascader mất đường dẫn cha. **Blocked remap:** sau khi 4Seller AI trả lời, nếu `categoryId` nằm trong Settings `listing.auto_publish.category_blocked_remap` (mặc định `903824:903952`), backend đổi sang leaf thay thế và trả thêm `replacedFromCategoryId`, `replacedFromCategoryName`, `replacementReason` — FE toast `message.info`. Áp dụng cả `POST /categories/recommend` (seller). `RecommendCategoryAsync` log Information cả request và response (title / shopId / categoryId / nodePathId) để đối chiếu với DevTools.

Dev-only (`ASPNETCORE_ENVIRONMENT=Development`): `GET /dev/listing-sync/ai-category?ownerId=&title=&shopId=&site=US` chạy đúng đường AI của auto publish B2 và echo `sentForm` (endpoint + 3 field gửi đi) · `parsed` · `catalogCheck` — dùng để so từng field với capture DevTools của UI 4Seller.

**CategoryCascader (FE) lazy loading:** component không còn tải `GET /tree`. Mount → nếu có category đã lưu, gọi `GET /path?externalId=` (1 round-trip, seed options + set value); **nếu chưa chọn** vẫn gọi `GET /children` cho node gốc (không skip `undefined === undefined`). Người dùng click mở cấp nào thì `loadData` mới gọi `GET /children?parentId=` cấp đó (giống 4Seller). Dropdown portal vào `.ant-modal-wrap` (hoặc `body`) để Cascader trong modal shop không bị focus-trap/z-index nuốt. Áp dụng cho `ListingEditPage`. Modal **Danh mục** `/shops`: ô Category mở **modal picker** riêng (cây panel rộng + tìm `GET …/categories?search=&take=80` + nút **AI** `POST …/recommend` với `title` = từ khóa đang gõ — thêm category gợi ý vào selection nhiều); Cascader `variant=panel` (menus luôn mở trong modal, không dropdown chật).

### Sync Body

```json
{
  "site": "US"
}
```

`site` tùy chọn — mặc định `US`. Cần ít nhất một tài khoản trong hệ thống có cookie 4Seller `logged_in` (Check login tại `/users`) và tài khoản đó có ít nhất 1 shop TikTok Connected trên 4Seller.

### Sync Response `data`

```json
{
  "site": "US",
  "fourSellerShopId": 226280,
  "syncedCount": 1200,
  "createdCount": 100,
  "updatedCount": 1100,
  "deactivatedCount": 5,
  "syncedAt": "2026-07-10T01:00:00Z"
}
```

`fourSellerShopId` trong response chỉ là shop credential đã dùng khi gọi 4Seller (debug), không phải scope của catalog.

### Tree node shape

```json
{
  "value": "877320",
  "label": "Women's Clothing",
  "isLeaf": false,
  "children": [
    { "value": "601226", "label": "Women Tops & Blouses", "isLeaf": true, "children": [] }
  ]
}
```

Listing create/update vẫn nhận `categoryId` / `categoryName` (external ID + name); admin-web Cascader điền tự động từ tree hệ thống theo site của shop listing.

## Admin Shop Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/shops` | `admin.shops.view` | Danh sách slim (`AdminShopListItemDto`), thứ tự **`createdAt` ASC** (tie-break `Id` — ổn định khi sửa shop, không nhảy theo SortOrder): **có** `email` / `tiktokPassword` / `twoFactorAuth` / `llcName` (ops + Xuất Excel), không metadata, **không** field tiền On hold — `receivingBankAccountId` + **đối chiếu bank last-4** (`settlementBankLast4`, `receivingBankLast4`, `bankMatchStatus`: `Matched` \| `Mismatched` \| `MissingSettlement` \| `MissingReceivingBank`; LEFT JOIN thẻ chỉ lấy `AccountNumber` để so đuôi, không trả số TK đầy đủ) + `deli` + **`fourSellerConnectedAt`** (UTC, null nếu chưa từng gắn / hàng legacy) + **hồ sơ danh mục** (`categoryId`/`categoryName` + tuổi/giá + cờ QS/Local/US WH/Shein Club + `minRating`/`minSoldCount`/`maxVariantCount` + `bodySizeCode` + `styleCodes`) + field bảng/filter + subset TTS (`shopCode`, `publishLimitPublished`/`Total`, `timezone`, `sellerType`, `tikTokShopStatus`) + **`todayOnlineSeconds`** (giây online ngày VN — cùng semantics `GET /shops`) + auto publish per-shop + **scheduler aggregate** (`autoPublishSchedulerStatus`, `autoPublishNextPickAt`, `autoPublishEligibleDraftCount` — cùng semantics `GET /shops`); kèm `publishLimitPublished`/`Total` + `activeListingCount` (= published). Chi tiết đầy đủ qua `GET /admin/shops/{id}` (cũng không trả `onHold*`). Admin UI `/shops` (shared `@drop/shops` + `ADMIN_SHOPS_CONFIG`) lọc client-side (kể cả filter **Danh mục** + filter **Bank** theo `bankMatchStatus` + Select **Sắp xếp** Profile (theo số cuối tên) / ngày thêm / Listing + nút đảo chiều tăng-giảm); nút **Xuất Excel** export client-side theo bộ lọc hiện tại (không platform/region/catalog/online/auto-publish; kèm email/password/2FA + Bank TTS / Bank hệ thống / Khớp bank); nút **Import shop** (FE-only, `admin.shops.manage`) parse Excel client-side rồi loop `POST`/`PUT /admin/shops` — cột External shop ID / Tên shop / Profile / Company / Seller username; match External ID rồi tên; Company có → LLC, trống → PER; platform/region chọn trên modal áp dụng mọi dòng; **không** endpoint bulk; cột **Profile**: `profileName · seller` cùng 1 dòng (không cột Seller riêng); cột **Shop** 1 dòng (LLC/PER + tên + DELI); cột **Danh mục** (tên category + chip body-size/style/QS/Local, tag cam **Chưa chọn**); cột **Listing** = `published/limit` (TTS `publishLimitPublished`/`Total`; không đếm listing local); cột **Online** = `todayOnlineSeconds` format `Xh Ym`; cột **Bank** = trạng thái đối chiếu last-4 (Tag Khớp/Lệch/Chưa sync TTS/Chưa gắn thẻ + Tooltip TTS · Hệ thống; click mở modal thẻ khi đã gắn); cột **Auto publish** hiện ON/OFF + badge **Processing** / **Waiting** (countdown) / **Queue +N**; **poll ngầm 12s** qua `GET …/auto-publish-scheduler-status` (chỉ ids in-flight) + patch React Query cache — không refetch full list / không spin overlay bảng; hết in-flight → invalidate list một lần; nút **Tải lại** vẫn full list; **không** cột On hold (tiền ở `/shops/finance`); bảng gọn + modal Xem (khối TikTok profile) / Sửa lazy-load detail / **Danh mục** (Cascader leaf, site khóa `shop.region`); click Tag Bank (khi đã gắn) → `GET /admin/receiving-bank-accounts/{id}`; sellers/thẻ options chỉ load khi mở form. Form gắn thẻ: label `formatReceivingBankOptionLabel` + badge **Chưa gắn**/**Đã gắn** (thẻ shop khác disabled). FE: `createShopApi({ basePath: '/admin/shops' })` |
| GET | `/admin/shops/options` | `admin.shops.view` | Dropdown tối giản `{id,name,profileName,ownerId,platformCode,status,bodySizeCode}` (SortOrder) — dùng filter Orders / thống kê / form nhẹ / câu tìm Shein trên Trending keywords; UI Orders label `profileName (name)` (profile chính) |
| GET | `/admin/shops/auto-publish/system-config` | `admin.shops.view` | `AutoPublishSystemConfigDto`: `systemAutoPublishEnabled`, `defaultIntervalSeconds`, `defaultMinDraftAgeMinutes` — gate UI ShopsPage |
| GET | `/admin/shops/auto-publish-scheduler-status` | `admin.shops.view` | Poll nhẹ badge scheduler — shape giống seller; mọi shop auto-publish-enabled trong `ids[]`. |
| GET | `/admin/shops/finance` | `admin.shops.view` | Overview tài chính mọi shop. Query `ownerUserId`, `status` (mặc định `Active`, `all` = mọi trạng thái), `from`/`to` (UTC inclusive, **chỉ** ledger tiền đã về; omit = all-time). Payload giống seller `GET /shops/finance` + `owner*` trên từng row. KPI `shopsWithoutBankCount` + `pendingConfirmationAmount` + `acquisitionCostTotal` (giá mua shop, all-time). Cột **Giá mua** chỉ đọc, chưa có giá hiện 0. UI admin-web `/shops/finance` (shared `@drop/shop-finance`, `ADMIN_SHOP_FINANCE_CONFIG`, filter Seller; tên seller gộp dòng profile, không cột Seller riêng). Click **Tiền đã về** → Drawer `GET /admin/receiving-bank-accounts/transactions?shopId=` (cùng `from`/`to`) |
| PUT | `/admin/shops/{id}/acquisition-cost` | `admin.company-ledger.manage` | Upsert giá mua shop trên sổ cái (`Source=ShopAcquisition`, unique `ShopId`). Body `{ amount, occurredAt?, note? }`. `amount` null hoặc ≤ 0 → xóa dòng. Tạo mới gán `ownerUserId = shop.ownerId`; lần sửa giữ seller đã gán. Form **Sửa shop** (admin, quyền này) gửi sau khi lưu shop. Không nằm trong `admin.shops.manage`. |
| GET | `/admin/shops/{id}` | `admin.shops.view` | Chi tiết shop (không trả `onHold*` — tiền ở `GET /admin/shops/finance`). Thêm `acquisitionCost` (USD, null nếu chưa có dòng `ShopAcquisition`) |
| POST | `/admin/shops` | `admin.shops.manage` | Tạo shop và cấp cho seller (`ownerId`); `sortOrder` = max + 1 (cuối danh sách). `externalShopId` unique **xuyên seller**: trùng → 400 kèm tên shop + seller đang giữ (chuyển shop đó sang seller mới thay vì tạo hàng thứ hai, nếu không listing/đơn/dữ liệu NCC sẽ kẹt lại hàng cũ). Tạo kèm `fourSellerShopId` → **tự rematch** đơn mồ côi đã sync trước đó dưới id này (`Order.ShopId` null → gán shop mới), không cần chạy đồng bộ shop 4Seller. |
| PUT | `/admin/shops/{id}` | `admin.shops.manage` | Cập nhật shop / đổi seller. **Không** ghi hồ sơ danh mục / `ProductCategories`. Đổi `status` → ghi `ShopOperationalPeriods` + Telegram cảnh báo đổi trạng thái (source `admin`). Đổi `fourSellerShopId` → id cũ lưu vào `previousFourSellerShopId` để rematch sau này còn tìm được Orders/Listings mang id cũ, và **rematch chạy ngay** cho đơn mang id mới hoặc id cũ (không cần đợi đồng bộ shop 4Seller); rematch lỗi chỉ ghi log, không làm hỏng lượt lưu shop. **Đổi `ownerId`** → cascade `ShopOwnershipTransferService`: rematch (theo `FourSellerShopId` **hoặc** `PreviousFourSellerShopId`) + chuyển `OwnerId` cho Orders / Listings / analytics / discount / saved keywords / listing-add-origin / `ListingAutoPublishRun` / `ShopTaskCompletion` / `TikTokShopSyncLog`; **xóa `ListingSyncWatermark` của seller nhận** để lượt sync kế tiếp full-scan (cursor delta của họ sẽ bỏ qua hàng cũ hơn của shop vừa nhận); trùng `PlatformOrderId` với seller mới → **merge** một row (`ordersMerged`), không lỗi conflict. Toàn bộ nhánh đổi owner (cascade + `ShopOperationalPeriods`) chạy trong **một transaction** — cascade có flush giữa chừng nên nếu không bọc, lỗi ở bước sau để lại shop đã đổi chủ mà thiếu period `OwnerChanged`. `externalShopId` cũng bị chặn trùng xuyên seller như khi tạo. |
| POST | `/admin/shops/{id}/reassign-owned-data` | `admin.shops.manage` | Backfill ownership + **gộp đơn trùng** cùng `PlatformOrderId` trên shop + **gộp listing trùng** cùng `TikTokProductId` — kể cả khi hai bản nằm ở hai hàng `Shops` cùng `ExternalShopId`; bản giữ lại nhận thêm mọi field bản shell có mà nó thiếu (`SourceUrl`, `SupplierProductInfoId`, `ReviewCount`/`AverageRating`, `RawPayload`, description, category, `AppliedPricingJson`, …) và **thừa kế variation** nếu nó chưa có. Cũng reclaim listing lệch owner, **rematch** listing/order `ShopId` null khớp `FourSellerShopId`/`PreviousFourSellerShopId` (kể cả khi OwnerId đã đúng), và **điền** `Listing.SourceUrl` trống từ `OrderItem` (shop + SellerSku). Response `ShopOwnershipTransferResultDto` (`ordersMerged`, `listingsCollapsed`, `listingsRematched`, …). UI form Sửa: **Đồng bộ ownership**. |
| POST | `/admin/shops/reassign-all-owned-data` | `admin.shops.manage` | Chạy reassign cho **mọi** shop còn Order/Listing lệch `Shop.OwnerId`, có shell trùng TikTok product, **hoặc** có listing/order orphan (`ShopId` null) khớp `FourSellerShopId` / `PreviousFourSellerShopId`. Response cùng DTO (`shopsProcessed`, tổng counters). UI `/shops`: **không** hiện nút header bulk — chỉ nút trên Alert mapping-health khi còn đơn orphan; per-shop qua form Sửa. Restart API **không** thay cho endpoint này. |
| GET | `/admin/shops/mapping-health` | `admin.shops.view` | Chẩn đoán đơn `ShopId` null: `unlinkedOrderCount` + `groups[]` gom theo `FourSellerShopId` (`orderCount`, `ownerId`/`ownerName`, `firstOrderAt`/`lastOrderAt`, `hasLocalShop`). UI `/shops` hiện Alert khi còn đơn chưa map. |
| GET | `/admin/age-ranges` | `admin.shops.view` | Giống seller `GET /age-ranges` (preset khoảng tuổi). |
| PUT | `/admin/shops/{id}/catalog-profile` | `admin.shops.manage` | Giống seller `PUT /shops/{id}/catalog-profile` (mọi shop). Response slim `ShopCatalogProfileDto`. |
| PUT | `/admin/shops/{id}/auto-publish` | `admin.shops.manage` | Giống seller `PUT /shops/{id}/auto-publish`. Response slim `ShopAutoPublishDto`. |
| POST | `/admin/shops/{id}/what-to-sell-today` | `admin.shops.view` | Giống seller `POST /shops/{id}/what-to-sell-today` (mọi shop, không scope owner) |
| PUT | `/admin/shops/reorder` | `admin.shops.manage` | Sắp xếp lại toàn bộ shop (body `orderedIds` phải khớp exact tập shop hiện có; first = SortOrder 0) |
| DELETE | `/admin/shops/{id}` | `admin.shops.manage` | Xóa shop |

## Admin Product opportunities

Trending keywords TikTok Shop unique theo `lead_id` (extension scrape); shop gắn qua `ProductOpportunityShopLinks`. UI admin-web `/product-opportunities` (shared `@drop/product-opportunities`, `ADMIN_PRODUCT_OPPORTUNITIES_CONFIG`).

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/product-opportunities` | `admin.product_opportunities.view` | Paged unique `lead_id` (EXISTS shop-link). Khi lọc `shopId`, cộng bookmark **không snapshot** (đề xuất «Shop này bán gì»). Query `shopId`, `ownerId`/`ownerUserId`, `q`, `saved` (true = chỉ keyword **đã lưu của shop đang lọc**; không lọc shop = bookmark của shop đang link), `leadTag` (CSV hoặc lặp; OR), `page`/`pageSize`, `sort` (`volume` mặc định / `online` / `order` / `gmv` / `name` / `synced`), `sortDir` (`desc` mặc định cho volume/online/order/gmv/synced; `asc` mặc định cho name). `online`/`order`/`gmv` null luôn xếp cuối. Response `{ items, total, pageCurrent, pageSize, lastSyncedAt, leadTagOptions[] }` (`lastSyncedAt` theo shop/owner, không phụ thuộc `q`; `leadTagOptions` theo shop/owner/`q`/`saved`, không hẹp theo `leadTag`). Item: keyword + `leadTagTexts[]`, `isSaved` **theo shop của dòng**, `shopCount`, `picUrl`+`picUrls[]`, volume/product/order/gmv (+ ring), `categories[]` (+ L1–L3), shop đại diện/owner, `shopSyncedAt`. UI bảng ẩn cột Order/GMV. Mobile: chỉ Keyword + Volume + Product; không checkbox/bulk-delete. |
| POST | `/admin/product-opportunities/bulk-delete` | `admin.product_opportunities.view` | Xóa keyword đã chọn. Body `{ ids: Guid[], shopId? }` (1–200). Có `shopId`: chỉ unlink shop đó. Không `shopId`: xóa keyword gốc (+ cascade mọi shop-link). Bookmark **của shop bị unlink** + gỡ jsonb research của shop đó. Response `{ deleted }`. |
| PUT | `/admin/product-opportunities/{id}/saved` | `admin.product_opportunities.view` | Lưu / bỏ lưu keyword **theo shop** (bookmark thuộc owner của shop, không phải admin). Body `{ saved: bool, shopId? }`. Unique `(ShopId, KeywordNormalized)`. Bỏ lưu: chỉ xóa bookmark shop đó (snapshot + research item giữ). Response `{ saved }`. |
| PUT | `/admin/product-opportunities/saved-keywords` | `admin.product_opportunities.view` | Lưu / bỏ lưu theo chữ keyword **của shop** (không có dòng snapshot). Body `{ shopId, keyword, saved }`. Unique `(ShopId, KeywordNormalized)`. Bỏ lưu không snapshot → gỡ khỏi jsonb research shop đó. |
| GET | `/admin/product-opportunities/saved-keywords` | `admin.product_opportunities.view` | Giống seller `GET /product-opportunities/saved-keywords` (mọi shop, không scope owner). |
| GET | `/admin/product-opportunities/shop-research` | `admin.product_opportunities.view` | Giống seller `GET /product-opportunities/shop-research` (mọi shop, không scope owner). |
| POST | `/admin/product-opportunities/shop-research` | `admin.product_opportunities.view` | Giống seller `POST /product-opportunities/shop-research` (mọi shop, không scope owner). Ghi đè `ShopKeywordResearches`. |

### Catalog profile body

`PUT /shops/{id}/catalog-profile` và `PUT /admin/shops/{id}/catalog-profile`:

```json
{
  "categories": [
    { "id": "605196", "name": "Automotive & Motorcycle" },
    { "id": "601152", "name": "Women's Clothing / Dresses / Mini Dresses" }
  ],
  "ageMin": 18,
  "ageMax": 35,
  "priceMinUsd": 9.99,
  "priceMaxUsd": 29.99,
  "preferQuickShip": true,
  "preferLocal": false,
  "preferUsWarehouse": false,
  "preferSheinClub": false,
  "minRating": 4.5,
  "minSoldCount": 100,
  "maxVariantCount": 8,
  "bodySizeCode": "midsize",
  "styleCodes": ["elegant", "casual"],
  "autoPublishEnabled": true,
  "autoPublishIntervalSeconds": 120,
  "autoPublishMinDraftAgeMinutes": 30
}
```

`categories` bắt buộc (1–20). Mỗi `id` phải active trong `TikTokCategories` cùng site `shop.region` (parent **hoặc** leaf). Server tự build `name` từ path catalog; client `name` chỉ fallback. `CategoryId` = id đầu; `CategoryName` = các name nối ` · `; JSON đầy đủ ở `CatalogCategories`. Legacy `categoryId` (+ `categoryName`) vẫn map thành 1 phần tử khi `categories` trống. Tuổi/giá optional (min ≤ max). `bodySizeCode` optional: `regular` | `plus` | `midsize` | `curvy` | `petite` | `tall` (null/omit = clear). `styleCodes` optional: mảng `elegant` | `casual` | `vintage` | `sexy` | `boho` | `cute` | `glamorous` | `vacation` | `modest` | `party` (max 10, unique; khớp facet Style Shein; `[]`/omit = clear; code cũ `casual-woman`/`royal` bị bỏ im). `minRating` 0–5 (0 = không lọc), `minSoldCount` ≥ 0 (0 = không lọc), `maxVariantCount` 0–500 (`variants < N`; 0 = không lọc). `preferSheinClub` + `preferUsWarehouse` (UI **US Warehouse**) + QS/Local: Checkbox, chọn nhiều. `autoPublishEnabled` / `autoPublishIntervalSeconds` (≥1 giây) / `autoPublishMinDraftAgeMinutes` (≥0 phút; draft mới hơn giá trị này không vào FIFO auto publish — 0 = không chờ; null shop → default Settings). **Không** cho `autoPublishEnabled=true` khi system setting tắt (400). Response có `categories[]` + denormalize + flags — FE patch 1 dòng list. Create/sửa ops shop không nhận các field này. Đổi region/marketplace site → xóa toàn bộ category (cần chọn lại đúng site). UI `/shops` Cascader **multiple** (checkbox đầu mỗi dòng).

### Reorder Admin Shops Body

```json
{
  "orderedIds": ["guid-first", "guid-second", "guid-third"]
}
```

`orderedIds` phải chứa **mọi** shop ID đúng một lần. FE merge: khi đang lọc/phân trang chỉ đổi chỗ các dòng đang thấy, rồi ghép lại vào đúng slot của danh sách đầy đủ trước khi gọi API (pattern giống `/admin/variation-spec-templates/reorder`).

### Create Admin Shop Body

```json
{
  "ownerId": "guid-seller-id",
  "platformId": "guid-platform-id",
  "name": "Shop Name",
  "region": "US",
  "externalShopId": "optional — có thể bỏ trống, nhập sau",
  "avatarUrl": "https://example.com/avatar.png",
  "fourSellerShopId": "optional",
  "shopUrl": "optional",
  "status": "Active",
  "proxy": "66.93.162.163:5",
  "profileName": "Profile-US-01",
  "email": "info@example.com",
  "tiktokPassword": "optional",
  "dayLive": "954 days",
  "deli": 8,
  "newPhoneNumber": "optional",
  "receivingBankAccountId": "guid-optional",
  "newBankNumber": "optional — legacy / denormalized from receiving bank",
  "paymentName": "JOHN DOE — legacy / denormalized from receiving bank",
  "shopType": "Llc",
  "llcName": "Cathedra, LLC",
  "twoFactorAuth": "optional"
}
```

### Shop Extended Fields

| Field | Type | Mô tả |
|-------|------|-------|
| `region` | string? | Mã region TikTok Shop (mặc định `US`). Dùng cho 4Seller `siteList` |
| `proxy` | string? | Proxy IP:Port |
| `profileName` | string? | Tên profile trình duyệt đã login (max 200) |
| `email` | string? | Email tài khoản shop |
| `tiktokPassword` | string? | Mật khẩu TikTok |
| `dayLive` | string? | Số ngày live (VD: "954 days") |
| `deli` | int? | Hạn thanh toán (DELI, số ngày). Có trên list admin (`AdminShopListItemDto`) + detail/create/update. Extension sync từ TTS `bill_finish_period_in_days` |
| `onHoldAmount` | decimal? | Tổng On hold TTS (`to_settle_amount_stat`). Lưu DB khi extension sync; **không** trên list/detail shop — chỉ `GET …/shops/finance` |
| `onHoldInTransitAmount` | decimal? | On hold — chờ giao hàng (`unsettled_statement_reasons_in_transit_new`). Chỉ finance API |
| `onHoldAwaitingSettlementAmount` | decimal? | On hold — đã giao chờ TT (`unsettled_statement_reasons_settlement_in_progress_new`). Chỉ finance API |
| `onHoldReserveAmount` | decimal? | On hold — tiền giữ lại (`finance_payments_table_type_reserve`). Chỉ finance API |
| `newPhoneNumber` | string? | Số điện thoại |
| `receivingBankAccountId` | guid? | FK thẻ nhận tiền (`ReceivingBankAccounts`). Mỗi shop gắn tối đa 1 thẻ |
| `settlementBankLast4` | string? | (admin list) 4 số cuối bank TikTok settlement (`SettlementBankAccountNumber`) |
| `receivingBankLast4` | string? | (admin list) 4 số cuối thẻ nhận tiền hệ thống |
| `bankMatchStatus` | string | (admin list) Đối chiếu last-4: `Matched` \| `Mismatched` \| `MissingSettlement` \| `MissingReceivingBank` |
| `receivingBankAccountName` | string? | (response) Tên thẻ nhận tiền |
| `receivingBankAccountNumber` | string? | (response) Số TK từ thẻ |
| `receivingBankAccountHolderName` | string? | (response) Chủ TK từ thẻ |
| `newBankNumber` | string? | Số tài khoản bank (legacy; đồng bộ từ thẻ khi gắn) |
| `paymentName` | string? | Tên thanh toán tiền **của shop** (tự nhập; không đồng bộ từ thẻ) |
| `shopType` | string? | Loại shop Per/LLC: `Per` (Shop cá nhân) hoặc `Llc` (Shop công ty) |
| `llcName` | string? | Tên LLC |
| `twoFactorAuth` | string? | Mã 2FA |
| `avatarUrl` | string? | URL ảnh đại diện shop |
| `sortOrder` | int | Legacy — vẫn lưu / cập nhật qua `PUT /admin/shops/reorder`; **không** còn là khóa sort của `GET /admin/shops` / `GET /shops` (list theo `createdAt` ASC). Dropdown `/admin/shops/options` vẫn `OrderBy(SortOrder)` |
| `fourSellerConnectionStatus` | string | Trạng thái kết nối 4Seller: `Connected` / `NotConnected` (computed từ `fourSellerShopId`) |
| `fourSellerConnectedAt` | datetime? | Thời điểm kết nối 4Seller (UTC), set khi `fourSellerShopId` được gán lần đầu |

## Admin / Seller Company Ledger

Sổ cái thu/chi vận hành (proxy, OpenAI, Cursor, tiền mua shop…). Tách khỏi tiền về và fulfil. `shopId` và `ownerUserId` đều optional, độc lập. Category: `SHOP_ACQUISITION` | `PROXY` | `OPENAI_TOPUP` | `CURSOR_BUY` | `CURSOR_TOPUP` | `OTHER_EXPENSE` | `OTHER_INCOME`. `SHOP_ACQUISITION` + `shopId` upsert unique (direction khóa Chi, **currency khóa USD**). Summary `byCurrency[]` (`creditTotal`, `debitTotal`, `net`) theo đúng filter, không cộng lẫn USD/VND.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/company-ledger` | `admin.company-ledger.view` | List + summary. Query `from`/`to` (`occurredAt`), `direction`, `categoryCode`, `ownerUserId`, `ownerUnassigned`, `shopId`, `shopUnassigned`, `search` (title/note). Admin thấy cả dòng không gán. UI `/system/ledger` |
| GET | `/admin/company-ledger/categories` | `admin.company-ledger.view` | Catalog cố định `{ code, name, defaultDirection }` |
| POST | `/admin/company-ledger` | `admin.company-ledger.manage` | Tạo. Body direction, categoryCode, amount > 0, currency `USD`\|`VND`, title? (tối đa 200, để trống được), note?, occurredAt, shopId?, ownerUserId?. Acquisition có shop thì cập nhật dòng unique và **khóa `currency=USD`** (cột Giá mua trên Tài chính shop) |
| PUT | `/admin/company-ledger/{id}` | `admin.company-ledger.manage` | Sửa. Gỡ shop trên dòng acquisition → `source=Manual`. Chuyển sang shop đã có giá mua → 400 |
| DELETE | `/admin/company-ledger/{id}` | `admin.company-ledger.manage` | Xóa dòng |
| GET | `/company-ledger` | `seller.company-ledger.view` | Chỉ đọc. Scope: `ownerUserId = JWT` **hoặc** shop đang thuộc JWT. Bỏ qua `ownerUserId`/`ownerUnassigned` từ client. UI seller `/ledger` |
| GET | `/company-ledger/categories` | `seller.company-ledger.view` | Cùng catalog, để hiện nhãn |

## Admin Receiving Bank Account Endpoints

Thẻ nhận tiền (catalog global account). Permission: `admin.shops.view` / `admin.shops.manage`.

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/receiving-bank-accounts/sources` | `admin.shops.view` **hoặc** `admin.fulfil.view` | Nguồn thẻ (PingPongX / WorldFirst / Khác; `supportsAccounts`) |
| GET | `/admin/receiving-bank-accounts/source-accounts` | `admin.shops.view` **hoặc** `admin.fulfil.view` | Danh sách tài khoản nguồn (`?sourceId=`) — payload bảng (không `sourceCode`/timestamps) |
| GET | `/admin/receiving-bank-accounts/source-accounts/options` | `admin.shops.view` **hoặc** `admin.fulfil.view` | Dropdown tài khoản nguồn active |
| POST | `/admin/receiving-bank-accounts/source-accounts` | `admin.shops.manage` **hoặc** `admin.fulfil.manage` | Tạo tài khoản nguồn |
| PUT | `/admin/receiving-bank-accounts/source-accounts/{id}` | `admin.shops.manage` **hoặc** `admin.fulfil.manage` | Cập nhật tài khoản nguồn |
| DELETE | `/admin/receiving-bank-accounts/source-accounts/{id}` | `admin.shops.manage` **hoặc** `admin.fulfil.manage` | Xóa (chặn nếu còn thẻ nhận tiền / thẻ fulfil) |
| GET | `/admin/receiving-bank-accounts` | `admin.shops.view` | Danh sách slim (`ReceivingBankAccountListItemDto`): nguồn/TK nguồn, currency, holder, account number, `shopCount`, `shops[]` — **không** kèm bank name/SWIFT/routing/address/timestamps. Chi tiết đầy đủ qua `GET …/{id}` |
| GET | `/admin/receiving-bank-accounts/options` | `admin.shops.view` | Dropdown thẻ active (+ `sourceId` / `sourceAccountId` để cascade filter stats). Form gắn shop (`@drop/shops`): label `formatReceivingBankOptionLabel` + badge Chưa gắn/Đã gắn; thẻ đã gắn shop khác disabled |
| GET | `/admin/receiving-bank-accounts/{id}` | `admin.shops.view` | Chi tiết đầy đủ (bank fields + shops) |
| POST | `/admin/receiving-bank-accounts` | `admin.shops.manage` | Tạo thẻ (`sourceId` bắt buộc; `sourceAccountId` bắt buộc khi nguồn `supportsAccounts`) |
| PUT | `/admin/receiving-bank-accounts/{id}` | `admin.shops.manage` | Cập nhật; đồng bộ `NewBankNumber` các shop đã gắn (không đụng `PaymentName`) |
| DELETE | `/admin/receiving-bank-accounts/{id}` | `admin.shops.manage` | Xóa (chặn nếu còn shop gắn hoặc còn lịch sử giao dịch) |

**Import hàng loạt (UI admin `/receiving-banks`):** không có bulk endpoint — FE parse Excel PingPongX Global Account rồi gọi lần lượt `POST`/`PUT` ở trên. Upsert theo `sourceId` + `accountNumber` (cùng nguồn thẻ đã chọn trong modal import). `accountType` lấy từ cột Excel (modal chỉ fallback khi thiếu).
| GET | `/admin/receiving-bank-accounts/transactions` | `admin.shops.view` | Lịch sử tiền về (`?receivingBankAccountId` / `shopId` / `ownerUserId` / `orderId` / `source` / `from` / `to`); `ownerUserId` lọc txn gắn shop của seller đó (loại Manual `ShopId=null`). Response có `confirmationStatus`, `shopProfileName`, `shopOwnerName`, `externalTransactionId`, `source` (`Manual`\|`SettlementPaid`\|`TikTokPayout`), `payoutOrders[]` (breakdown TTS khi `source=TikTokPayout`). UI: filter Seller + Shop (cascade shop theo seller); tag Loại + icon Eye xem chi tiết payout. |
| GET | `/admin/receiving-bank-accounts/transactions/stats` | `admin.shops.view` | Thống kê tiền về — **chỉ txn `ConfirmationStatus=Confirmed`** (cùng filter + `sourceId` / `sourceAccountId` / `txnSource`): `totalAmount` = Credit−Debit; `creditAmount` / `debitAmount`; `orderCount`; breakdown thẻ / nguồn / TK nguồn / shop; mỗi bucket có credit/debit/orderCount; `byCard.name` = Holder · masked, `byCard.extra` = SourceName · currency; series ngày theo **Asia/Ho_Chi_Minh** |
| GET | `/admin/receiving-bank-accounts/transactions/{id}` | `admin.shops.view` | Chi tiết giao dịch |
| POST | `/admin/receiving-bank-accounts/transactions` | `admin.shops.manage` | Tạo Manual: `shopId` bắt buộc; thẻ derive từ shop; Direction Credit/Debit; `ConfirmationStatus=Confirmed` |
| PUT | `/admin/receiving-bank-accounts/transactions/{id}` | `admin.shops.manage` | Cập nhật; SettlementPaid không đổi order/shop/card/direction; TikTokPayout chỉ cho sửa `note`; Manual: card derive lại từ shop |
| POST | `/admin/receiving-bank-accounts/transactions/{id}/confirmation-status` | `admin.shops.manage` | Đổi nhanh trạng thái. Body `{ confirmationStatus: Pending\|Confirmed\|Rejected }` cho Manual / TikTokPayout. SettlementPaid không đổi. UI: icon Swap cột Thao tác |
| POST | `/admin/receiving-bank-accounts/transactions/{id}/confirm` | `admin.shops.manage` | Duyệt → Confirmed (`Đã duyệt`; Manual hoặc TikTokPayout; sau đó mới vào stats) |
| POST | `/admin/receiving-bank-accounts/transactions/{id}/reject` | `admin.shops.manage` | Không duyệt Manual/TikTokPayout → Rejected (không tính tổng). SettlementPaid → lỗi nghiệp vụ |
| DELETE | `/admin/receiving-bank-accounts/transactions/{id}` | `admin.shops.manage` | Xóa; SettlementPaid → `Order.IsSettlementPaid=false`; TikTokPayout → clear `TikTokPaymentId` + `IsSettlementPaid` các đơn gắn payment_id |

### Create / Update Receiving Bank Transaction Body

```json
{
  "shopId": "…",
  "orderId": null,
  "amount": 25.80,
  "direction": "Credit",
  "currency": "USD",
  "externalTransactionId": "optional — mã GD nền tảng gửi tiền",
  "note": "optional",
  "occurredAt": "2026-07-19T05:00:00Z"
}
```

`receivingBankAccountId` không gửi trên wire — server lấy từ `Shop.ReceivingBankAccountId`. Seller create luôn Credit + Pending; admin create Credit/Debit + Confirmed. Response list có `confirmationStatus` (`Pending` \| `Confirmed` \| `Rejected`) và `externalTransactionId`. UI: **Chờ duyệt** / **Đã duyệt** / **Không duyệt**. **`externalTransactionId` unique** khi có giá trị (trùng → lỗi nghiệp vụ).
### Create Source Account Body

```json
{
  "sourceId": "b2000000-0000-0000-0000-000000000001",
  "name": "PingPong — Bay US",
  "username": "bay@example.com",
  "externalAccountId": "optional",
  "note": "optional",
  "isActive": true
}
```

### Create / Update Receiving Bank Body

```json
{
  "sourceId": "b2000000-0000-0000-0000-000000000001",
  "sourceAccountId": "guid-pingpong-account",
  "currency": "USD",
  "bankName": "Deutsche Bank Trust Company Americas",
  "bankLocation": "United States",
  "accountHolderName": "NGUYEN DAC BAY",
  "accountType": "Checking",
  "swiftBic": "BKTRUS33XXX",
  "routingNumber": "021001033",
  "accountNumber": "00395823000149549",
  "bankAddress": "One Columbus Circle",
  "isActive": true
}
```

| Field | Type | Mô tả |
|-------|------|-------|
| `name` | string | (response) Tự sinh từ `accountHolderName · accountNumber` |
| `sourceId` | guid | Nguồn thẻ (PingPongX / WorldFirst / Khác) |
| `sourceAccountId` | guid? | Tài khoản nguồn — bắt buộc khi nguồn `supportsAccounts` |
| `currency` | string | Loại tiền (USD, …) |
| `bankName` | string | Tên ngân hàng |
| `bankLocation` | string? | Quốc gia / location |
| `accountHolderName` | string | Tên chủ tài khoản |
| `accountType` | string? | Checking / Savings |
| `swiftBic` | string? | SWIFT / BIC |
| `routingNumber` | string? | ABA routing |
| `accountNumber` | string | Số tài khoản |
| `bankAddress` | string? | Địa chỉ ngân hàng |
| `isActive` | bool | Cho phép chọn trên form shop |
| `shopCount` | int | (response) Số shop đang gắn |
| `shops` | array | (response) Shop gắn: `id`, `name`, `profileName` — UI cột Shop: `Name · ProfileName` |

## Admin User Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/users` | `admin.users.view` | Danh sách tất cả người dùng (`hasFourSellerCookie`, `fourSellerCookieLoginStatus`, `hasFulfilPin`, không trả cookie/PIN thô) |
| GET | `/admin/users/{id}` | `admin.users.view` | Chi tiết người dùng |
| POST | `/admin/users` | `admin.users.manage` | Tạo người dùng mới (optional `fourSellerCookie`, `fourSellerProxy`, `fulfilPin`) |
| PUT | `/admin/users/{id}` | `admin.users.manage` | Cập nhật người dùng / gán role / cookie 4Seller / proxy 4Seller / mã PIN extension |
| POST | `/admin/users/import` | `admin.users.manage` | **Import hàng loạt (upsert)** — xem mục *Import tài khoản* bên dưới |
| DELETE | `/admin/users/{id}` | `admin.users.manage` | **Xóa hẳn** người dùng (hard delete). Chặn tự xóa / Admin cuối cùng / còn shop·đơn·listing·sổ cái·fulfil·chat·template gắn Restrict FK — dùng `PUT … isActive=false` để vô hiệu hóa tạm |
| PUT | `/admin/users/{id}/reset-password` | `admin.users.manage` | Đặt lại mật khẩu |
| POST | `/admin/users/{id}/four-seller-cookie/check-login` | `admin.users.view` | Kiểm tra session 4Seller của user (`is-login`); optional body cookie = draft; hết hạn thật → status `expired`; **567 → `blocked:true`**, **mạng/proxy/5xx → `unverified:true`** — giữ nguyên status (không đánh `expired`) |
| POST | `/admin/users/four-seller-cookie/check-login` | `admin.users.view` | Bulk check login: mọi user có cookie, hoặc filter `userIds`; hết hạn thật → status `expired`; user bị 567 / unverified → giữ status + đếm riêng trong message summary |

**`fourSellerProxy`** (per-seller, sticky egress proxy): body `POST`/`PUT /admin/users` nhận `fourSellerProxy` (null = giữ nguyên; chuỗi rỗng = xóa; non-empty = thay). Định dạng: `ip:port:user:pass`, `ip:port` (whitelist IP), hoặc `[scheme://][user:pass@]host:port` (**scheme mặc định http CONNECT**; prefix `https://` cho HTTPS proxy; prefix `socks5://` cho SOCKS5). `AdminUserDto` trả `fourSellerProxy` (chỉ admin, permission-gated). Xem mục **Proxy 4Seller** bên dưới.

**`fulfilPin`** (mã PIN 4 số cho extension KBI Fulfil): body `POST`/`PUT /admin/users` nhận `fulfilPin` (omit/`null` = giữ nguyên khi sửa; chuỗi rỗng = xóa; đúng 4 chữ số = đặt/đổi). PIN phải **duy nhất** toàn hệ thống; lưu `AspNetUsers.FulfilPinHash` = HMAC-SHA256(`Jwt:PinPepper` hoặc fallback `Jwt:Secret`, pin) — **write-only**, response chỉ có `hasFulfilPin`. Đổi/xóa PIN → revoke mọi refresh token. Admin UI `/users` cột PIN + field trong modal Thêm/Sửa.
| GET | `/admin/users/sellers` | `admin.users.manage` | Danh sách seller active (dropdown) |
| GET | `/admin/users/options` | `admin.users.view` | User active tối giản `{id,userName,fullName,isActive}` — filter dropdown (không roles/secrets). Query **`roles`** = tên role cách nhau bằng dấu phẩy (OR), vd. `roles=Admin,Fulfil` cho dropdown **người fulfil**; role không tồn tại bị bỏ qua (không lỗi); bỏ trống = mọi user active |
| GET | `/admin/users/role-options` | `admin.users.manage` | Danh sách vai trò (dropdown gán user) |

### Import tài khoản (`POST /admin/users/import`)

Upsert hàng loạt từ file Excel admin tải lên — **tối đa 500 dòng / lần**.

```json
{
  "items": [
    { "userName": "fulfil01", "email": "fulfil01@kbt.global", "fullName": "Nguyễn Văn A", "password": "Abc@12345", "roles": ["Fulfil"] }
  ]
}
```

Xử lý từng dòng (một dòng lỗi **không** chặn các dòng còn lại):

- Khớp tài khoản sẵn có theo **`userName`** (`FindByNameAsync`) **hoặc** **`email`** (`FindByEmailAsync`).
- Đã tồn tại → cập nhật `email`, `fullName`, **thay toàn bộ role**, và **đặt lại mật khẩu** (reset token + revoke mọi refresh token). Giữ nguyên `isActive`, cookie 4Seller, proxy, mã PIN.
- Chưa tồn tại → tạo mới với `isActive = true`.
- Bỏ qua kèm lý do khi: thiếu field / email sai định dạng / mật khẩu không đạt policy / role không tồn tại / `userName`–`email` trùng lặp trong cùng payload / `userName` và `email` đang thuộc **hai** tài khoản khác nhau / dòng trỏ vào **chính tài khoản đang đăng nhập** (tránh tự đăng xuất giữa chừng).

Response: `{ "created": 3, "updated": 2, "failed": [{ "index": 5, "userName": "x", "error": "…" }] }` — `index` là vị trí 0-based trong mảng `items` đã gửi.

Admin UI `/users` → nút **Import file**: parse `.xlsx` / `.xls` phía client (cột **Tên đăng nhập**, **Mật khẩu**, **Họ tên**, **Email** tuỳ chọn). Thiếu email → FE tự sinh `{userName}@kbt.global` (local-part chỉ giữ chữ/số/`.`/`_`/`+`/`-`). Mật khẩu lấy đúng theo file và phải đạt policy (≥8 ký tự, hoa, thường, số — **không** bắt ký tự đặc biệt). Preview kèm nhãn Thêm mới/Cập nhật và combobox vai trò từng dòng (mặc định **`Seller`**), xác nhận mới gọi API.

### Admin Check 4Seller Login (single)

Body giống self-service: `fourSellerCookie` tùy chọn — có thì kiểm tra draft; omit/rỗng thì dùng cookie đã lưu của `{id}`.

```json
{
  "isLoggedIn": true,
  "message": "Cookie 4Seller hợp lệ — đã đăng nhập.",
  "hasFourSellerCookie": true,
  "fourSellerCookieLoginStatus": "logged_in",
  "cookieCleared": false,
  "blocked": false,
  "unverified": false
}
```

### Admin Check 4Seller Login (bulk)

```json
{ "userIds": ["optional-guid-filter"] }
```

Omit/`userIds` rỗng → chỉ user có cookie. Response: mảng `{ userId, userName, fullName, hasCookie, isLoggedIn, fourSellerCookieLoginStatus, cookieCleared, blocked, unverified, message }`. Cookie hết hạn thật → `fourSellerCookieLoginStatus=expired` (giữ cookie; `cookieCleared` luôn `false`). `blocked`/`unverified` → giữ status hiện tại.

### Create Admin User Body

Mật khẩu phải đáp ứng chính sách Identity: tối thiểu 8 ký tự, gồm chữ hoa, chữ thường và số (không bắt buộc ký tự đặc biệt).

```json
{
  "userName": "newuser",
  "email": "user@example.com",
  "fullName": "Full Name",
  "password": "Password@123",
  "roles": ["Seller"],
  "fourSellerCookie": "optional-session-cookie"
}
```

### Update Admin User Body

`fourSellerCookie`: omit/`null` = giữ nguyên; chuỗi rỗng = xóa; non-empty = thay thế. Response chỉ có `hasFourSellerCookie` (không trả giá trị cookie).

```json
{
  "email": "user@example.com",
  "fullName": "Full Name",
  "isActive": true,
  "roles": ["Seller"],
  "fourSellerCookie": "new-session-cookie"
}
```

## Admin Role & Permission Endpoints

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/admin/roles` | `admin.roles.view` | Danh sách vai trò |
| GET | `/admin/roles/{id}` | `admin.roles.view` | Chi tiết vai trò + permission IDs |
| POST | `/admin/roles` | `admin.roles.manage` | Tạo vai trò mới |
| PUT | `/admin/roles/{id}` | `admin.roles.manage` | Cập nhật vai trò |
| DELETE | `/admin/roles/{id}` | `admin.roles.manage` | Xóa vai trò (không áp dụng role hệ thống) |
| PUT | `/admin/roles/{id}/permissions` | `admin.roles.manage` | Gán permissions cho vai trò |
| GET | `/admin/permissions` | `admin.roles.view` | Danh sách tất cả permissions |

### Set Role Permissions Body

```json
{
  "permissionIds": ["guid-permission-1", "guid-permission-2"]
}
```

## Files (Bunny CDN)

| Method | Route | Permission | Description |
|--------|-------|------------|-------------|
| GET | `/files` | `seller.files.view` (Admin bypass) | Danh sách file của user |
| POST | `/files/upload` | `seller.files.manage` (Admin bypass) | Upload multipart → Bunny; trả `publicUrl` |
| DELETE | `/files/{id}` | `seller.files.manage` (Admin bypass) | Xóa file + Bunny object |

- `POST /files/upload`: trùng hash với file đang active → trả `isDuplicate`; trùng hash với file đã soft-delete → restore row + re-upload Bunny (tránh lỗi unique `IX_UserFiles_OwnerId_FileHash`).
- `GET /files` / `GET /files/{id}`: nếu `PublicUrl` trống (file upload trước khi có CDN), backend tự build lại từ `bunny.cdn_hostname` + `StoragePath` và backfill DB.

## Permissions

### Admin (admin-web)

- `admin.dashboard.view` — Xem Dashboard tổng quan + menu Thống kê (`/stats/orders`, `/stats/revenue`, `/stats/seller-performance`, `/stats/charts`)
- `admin.shops.view` — Xem tất cả shop
- `admin.shops.manage` — Tạo/sửa/xóa/cấp shop
- `admin.product_opportunities.view` — Xem Product opportunities (mọi shop)
- `admin.users.view` — Xem danh sách người dùng
- `admin.users.manage` — CRUD người dùng, reset password, danh sách seller
- `admin.roles.view` — Xem vai trò và permissions
- `admin.roles.manage` — CRUD vai trò, gán permissions
- `admin.products.view` — Xem scrape / nguồn liên quan listings (permission còn dùng; catalog `/admin/products` đã gỡ)
- `admin.products.manage` — Scrape crawl / product-sources options / thao tác liên quan listings (catalog CRUD đã gỡ)
- `admin.product-sources.view` — Xem nguồn hàng
- `admin.product-sources.manage` — CRUD nguồn hàng
- `admin.categories.view` — Xem danh mục TikTok đã sync
- `admin.categories.manage` — Đồng bộ danh mục từ 4Seller
- `admin.orders.view` — Xem đơn hàng hệ thống
- `admin.orders.manage` — Đồng bộ & quản lý đơn hàng
- `admin.listings.view` — Xem listing hệ thống
- `admin.listings.manage` — Đồng bộ & quản lý listing
- `admin.platforms.view` — Xem nền tảng
- `admin.platforms.manage` — Quản lý nền tảng
- `admin.settings.view` — Xem cấu hình hệ thống
- `admin.settings.manage` — Quản lý cấu hình hệ thống
- `admin.shop_tasks.view` — Xem bảng Nhiệm vụ hệ thống (mọi shop)
- `admin.shop_tasks.manage` — CRUD nhiệm vụ truy cập URL
- `admin.company-ledger.view` — Xem sổ cái thu/chi hệ thống
- `admin.company-ledger.manage` — CRUD sổ cái + sửa giá mua shop

### Seller (seller-web)

- `seller.dashboard.view` — Xem Dashboard Seller + menu Thống kê (`/stats/orders`, `/stats/revenue`, `/stats/seller-performance`)
- `seller.orders.view` — Xem đơn hàng của mình
- `seller.orders.manage` — Sync / mark-ship / sync-track / sync-platform-status / cost / milestones / settlement / notes
- `seller.shops.view` — Xem shop được cấp
- `seller.shops.edit` — Sửa shop được cấp
- `seller.product_opportunities.view` — Xem Product opportunities (shop của mình)
- `seller.products.view` — Xem listings (Draft/Published) + draft extension
- `seller.products.manage` — Sync / edit / push listings + scrape post-html (catalog `/products` đã gỡ)
- `seller.shop_tasks.view` — Xem bảng Nhiệm vụ hệ thống (shop của mình)
- `seller.company-ledger.view` — Xem sổ cái thu/chi đã gán cho mình hoặc shop của mình

## Default Accounts

| Username | Password | Role | Ứng dụng |
|----------|----------|------|----------|
| `admin` | `Admin@123` | Admin | admin-web |
| `seller` | `Seller@123` | Seller | seller-web |

## Lưu ý RBAC

- Permission codes chỉ được định nghĩa qua database seed, không tạo mới qua API.
- Role hệ thống (`Admin`, `Manager`, `Leader`, `Seller`) không thể xóa hoặc đổi tên.
- Sau khi thay đổi role/permission của user, JWT hiện tại không tự cập nhật — user cần đăng xuất và đăng nhập lại (hoặc đợi access token hết hạn rồi refresh). **Đổi/reset mật khẩu** hoặc **vô hiệu hóa tài khoản** thì thu hồi ngay: security stamp đổi + revoke mọi refresh token; request tiếp theo nhận 401. **admin-web** / **seller-web**: gọi `/auth/me` khi tải trang; chỉ `POST /auth/refresh` khi access JWT sắp hết hạn (không refresh mỗi lần mở tab — tránh revoke session tab khác khi Edit listing `_blank`). Kiểm tra quyền portal (`canAccessAdminPortal` / `canAccessSellerPortal`); không đủ quyền → 403 + đăng xuất.
- Role `Admin` luôn vượt qua mọi kiểm tra permission trên API.
