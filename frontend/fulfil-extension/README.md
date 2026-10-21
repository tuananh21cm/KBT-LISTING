# KBT Fulfil

Extension Chrome/Edge (Manifest V3) cho người đi đơn mua hàng: đăng nhập role **Fulfil** / **Admin**, nhận diện nguồn hàng theo tab (giống Product Scraper), liệt kê đơn `to_ship` chưa mua (`FulfilPurchasedAt` null), mở `SourceUrl`, đánh dấu đã mua / gỡ về chưa mua khi bấm nhầm.

## Build / load

```bash
cd frontend/fulfil-extension
npm install
npm run build      # ⚠ trỏ API production — muốn localhost thì dùng `npm run build:local`
```

Chrome → Extensions → Load unpacked → chọn thư mục `dist/`.

Dev watch: `npm run dev` (sau mỗi rebuild: **Reload** extension trên `chrome://extensions`).

Content scripts được bundle dạng **IIFE** (không code-split). Nếu console hiện `Cannot use import statement outside a module` trên `content.js`, thường do load bản build cũ — chạy lại `npm run build` rồi Reload extension.

API mặc định (production build): `https://4seller-api.kbt.global/api/v1`.

Local (đọc `.env.development` → `http://localhost:5000/api/v1`):

```bash
npm run build:local
# hoặc watch: npm run dev
```

Sau đó **Reload** extension trên `chrome://extensions` (Load unpacked → `dist/`).

## Quyền

- Role `Fulfil` hoặc `Admin`
- Permissions seed: `admin.orders.view`, `admin.orders.manage`, `admin.product-sources.view`
