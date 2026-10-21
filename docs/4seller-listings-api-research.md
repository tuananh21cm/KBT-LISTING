# 4Seller Listings / Product API Research

> Nguồn: reverse-engineer JS bundle 4Seller (`tiktok-nCpMIcZ3.js`) + tích hợp hiện tại.
> Base URL: `https://www.4seller.com/api`

## API đã tích hợp (shop)

| Method | Path | Mô tả |
|--------|------|-------|
| POST | `/api/auth/redirect/url` | OAuth redirect URL |
| POST | `/api/shop/get-list` | Danh sách shop đã kết nối |

## Crawl / Product Scrape (đã tích hợp)

Referer UI: `/web/tools-v2/scarape/history.html`. Proxy admin: `/api/v1/admin/fourseller/crawl/...`.
Crawl HTTP headers (code hiện tại): `Accept`, `Accept-Language`, `Origin`, `Referer`, `User-Agent`, `Cookie`, `Content-Type` (POST). `HttpClientHandler.UseCookies = false` — cookie chỉ từ header thủ công.

| Method | Path | Mô tả |
|--------|------|-------|
| POST | `/api/crawl/get-crawl-url-repeat` | Body `string[]` URL. **HTTP 200 + code:0** bắt buộc (≠200 → lỗi thoát). `data: []` = **accept vào pool** (app phải poll `get-move-draft-page`, không throw); `data: [string urls]` = confirm scrape lại; `data: [{id, sourceUrl…}]` = draft đã có trên cookie user → claim local. Cookie hết hạn = `code:4001`, không phải `data:[]`. Draft pool đôi khi chậm 30–120s hoặc không materialize dù UI 4Seller scrapes OK (extension / bulk UI). |
| POST | `/api/crawl/get-move-draft-page` | Body `{ pageCurrent, pageSize, searchValue }` → `records[]` draft |
| POST | `/api/crawl/save-to-platform` | Body `{ moveDraftId: long[], shopIdList: long[] }` → `data` = `scheduleCode` string |
| GET | `/api/crawl/get-schedule?scheduleCode=` | Progress `{ total, succeed, failed, failedList }` |
| POST | `/api/crawl/delete-draft` | Body `long[]` move-draft ids — xóa 1/nhiều draft |

`shopIdList` = 4Seller internal shop id (`record.Id`). Envelope crawl dùng `msg` (không phải `messages`).

Ví dụ draft record:

```json
{
  "id": 70847980711,
  "status": 0,
  "image": "http://img.ltwebstatic.com/...",
  "sourceUrl": "https://us.shein.com/...",
  "variantCount": 12,
  "lowPrice": 11.11,
  "highPrice": 17.89,
  "currency": "USD",
  "creator": "KBT Seller",
  "createTime": "2026-07-11 07:44:13",
  "productName": "ROMWE Grunge Punk ..."
}
```

## TikTok Listing — endpoints chính

| Method | Path | Mô tả |
|--------|------|-------|
| POST | `/api/listing/tiktok/page` | Danh sách listing (Draft / Published / …) |
| POST | `/api/listing/tiktok/detail` | Chi tiết 1 listing (form POST) |
| POST | `/api/listing/tiktok/batch-detail` | Chi tiết nhiều listing |
| POST | `/api/listing/tiktok/save-draft` | Lưu draft |
| POST | `/api/listing/tiktok/publish` | Publish listing |
| POST | `/api/listing/tiktok/update` | Cập nhật listing |
| POST | `/api/listing/tiktok/delete` | Xóa listing |
| GET | `/api/listing/tiktok/get-listing-num` | Đếm listing theo trạng thái |
| POST | `/api/listing/tiktok/get-status-count` | Thống kê trạng thái |
| POST | `/api/listing/tiktok/batch-save-draft` | Lưu draft hàng loạt |
| POST | `/api/listing/tiktok/batch-publish` | Publish hàng loạt |

Danh sách đầy đủ: `docs/4seller-tiktok-listing-apis.txt`

## API phụ trợ (template, meta, image)

| Method | Path | Ghi chú |
|--------|------|---------|
| POST | `/api/product-template/page` | Template sản phẩm |
| POST | `/api/product-template/save` | Lưu template |
| GET | `/api/product-template/detail` | Chi tiết template |
| POST | `/api/product-rule/page` | Rule sản phẩm |
| POST | `/api/listing/log` | Log publish |
| POST | `/api/listing/tiktok/check-ref-category` | Kiểm tra category TikTok |
| POST | `/api/meta/tiktok/get-category-list` | Danh mục TikTok (lazy theo `parentId`) — **đã tích hợp sync** |
| GET | `/api/meta/tiktok/get-category-by-id` | Chi tiết category |

### `get-category-list` (đã tích hợp)

Request:

```json
{ "site": "US", "parentId": 0, "shopId": 226280 }
{ "site": "US", "parentId": "877320", "shopId": 226280 }
```

