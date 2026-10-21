# SHEIN — cơ chế ký request & cách extension lấy dữ liệu

Tài liệu tra cứu cho lúc bắt request SHEIN (DevTools / HAR). Mục tiêu: biết **soi cái gì**,
**so với cái gì**, và **đâu là chỗ đã biết chắc / đâu còn phải xác minh**.

Code liên quan:

| Vai trò | File |
| --- | --- |
| Hook MAIN-world: ký + hớt JSON + forge | `frontend/browser-extension/public/sheinReviewCapture.js` |
| Content script: cầu nối, gọi BFF, bấm phân trang | `frontend/browser-extension/src/content/index.ts` |
| Dựng URL pdsearch / PDP | `frontend/browser-extension/src/shared/sheinSearchUrl.ts` |
| Dựng request BFF (replay) | `frontend/browser-extension/src/shared/sheinSearchApi.ts` |
| Điều phối phân trang + nhịp chống risk | `frontend/browser-extension/src/background/productSearch.ts` |
| Parse JSON list / PDP | `sheinReviewCapture.js` (`slimProduct`, `parseRealtimeData`) |

---

## 1. Hai endpoint đang dùng

| Endpoint | Dùng để | Method |
| --- | --- | --- |
| `/bff-api/product/get_products_by_keywords` | List kết quả tìm kiếm (pool sản phẩm) | **POST** mặc định; `page` nằm trong JSON body (có thể lồng `queryObj.page`), không phải luôn ở query |
| `/bff-api/…/get_goods_detail_realtime_data` | Metric PDP: rating, stock, seller, SLA, Local, QuickShip | Theo SPA |

Nhận diện trong code: `isSearchUrl()` = regex `get_products_by_keywords`, `isPdpUrl()` =
`get_goods_detail_realtime_data`. Đổi tên endpoint là hai regex này chết trước tiên → khi
bắt request lần sau, **việc đầu tiên là đối chiếu path còn đúng không**.

---

## 2. Cơ chế ký — cái gì gắn thời điểm, cái gì không

### 2.1 Header SHEIN tự sinh (hớt được, KHÔNG replay lại được)

Allowlist đang hớt (`HEADER_ALLOWLIST` trong `sheinReviewCapture.js`), cộng mọi header
tiền tố `x-` hoặc `_`:

```
armortoken   x-csrf-token   smdeviceid   x-gw-auth   x-cs-random
x-oest       x-ad-flag      webversion   x-requested-with
content-type _ver           _lang        authorization
anti-in      blackbox       uat
```

Nhóm anti-bot thật sự là **`armorToken`, `x-gw-auth`, `blackbox`, `smdeviceid`**. Chúng
**gắn thời điểm và gắn nội dung request**, nên:

- Replay lại token cũ với query đã viết lại → **403 / risk**. Đây là lý do forge chỉ còn là
  phương án cuối, và là lý do phải hớt header **sau** mỗi lần điều hướng chứ không dùng
  snapshot của document cũ (xem `pullSearchSession`: content còn sống mà trả session cũ thì
  bỏ, không lấy `latestSession` của service worker).
- **Chưa xác minh:** thuật toán và input của `armorToken` / `x-gw-auth` (URL? body? timestamp?
  device id?). Muốn biết thì bắt 2 request cùng keyword khác `page` rồi diff.

### 2.2 Header extension KHÔNG được gửi

Denylist (`HEADER_DENYLIST` phía MAIN, `SHEIN_SEARCH_HEADER_DENY` phía content):

```
host  origin  referer  connection  content-length  cookie  user-agent
accept-encoding  accept-language  sec-fetch-*  sec-ch-ua*  priority
cache-control  pragma
```

Nguyên tắc: **header lạ là dấu hiệu bot**. Request forge chỉ gửi đúng 3 cái
`accept` / `content-type` / `x-requested-with` rồi để tầng của SHEIN tự ký. Cookie luôn lấy
từ tab qua `credentials: 'include'`, **không bao giờ copy `Cookie` / `Referer` bằng tay**.

