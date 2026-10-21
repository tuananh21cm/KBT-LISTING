# 4Seller TikTok `save-draft` golden body shape

Captured from browser (listing `833495280716`, LOCAL TO LOCAL) on 2026-07-16; sale-prop maps
re-captured from listing `851798330824` on 2026-08-25 (the 07-16 note wrote `option{N}Value` as a
plain map, which is what corrupted every push — see below).

## Critical field types (reject → `code:4100` 业务异常参数错误 if wrong)

| Field | Required shape | Notes |
|-------|----------------|-------|
| `id` | **string** | e.g. `"833495280716"` |
| `isGlobal` | **number** `0` | Not `null` for local shops |
| `shopId` / `currentShopId` | number | |
| `productAttributes` | **JSON string** | Escaped array of `{attributeId, attributeName, values[]}` |
| `brandInfo` | **JSON string** | e.g. `{"id":"0","name":"No brand"}` — must stay in sync with `brandId`/`brandName` when shop/brand changes |
| `option1` / `option2` | **JSON string** của **object** | Attr id→name map, e.g. `{"100000":"Color"}`. Slot order Color→`option1`, Size→`option2`, Number of Items→`option3` |
| `option1Value` / `option2Value` | **JSON string** của **mảng** | Ordered `[{valueId: label}, …]` — **không** phải object. Giá trị TikTok chưa resolve dùng chính nhãn làm key (`{"0XL":"0XL"}`), giống `option{N}` = `{"Color":"Color"}`. Sai container vẫn trả success nhưng trang edit 4Seller quay vòng mãi |
| `skuImgs` | **JSON string** of array | `[{attrName:"Color", img_url:"u1\|u2", custom_value:"Black"}, …]` — **not** `{Black:[url]}` object |
| `productCertifications` | **JSON string** | Often `"[]"` — not used for Product Certification Yes/No |
| `productAttributes` cert Yes/No | inside `productAttributes` JSON string | Fashion: upsert `101619` Dangerous Goods, `101395` Prop 65 Repro, `101400` Prop 65 Carcinogens — `valueId` Yes=`1000058` / No=`1000059` (default **No**) on every save-draft/publish/update |
| `requireMessage` | **string** | Browser gửi `""` — sanitize push điền nếu thiếu |
| `weightUnit` / `dimensionUnit` (root) | **string** | Browser: `POUND` / `INCH` ở root; variant rows giữ `lb` / `inch`. Form local dùng alias ngắn — sanitize map root trước push |
| `packageLength/Width/Height` | **string** | e.g. `"1"`, `"6"`, `"9"` |
| `variation` | array | Singular key only |
| `searchTerms` / `productHighlights` | **JSON array** of strings | e.g. `["soft knit"]` — **not** a JSON string (unlike `productAttributes`). Always written on save-draft/publish/update; created if missing |
| `variation[].option1` | Color label | e.g. `"Black"` |
| `variation[].option2` | Size label | e.g. `"L"` |
| `variation[].mainImage` | **single URL** | Not pipe-joined |
| `variation[].stockInfoList` | `[{warehouse_id, available_stock}]` | `warehouse_id` must match selected shop warehouse after shop switch |
| `variation[].attrs` | `["Black","L"]` | Must equal the non-empty `option1`/`option2`/`option3` labels of the **same row**, in slot order |
| `variation[].keyId` | same as `id` | Dropped together with `id` when a row is reused for a different sale-prop combo (4Seller must create a new SKU, not rename a live one) |
| `variation[].type` | `"auto"` | |

## Sale-prop block is rebuilt, never patched in place

The push body is a clone of the previous 4Seller detail, so anything the edit form removed survives unless it is rebuilt. `ListingSalePropSync` derives the whole block from the live variation set on every push:

- `variation[].attrs` is **always** regenerated from that row's option labels. Reusing a cloned row keeps the previous combo's `attrs`, and a mismatch between `attrs` and `option1`/`option2` is rejected.
- Per-SKU `option{N}` is written **and removed**: dropping the Size axis on the form must clear `option2` on every SKU, not just stop writing it.
- An axis no longer used by any SKU has **both** `option{N}` and `option{N}Value` deleted — leaving an empty value map behind is still a declared dimension to TikTok.
- Existing `valueId`s are kept per label; a brand new label gets its id from `GET /api/meta/tiktok/get-category-attribute-list-by-id`, and falls back to **the label itself as key** (4Seller's own convention for a custom value) when 4Seller cannot be reached. A key that is not numeric is a placeholder, so the schema id wins over it when the schema is loaded — that is exactly what the browser writes back (`Black` → `1001063`, `0XL` stays `0XL`).
- Both root maps are read back in whichever container the payload holds (object or array) and written back in the same one; an unchanged map is left byte-for-byte alone. **Push/preview** snapshots `CaptureValueShapes` from RawPayload **before** the first `RebuildRootMaps` and passes that snapshot through the schema pass so resolving a label-as-key to a TikTok numeric id never converts an array into an object.

Root `warehouseId` is **not** required in the golden body (warehouse lives under `stockInfoList`), but our local push validation still hydrates/requires it for safety. When the admin form changes warehouse (or shop → default warehouse), `ListingSyncService` patches root `warehouseId` and **collapses** every `stockInfoList` to a **single** `{warehouse_id, available_stock}` row (same for `globalStockInfoList` when present). Local `Quantity` is always written to `availableStock` (create-if-missing) before collapse so a raw clone without stock keys does not become `available_stock: 0`. Writing the same qty into every legacy multi-warehouse row used to make inbound `ReadVariationStock` SUM them and inflate stock on each auto-publish/retry. Auto-publish default qty is **45**.

Shop-scoped metadata used by 4Seller listing edit (and now proxied by admin):

| 4Seller path | Purpose |
|--------------|---------|
| `GET /api/meta/tiktok/get-brand-list-by-shop-id` | Authorized brands for shop |
| `GET /api/meta/tiktok/get-brand-list-by-shop-id-brand-suggest?brandName=` | Brand typeahead |
| `GET /api/meta/tiktok/get-warehouse-list-by-shop-id?fbt=0&allocationMode=` | Warehouses + `isDefault` |

## Publish response envelope (HTTP 200)

Browser `POST /api/listing/tiktok/publish` trả nested outcome — **root `code:0` chưa đủ**:

```json
{ "code": 0, "msg": "success", "data": { "code": 0, "listingId": 851868670824, "errorMessage": null } }
```

- **`data.code ≠ 0`** hoặc **`data.errorMessage` non-empty** → push thất bại (dù root OK).
- Referer browser: `https://www.4seller.com/web/listing/tiktok/edit/{id}.html?status=draft`.

Backend `FourSellerListingPushResponseParser` + `EnsureAccepted` map lỗi này thành `BusinessException` — không optimistic Pending / không toast success giả.