- `shopId`: **id nội bộ 4Seller** từ `/api/shop/get-list` (`record.Id`, vd `226280`) — **không** dùng `sellingPartnerId`. Backend tự chọn shop Connected TikTok theo `MarketplaceId` = site
- `parentId`: `0` (number) = root; sau đó dùng `categoryId` string của node cha (vd `"605196"`)
- Response envelope: `{ code: 0, success: true, messages: "SUCCESS", data: [...] }`
- Node fields thật:

| Field | Ý nghĩa |
|-------|---------|
| `categoryId` | ID TikTok → local `ExternalId` |
| `categoryName` | Tên |
| `categoryParentId` | Parent TikTok ID |
| `leafCategory` | `0` = có con, `1` = leaf |
| `nodePathId` | Path ID (`605196/809488`) |
| `nodePath` | Path tên |
| `id` | ID nội bộ 4Seller row — **không** dùng làm category |

- Local: bảng `TikTokCategories` (catalog chuẩn theo `Site`); sync BFS qua `CategoryService` + `POST /api/v1/admin/categories/sync` body `{ "site": "US" }`

## URL pattern UI

- Danh sách: `/web/listing/...`
- Sửa draft TikTok: `/web/listing/tiktok/edit/{listingId}.html?status=draft`

## Mapping sang DB local

Migration: `AddTikTokListingDetailTables` — mở rộng `ShopProducts` + 4 bảng con.

### Quan hệ bảng

```
ProductSources
  └── Products (source catalog — ExternalProductId theo nguồn)
        └── ShopProducts (TikTok listing trên 1 shop)
              ├── ListingSkus (Variations)
              ├── ListingImages (Photos & Video)
              ├── ListingAttributes (Specifics)
              └── ListingCertifications (Certification)
```

### Section UI 4Seller → Bảng DB

| Section UI (`#id`) | Bảng / cột |
|------------|------------|
| Header (Save / Publish / Draft) | `ShopProducts.ListingStatus`, `PublishStatus`, `ListedAt` |
| Shop Reference listing | `ShopProducts.RefListingId`, `RefShopId`, `SiteId` |
| Shop Product Info `#productInfo` | `ShopProducts.ProductName`, `CategoryId`, `CategoryName`, `BrandId`, `BrandName`, `ProductType` |
| Variations `#variations` | `ListingSkus` |
| Photos & Video `#photosAndVideo` | `ListingImages` |
| Specifics `#specifics` | `ListingAttributes` |
| Description `#description` | `ShopProducts.DescriptionHtml` |
| Shipping `#shipping` | `ShopProducts.WarehouseId`, `PackageWeight/Length/Width/Height`, `WeightUnit`, `DimensionUnit`, `SizeChartId`, `SafetyWarning` |
| Certification `#certification` | `ListingCertifications` |
| 4Seller set `#sellerSet` | `ListingSourceUrl`, `FourSellerGroupId`, `FourSellerGroupName`, `AppliedRules` |
| Raw API snapshot | `ShopProducts.FourSellerRawPayload` (jsonb) |

Admin form: `/products/listings/:listingId/edit` — Collapse theo thứ tự trên; Save → Draft, Save & Publish → Published (local; chưa gọi 4Seller API).

### `Product` (source catalog)

| 4Seller / source field | Local column | Ghi chú |
|-------------------------|--------------|---------|
| source product id | `ExternalProductId` (+ `SourceId` → `ProductSources`) | Unique theo owner+source |
| source SKU | `ExternalSku` | optional |
| `title` | `Title` | |
| `description` | `Description` | |
| source link | `SourceUrl` | catalog; listing dùng `ListingSourceUrl` |
| `status` | `Status` | Draft/Active/Archived |
| (raw JSON) | `Metadata` | Lưu fields chưa map |

### `ShopProducts` (listing chính — đã mở rộng)

| 4Seller field (dự kiến) | Local column | Ghi chú |
|-------------------------|--------------|---------|
| `listingId` / `id` | `FourSellerListingId` | ID listing trên 4Seller |
| `productId` (TikTok) | `TikTokProductId` | |
| `skuId` | `TikTokSkuId` | SKU mặc định |
| `status` | `ListingStatus` | Draft/Published/Removed/Syncing |
| `publishStatus` | `PublishStatus` | UnderReview/Failed/… |
| `publishTime` | `ListedAt` | |
| `updateTime` | `LastSyncedAt` | |
| — | `SyncStatus` | Pending/Synced/Error (nội bộ) |
| — | `SyncError` | Lỗi sync gần nhất |
| `sourceListingId` | `RefListingId` | Shop reference |
| `refShopId` | `RefShopId` | |
| `siteId` | `SiteId` | US/EU/… |
| `productName` | `ProductName` | |
| `categoryId` | `CategoryId` | |
| `categoryName` | `CategoryName` | |
| `brandId` | `BrandId` | |
| `brandName` | `BrandName` | |
| `productType` | `ProductType` | SingleSku/MultiSku |
| `description` | `DescriptionHtml` | HTML |
| `warehouseId` | `WarehouseId` | |
| `packageWeight` … | `PackageWeight/Length/Width/Height` | |
| `sizeChartId` | `SizeChartId` | |
| `safetyWarning` | `SafetyWarning` | |
| Source URL (4Seller set) | `ListingSourceUrl` | max 500 |
| Group | `FourSellerGroupId`, `FourSellerGroupName` | |
| Applied rules | `AppliedRules` | jsonb |
| (app flags) | `Metadata` | jsonb |
| (full detail response) | `FourSellerRawPayload` | jsonb |