### 2.3 Tham số URL — chỗ gây risk nặng nhất

`SHEIN_SESSION_SCOPED_PARAMS` (trong `sheinSearchUrl.ts`) — **tuyệt đối không tự sinh**:

| Param | Là gì |
| --- | --- |
| `src_tab_page_id=page_search{ts}` | id một lượt render list, phải do SPA phát ra trước đó |
| `src_identifier` | vị trí click **trong đúng lượt xem list đó** — dạng `st=2` / `sc={kw}` / `sr=0` / `ps=1`, phân tách bằng dấu backtick |
| `src_identifier_pre_search` | breadcrumb category của phiên |

SHEIN **đối chiếu** chúng với log page-view của chính phiên đang mở. URL mình tự dựng luôn
khai một lượt xem chưa từng xảy ra → `/risk/action/limit` **ngay lần đầu**, dù header và bộ
lọc đều đúng.

**Kiểm chứng đã làm (2026-08-20):** copy URL từ nút Trending keyword, dán thanh địa chỉ → risk
ngay. Xóa **đúng 2** tham số `src_identifier` + `src_tab_page_id` → vào bình thường.

Ngược lại **giữ được** vì không mang id/timestamp nào để soi lại:
`ici`, `src_module`, `source`, `search_source`, `search_type`, `sourceStatus`, `force_suggest`.

Bộ lọc `min_price` / `max_price` / `price_input` / `attr_ids` / `attr_node_ids` / `attr_values` /
`tag_ids` / `sort` gắn được, **nhưng chỉ khi có giá trị thật** — gắn param rỗng cũng là chữ ký lạ.

---

## 3. Cách lấy dữ liệu — ưu tiên "để SHEIN tự gọi"

Nguyên tắc xuyên suốt: **request nào SPA tự phát thì không bao giờ 403**, vì chính SHEIN ký nó.
Extension chỉ *hứng* JSON. Chuỗi 4 tầng, dừng ở tầng nào ăn tầng đó:

1. **Capture của chính SPA** — hook đọc response, khớp cả `page` **và** `keywords` chuẩn hóa.
   Trang 1 gần như luôn lấy từ warmup.
2. **Bấm thanh phân trang của SPA** (trang 2+) — `SHEIN_SEARCH_GOTO_PAGE`. Nội dung đổi tại chỗ,
   **không** load lại document, giữ nguyên ngữ cảnh phiên. Nhanh và sạch nhất.
3. **Điều hướng URL `pdsearch?page=N`** (`chrome.tabs.update`) — SPA tự fetch + tự ký. Tốn một
   lần load document thật (~3–6s).
4. **Forge XHR MAIN-world** — *phương án cuối*. `window.fetch` với overlay `conservative`
   (chỉ đổi `keywords` + `page`, giữ mọi param SPA đã ký, có gỡ nhóm session-scoped ở §2.3).

> **Chưa xác minh, cần soi lần sau:** tầng 4 chỉ được ký **nếu SHEIN wrap chính `window.fetch`**.
> Nếu họ ký trong tầng axios/XHR riêng thì request forge ra đi **không có `armorToken`** — và một
> request sai chữ ký đủ để SHEIN gắn cờ cả phiên, khiến SPA sau đó tự lăn vào `/risk/`.
> **Cách kiểm:** chạy tới lúc forge, xem request `get_products_by_keywords` do extension bắn
> trong tab Network có `armorToken` / `x-gw-auth` không. **Không có → bỏ hẳn tầng 4.**

### Bẫy đã sập một lần

Request forge đi qua **chính hook `window.fetch`** của mình, nên `saveSession` từng ghi đè
`kbt_shein_search_session` bằng đúng 3 header trơ, mất sạch `armorToken` đã hớt của SPA → mọi
replay sau đó chắc chắn 403 dù SPA vẫn còn phiên tốt. Đã chặn bằng cờ `init.__kbtForged`.
**Thêm bất kỳ request tự phát nào cũng phải gắn cờ này.**

### Nhịp chống risk (3 tầng)

