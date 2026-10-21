# KBT Seller (Browser Extension)

Extension Chrome/Edge (Manifest V3) **KBT Seller**: đăng nhập bằng tài khoản **Seller**, gọi API `https://4seller-api.kbt.global`, xem thông tin shop **TikTok Shop US** từ [seller-us.tiktok.com](https://seller-us.tiktok.com/).

> Giao diện mở dạng **Side Panel** cạnh trang web (bấm icon extension).

## Phase hiện tại

- Đăng nhập kinhbac (JWT) — chỉ **Seller** / Admin / `seller.*`.
- Lấy thông tin shop thật trên tab [seller-us.tiktok.com](https://seller-us.tiktok.com/) với **`credentials: 'include'`** (không nhồi Cookie/X-Bogus từ curl) — request nào trang đã tự gọi trong 10 phút gần đây thì dùng lại payload đó, khỏi gọi lại (xem **Hớt JSON thụ động**):
  - `GET /api/v3/seller/common/get`
  - `GET /passport/account/info/v2/`
  - `GET /api/v1/seller/onboard/v2/config/get`
  - `GET /api/v1/product/product_creation/preload` (`publish_limit`) — namespace `/api/v1/product/*` đi qua **bridge MAIN world** (xem mục dưới), gateway trả `code=10000` rỗng nếu request không được trang ký
  - **`POST /api/v1/pay/onboard_info/get`** `{user_type:1,biz_type:1}` → company, địa chỉ, owner, email không mask
- Nút **Info** / mở seller-us / auto khi load trang seller-us (lần đầu chưa có shopInfo vẫn tự fetch).
- Icon View header: checklist header bắt qua `webRequest` + `shopOk` khi GET thành công.
- Sync profile lên Kinhbac: `PUT /shops/tiktok-profile` — **Info** PUT ngay; auto-capture debounce ~8s. Khớp `sellerId` → `Shop.ExternalShopId`; không tạo shop mới.
- Settlement bank: `GET /api/v1/seller/settlement/account/get` → hiển thị **Bank** (masked) + sync `SettlementBankAccountNumber` (không đè `NewBankNumber` thẻ nhận tiền).
- Finance On hold: `GET /api/v1/pay/statement/stat/info` → 4 số `onHold*` + `deli` (`bill_finish_period_in_days`) sync cùng `PUT /shops/tiktok-profile`.

## Bridge MAIN world cho `/api/v1/product/*`

`public/sellerProductBridge.js` chạy `document_start` ở world **MAIN**. Gateway product của TikTok đòi CSRF thật cùng `X-Bogus` / `_signature` / `msToken` mà `webmssdk` sinh **theo từng URL** — isolated world không sinh được, nên không bịa header nữa mà để trang tự ký. Thang ưu tiên khi cần số:

1. **`page-harvest`** — hớt luôn response của chính trang (mục dưới). Không tốn request nào. Extension **không** tự mở trang để tạo tình huống đó.
2. **`bridge`** — gọi qua `window.fetch` của page (đọc lại tại thời điểm gọi để lấy lớp bọc ngoài cùng), header lấy từ template request thật của trang. Content ping MAIN 500ms trước; im lặng thì bỏ chặng này.
3. **`isolated`** — fetch từ content script như cũ: `GET+csrf` → `GET+shop_region` → `POST{}`.

Chính sách header: chỉ tái dùng header **theo phiên** (`x-secsdk-csrf-token`, `x-tt-*`, `content-type`); loại chữ ký một-lần (`x-bogus`, `x-gorgon`, `x-argus`, `x-ladon`, `_signature`…) và nhóm browser tự quản (`cookie`, `origin`, `referer`, `sec-*`, `user-agent`); URL tái dùng bị xóa `X-Bogus` / `_signature` / `msToken`. **Không replay chữ ký** vì nó gắn với đúng một URL — bê sang URL khác là tự khai sai phiên, đúng thứ risk-gate bắt được.

Request do extension tự gọi được đánh dấu ở `window.__kbTtsForgedUrls`: `pageIdleHook.js` không đếm nó vào inflight, bridge không lấy nó làm template (header trơ sẽ ghi đè token phiên tốt) và không hớt lại response của nó như số của trang.

Message bước Info ghi rõ nguồn — `· Listing 12/1000 (page-harvest) · harvest 6/8` — hoặc liệt kê từng chặng đã trượt.

## Hớt JSON thụ động (10 endpoint không tham số)

Khi bạn đi lại trong Seller Center, bridge lưu response của các endpoint **không tham số** vào `sessionStorage['kb_tts_harvest']` (`{ raw, at, sellerId }`; cap ~400KB/entry, ~2MB tổng): `seller/common/get`, `passport/account/info`, `onboard config`, `product_creation/preload`, `pay/onboard_info`, `settlement/account/get`, `statement/stat/info`, `products/list`, `ahr/score`, `experience-score`.

Bước **Info** và **Shop Score** hỏi store một lần cho cả lượt (`kb-tts-harvest-get`) và dùng luôn entry **≤10 phút** + đúng `sellerId` + qua `isUsable` thay vì tự gọi request. `isUsable` không phải trang trí: `products/list` chỉ nhận `tab_id=19` (tab khác không mang `suspend_reason` nên suy ra sai thành "không bị khóa"), `statement/stat/info` chỉ nhận `amount_stat_type=1` (loại khác thì "to settle" mang nghĩa khác), `ahr/score` chỉ nhận `account_type=2&account_level=1`, `experience-score` chỉ nhận `shop_id` rỗng, `pay/onboard_info` chỉ nhận body `{user_type:1,biz_type:1}`. Payload lỗi (`code≠0`, thiếu `data`) giữ nguyên entry cũ.

Nhóm endpoint **có tham số** (payout list, health records, Compass analytics, `lead/list`) không hớt: payload phụ thuộc trang / khoảng ngày / category nên phải khớp fingerprint mới dùng lại được.

**Không cần F5 sau khi reload extension:** background ping `PING_PRODUCT_BRIDGE`, thiếu hook thì `chrome.scripting.executeScript` world `MAIN` (`pageIdleHook.js` + `sellerProductBridge.js`), và inject sẵn cho tab seller-us đang mở khi `onInstalled`/`onStartup`. Giới hạn: script inject muộn chỉ hook được request **sau** thời điểm inject — request trang đã gửi trước đó thì mất, nên thang `bridge` → `isolated` vẫn giữ làm đường lùi.

## Build & Load

```bash
npm install
npm run build        # API production
npm run build:local  # API localhost:5000
npm run dev          # watch + env local
```

Load unpacked: `chrome://extensions` → Developer mode → **Load unpacked** → `frontend/tiktok-shop-extension/dist`.