### `ListingSkus` (Variations)

| 4Seller field | Local column |
|---------------|--------------|
| `skuId` | `FourSellerSkuId` / `TikTokSkuId` |
| `sellerSku` | `SellerSku` |
| `price` | `SalePrice` |
| `originalPrice` | `OriginalPrice` |
| `quantity` / `inventory` | `Quantity` |
| `attributes` | `VariantAttributes` (jsonb) |
| per-SKU package | `PackageWeight/Length/Width/Height` |

### `ListingImages` (Photos & Video)

| 4Seller field | Local column |
|---------------|--------------|
| `url` | `Url` |
| `type` | `MediaType` — Main/Detail/Video/SizeChart/Certification |
| `imageId` | `TikTokImageId` |
| `sortOrder` | `SortOrder` |

### `ListingAttributes` (Specifics)

| 4Seller field | Local column |
|---------------|--------------|
| `attrId` | `AttrId` |
| `attrName` | `AttrName` |
| `value` | `AttrValue` |
| `valueType` | `ValueType` |

### `ListingCertifications` (Certification)

| 4Seller field | Local column |
|---------------|--------------|
| `type` | `CertType` — Manufacturer/ResponsiblePerson/ProductCert/Compliance |
| `id` | `ExternalId` |
| `name`, `phone`, `email`, `address` | `Name`, `Phone`, `Email`, `Address` |
| `fileUrls` | `FileUrls` (jsonb) |

### Endpoint → DB write map

| Endpoint 4Seller | Ghi vào |
|------------------|---------|
| `POST /listing/tiktok/page` | Browse only (seller proxy); UI `/listings` |
| `POST /listing/tiktok/batch-detail` (+ fallback `detail`) | Merge → catalog `Products` + `ProductImages` + `ProductVariants` (import; chưa `ShopProducts`) |
| `POST /listing/tiktok/detail` | (tương lai) Parse → `ShopProducts` + child tables; lưu raw → `FourSellerRawPayload` |
| `POST /listing/tiktok/save-draft` | Build body từ `ShopProducts` + child tables |
| `POST /listing/tiktok/publish` | Cập nhật `PublishStatus`, `ListedAt`, `TikTokProductId` |
| `POST /listing/tiktok/batch-update-price-v2` | `ListingSkus.SalePrice`, `OriginalPrice` |
| `POST /listing/tiktok/batch-update-quantity` | `ListingSkus.Quantity` |
| `POST /listing/tiktok/batch-update-listing-attr` | `ListingAttributes` |
| `POST /listing/tiktok/batch-save-description` | `ShopProducts.DescriptionHtml` |
| `POST /listing/tiktok/batch-update-listing-certification-arr` | `ListingCertifications` |
| `POST /listing/tiktok/batch-update-size-weight` | Package fields |
| `POST /listing/tiktok/batch-update-size-charts` | `SizeChartId` + `ListingImages` type `SizeChart` |

### Seller proxy (đã implement)

| App route | 4Seller | Ghi chú |
|-----------|---------|---------|
| `POST /api/v1/fourseller/listings/{platform}/page` | `/api/listing/{platform}/page` | Cookie JWT user; `{platform}` phải là `Platforms.Code` active (lowercase) |
| `POST /api/v1/fourseller/listings/{platform}/import` | page snapshot + `batch-detail`/`detail` | → `Products` catalog; skip nếu trùng ExternalProductId |
| `GET /api/v1/platforms/options` | — | Seller dropdown nền tảng active |

### Constants (`DropshipConstants.cs`)

- `ListingStatuses` — Draft, Published, Removed, Syncing
- `PublishStatuses` — Draft, UnderReview, Published, Failed, Deactivated
- `SyncStatuses` — Pending, Synced, Error
- `ListingMediaTypes` — Main, Detail, Video, SizeChart, Certification
- `ListingCertTypes` — Manufacturer, ResponsiblePerson, ProductCert, Compliance
- `ListingProductTypes` — SingleSku, MultiSku

## Orders (đã tích hợp)

| Method | Path | Mô tả |
|--------|------|-------|
| POST | `/api/v2/order/page` | Danh sách đơn theo `orderStatus` (`pending`, `on_hold`, `to_ship`, `in_process`, `shipped`, `canceled`), paging `pageCurrent`/`pageSize` |

Referer UI: `/web/order2.html?type=pending|onHold|toShip|inProcess|shipped|canceled`. Proxy sync: `POST /api/v1/admin/orders/sync` — lặp cookie từng seller, upsert local `Orders` theo `orderId`. Headers như listing/crawl (Cookie + browser-like); chưa dùng `x-4seller-sign`.