| Tầng | Giá trị |
| --- | --- |
| Giữa 2 trang thành công | `2600ms + 500ms×độ_sâu + jitter 0–2400ms` |
| Sau khi hớt JSON (SPA còn paint) | `PAGE_SETTLE_MS = 1200ms` |
| **Trước mỗi bước leo thang trong cùng 1 trang** | `1800ms + jitter 0–1200ms` |

Tầng thứ ba quan trọng nhất: thiếu nó thì một trang xử lý trượt bắn 2–3 request BFF cách nhau
dưới một giây, đúng lúc SHEIN vừa thấy request trước bất thường. Jitter để rộng gần bằng base —
nhịp đều tăm tắp là tín hiệu bot rẻ nhất.

---

## 4. Đường đi của dữ liệu đã hớt

```
SPA fetch/XHR
  └─ hook MAIN-world (sheinReviewCapture.js)
       ├─ sessionStorage: kbt_shein_search_session   (templateUrl + method + headers + body)
       ├─ sessionStorage: kbt_shein_search_cache     (JSON list đã slim)
       ├─ sessionStorage: kbt_shein_pdp_cache        (map theo goodsId, cap 24)
       └─ postMessage → content script
            ├─ kbt-shein-search-session  → chrome.runtime: SHEIN_SEARCH_SESSION
            ├─ kbt-shein-search-capture  → chrome.runtime: SHEIN_SEARCH_CAPTURE
            └─ kbt-shein-pdp-capture
```

Background kéo chủ động khi cần: `GET_SHEIN_SEARCH_SESSION`, `GET_SHEIN_SEARCH_CAPTURE`.
Dọn giữa hai lượt tìm: `RESET_SHEIN_SEARCH_SESSION` — **chỉ gỡ đúng 2 key của mình**, không
`sessionStorage.clear()` (xoá cache state của chính SHEIN, buộc SPA re-init mỗi lượt, vừa chậm
vừa đáng ngờ).

Forge đi hai chiều qua `window.postMessage`: `kbt-shein-search-fetch` →
`kbt-shein-search-fetch-result`.

---

## 5. Map field JSON → model (kiểm khi SHEIN đổi schema)

### List (`get_products_by_keywords`)

Mảng sản phẩm dò lần lượt: `info.products` → `info.goods` → `info.list` → `info.productList`
→ `json.products` → `json.goods`. Tổng: `info.num` → `info.total` → `info.totalCount`.
Trang: `pageFromJson` → `pageFromUrl` → `pageFromBody` (POST).

| Model | Nguồn |
| --- | --- |
| `goodsId` | `goods_id` / `goodsId` |
| `goodsUrlName` | `goods_url_name` — **slug PDP lấy từ đây, không slugify từ tên** |
| `price` | `salePrice.usdAmount` → `.amount` → `salePrice` → `price` |
| `soldCount` | `salesLabel.labelLang` kiểu `300+ sold` → 300 (còn `sales_label`, `salesVolume`, `soldNum`, `unit_sell`…) |
| `rating` / `reviewCount` | `comment_rank_average` / `comment_num` |
| `isLocal` | `productMaterial.titleLabelList[].labelType === "local_stock"` |
| `isQuickShip` | `isSupportQuickShip` / `quickship` |
| `isSheinClub` | `sheinClub` / `premiumFlag` |
| `variantCount` | `relatedColor[]` → `relatedColorCount` / `skuCount` |
| facet style | mọi `attr_id` khớp `^\d+_\d+$` (vd. `101_167` = Casual) → tự học vào `kbt_shein_style_attrs` |

### PDP (`get_goods_detail_realtime_data`)

| Model | Nguồn |
| --- | --- |
| `reviewCount` / `averageRating` | `info.comment.comments_overview.comment_num` / `.comment_rank_average` |
| `stock` | `info.productInfo.stock` |
| seller | `info.storeInfo.sellerInfo` (+ `modeChangeSellerInfo` ưu tiên) → `storeCode`/`storeTitle`/`soldByTips`/`shopRank`/`storeAttention` |
| SLA | `info.shipInfo` → `dayPercentDesc` + `dayPercents` (vd. `88% ≤ 7`). **Không** dùng `shippingDayPercentDesc` — đó là số toàn sàn (~80.34%), giống nhau mọi shop |
| `isLocalProduct` | `shipInfo.localMadeTips === "Local"` → fallback `productInfo.isLocalProduct` |
| `isSupportQuickShip` | OR mọi `sku_list[].mall_stock[].isSupportQuickShip` (ưu tiên `mall_code` khớp `selectedMallCode`); **không** dùng `sku.isSupportQuickShip` |

---

## 6. Nhận biết bị chặn

| Tín hiệu | Code |
| --- | --- |
| URL tab có `/risk/` trong path | `isSheinRiskUrl` |
| Payload chứa `/risk/`, `captcha`, `action/limit` (ghép `code`+`msg`+`tips`+`info`) | `isSheinSearchRiskPayload` |
| HTTP 401/403, hoặc msg có `TokenIllegal`/`captcha`/`blocked` | `isSheinSearchAuthAbort` |
| `code` ≠ `0`/`200`/`success` **và** products rỗng **và** msg có risk/login/illegal | `interpretSheinSearchHttp` |

Bị risk → **giữ pool đã gom**, `status=done` kèm `Dừng sớm: …`; chỉ `status=error` khi pool rỗng.

---

## 7. Checklist khi bắt request SHEIN lần sau

Bắt **2 request để diff**: một do **bấm tay trên UI**, một do **extension** chạy cùng keyword.

1. **Path** — còn `get_products_by_keywords` không? (§1)
2. **Method + `page` nằm ở đâu** — query hay JSON body / `queryObj.page`? (§1)
3. **Query params** — bản bấm tay có `src_identifier` / `src_tab_page_id` không? Bản extension
   **phải không có**. Có thêm param mới nào chứa id/timestamp thì bổ sung vào
   `SHEIN_SESSION_SCOPED_PARAMS`. (§2.3)
4. **Request headers** — liệt kê nhóm anti-bot thực tế, so với `HEADER_ALLOWLIST`. Xuất hiện tên
   mới (kiểu `x-…`) thì hook đã tự bắt theo tiền tố `x-`, nhưng nên ghi tên vào allowlist cho rõ.
5. **Request forge có được ký không** — câu hỏi quan trọng nhất còn treo ở §3.
6. **Response** — `info.products` còn đúng chỗ? `info.num` còn là tổng? `salesLabel` / `titleLabelList`
   còn nguyên tên? (§5)
7. **`limit`/trang** — SPA đang xin bao nhiêu SP mỗi trang (quan sát ~118). Ảnh hưởng trực tiếp
   `MAX_SEARCH_PAGES = 4` và ngưỡng `MIN_SEARCH_POOL = 200` của cache pool.

Lưu HAR vào chỗ **không commit** (`*.local`), vì nó chứa cookie đăng nhập SHEIN.

---

## 8. Việc còn nợ

1. **Cache pool bị "đầu độc" khi risk giữa đường** — `searchPoolCacheUsable` coi pool dùng được khi
   `products >= max(200, limit×6)`. Lượt bị cắt ở trang 2 với 237 SP → lần Tìm lại bỏ qua phân
   trang hoàn toàn, dùng lại đúng 237 SP trong 6 giờ thay vì gom đủ ~475. Cần cờ "bị cắt".
2. **Xác minh chữ ký nhánh forge** (§3) → nếu không ký, bỏ hẳn tầng 4.
3. **Cooldown sau risk** — hiện bấm Tìm lại là điều hướng ngay trong khi phiên đang bị gắn cờ.
4. **Dead code**: `replayRequest` luôn `conservative: true`, nên toàn bộ nhánh không-conservative
   của `buildSheinSearchApiUrl` (+ `BODY_OVERLAY_KEYS`, `SHEIN_SEARCH_PAGE_LIMIT`) không còn chạy.
   Đọc code dễ tưởng bộ lọc được gắn vào BFF.
