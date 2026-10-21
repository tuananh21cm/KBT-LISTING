using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Application.Interfaces;

/// <summary>
/// Application database context abstraction for services.
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>Shops table.</summary>
    DbSet<Shop> Shops { get; }

    /// <summary>Shop operational periods used for seller-performance shop-day exposure.</summary>
    DbSet<ShopOperationalPeriod> ShopOperationalPeriods { get; }

    /// <summary>Platforms table.</summary>
    DbSet<Platform> Platforms { get; }

    /// <summary>Product supply sources table.</summary>
    DbSet<ProductSource> ProductSources { get; }

    /// <summary>Variation spec templates (Color / Size / Number of Items).</summary>
    DbSet<VariationSpecTemplate> VariationSpecTemplates { get; }

    /// <summary>Per-user shipping presets for product forms.</summary>
    DbSet<ShippingTemplate> ShippingTemplates { get; }

    /// <summary>Cached 4Seller platform shipping carriers (TTL refresh from live API).</summary>
    DbSet<PlatformShippingService> PlatformShippingServices { get; }

    /// <summary>Mirrored 4Seller TikTok product-discount activities.</summary>
    DbSet<ProductDiscountActivity> ProductDiscountActivities { get; }

    /// <summary>Per-user pricing formula presets for batch sale-price calculation.</summary>
    DbSet<PricingTemplate> PricingTemplates { get; }

    /// <summary>TikTok category catalog table.</summary>
    DbSet<TikTokCategory> TikTokCategories { get; }

    /// <summary>User files table.</summary>
    DbSet<UserFile> UserFiles { get; }

    /// <summary>System settings table.</summary>
    DbSet<Setting> Settings { get; }

    /// <summary>Scrape submission batches.</summary>
    DbSet<ScrapeBatch> ScrapeBatches { get; }

    /// <summary>URLs inside scrape batches.</summary>
    DbSet<ScrapeBatchUrl> ScrapeBatchUrls { get; }

    /// <summary>Owned 4Seller crawl drafts.</summary>
    DbSet<ScrapeDraft> ScrapeDrafts { get; }

    /// <summary>Synced 4Seller orders.</summary>
    DbSet<Order> Orders { get; }

    /// <summary>PrintPackShip USPS track-scan batches.</summary>
    DbSet<UspsScanBatch> UspsScanBatches { get; }

    /// <summary>Items inside a USPS scan batch.</summary>
    DbSet<UspsScanBatchItem> UspsScanBatchItems { get; }

    /// <summary>Order line items.</summary>
    DbSet<OrderItem> OrderItems { get; }

    /// <summary>Fulfil purchase attempts (per Chrome-profile session history).</summary>
    DbSet<OrderFulfilAttempt> OrderFulfilAttempts { get; }

    /// <summary>Reusable issue-tag library.</summary>
    DbSet<IssueTag> IssueTags { get; }

    /// <summary>Preset shop audience age brackets (Select + marketing copy).</summary>
    DbSet<AgeRange> AgeRanges { get; }

    /// <summary>Canonical TikTok trending keywords (unique by lead_id).</summary>
    DbSet<ProductOpportunity> ProductOpportunities { get; }

    /// <summary>Shop membership for a canonical trending keyword.</summary>
    DbSet<ProductOpportunityShopLink> ProductOpportunityShopLinks { get; }

    /// <summary>Seller-owned saved keywords (survives snapshot replace; later AI search URLs).</summary>
    DbSet<ProductOpportunitySavedKeyword> ProductOpportunitySavedKeywords { get; }

    /// <summary>Persisted «Shop này bán gì» result (one row per shop).</summary>
    DbSet<ShopKeywordResearch> ShopKeywordResearches { get; }

    /// <summary>Order ↔ issue tag assignments.</summary>
    DbSet<OrderIssueTag> OrderIssueTags { get; }

    /// <summary>Mọi lần mua hàng NCC — đã gắn hay chưa gắn đơn sàn, sync hay nhập tay.</summary>
    DbSet<SupplierPurchase> SupplierPurchases { get; }

    /// <summary>Nhật ký từng lô Sync Order SHEIN (giữ 7 ngày).</summary>
    DbSet<SheinSyncRunLog> SheinSyncRunLogs { get; }

    /// <summary>Nhật ký sync extension TikTok Shop (profile / payout / keywords / analytics).</summary>
    DbSet<TikTokShopSyncLog> TikTokShopSyncLogs { get; }

    /// <summary>Violation/warning records từ TikTok Shop Growth Center.</summary>
    DbSet<TikTokShopHealthRecord> TikTokShopHealthRecords { get; }

    /// <summary>Nhiệm vụ truy cập URL hàng ngày do admin cấu hình.</summary>
    DbSet<ShopTaskDefinition> ShopTaskDefinitions { get; }

    /// <summary>Kết quả shop thực hiện nhiệm vụ truy cập URL theo ngày VN.</summary>
    DbSet<ShopTaskCompletion> ShopTaskCompletions { get; }

    /// <summary>Synced 4Seller listings (Draft / Published mirror).</summary>
    DbSet<Listing> Listings { get; }

    /// <summary>Staging origin from extension post-html (applied onto listings after sync).</summary>
    DbSet<ListingAddOrigin> ListingAddOrigins { get; }

    /// <summary>Mốc delta của listing sync theo từng seller/platform/status.</summary>
    DbSet<ListingSyncWatermark> ListingSyncWatermarks { get; }

    /// <summary>Synced listing variations.</summary>
    DbSet<ListingVariation> ListingVariations { get; }

    /// <summary>Auto publish run logs (timeline).</summary>
    DbSet<ListingAutoPublishRun> ListingAutoPublishRuns { get; }

    /// <summary>Daily TikTok Compass product metrics (một dòng / product / ngày VN).</summary>
    DbSet<ListingAnalyticsSnapshot> ListingAnalyticsSnapshots { get; }

    /// <summary>Precomputed delist counters (một dòng / shop / ngày VN snapshot).</summary>
    DbSet<ListingDelistDaySummary> ListingDelistDaySummaries { get; }

    /// <summary>Lịch sử phiên online extension (tab seller-us).</summary>
    DbSet<ShopOnlineSession> ShopOnlineSessions { get; }

    /// <summary>Giá trị attribute TikTok từ chối, học từ chính lỗi publish.</summary>
    DbSet<TikTokAttributeValueRejection> TikTokAttributeValueRejections { get; }

    /// <summary>Supplier shops on source platforms (Shein storeCode + SLA).</summary>
    DbSet<SupplierSellerInfo> SupplierSellerInfos { get; }

    /// <summary>Staged supplier product snapshots (rating / stock) by SourceUrlKey.</summary>
    DbSet<SupplierProductInfo> SupplierProductInfos { get; }

    /// <summary>Fulfil purchase accounts.</summary>
    DbSet<FulfilAccount> FulfilAccounts { get; }

    /// <summary>Fulfil account ↔ user assignees.</summary>
    DbSet<FulfilAccountAssignee> FulfilAccountAssignees { get; }

    /// <summary>Fulfil payment cards.</summary>
    DbSet<FulfilCard> FulfilCards { get; }

    /// <summary>Fulfil account ↔ card links.</summary>
    DbSet<FulfilAccountCard> FulfilAccountCards { get; }

    /// <summary>Fulfil card ↔ user assignees.</summary>
    DbSet<FulfilCardAssignee> FulfilCardAssignees { get; }

    /// <summary>Fulfil transaction ledger.</summary>
    DbSet<FulfilTransaction> FulfilTransactions { get; }

    /// <summary>Receiving bank accounts (thẻ nhận tiền).</summary>
    DbSet<ReceivingBankAccount> ReceivingBankAccounts { get; }

    /// <summary>Receiving bank transaction ledger (tiền về thẻ nhận tiền).</summary>
    DbSet<ReceivingBankTransaction> ReceivingBankTransactions { get; }

    /// <summary>Company operating income/expense ledger (sổ cái thu/chi hệ thống).</summary>
    DbSet<CompanyLedgerEntry> CompanyLedgerEntries { get; }

    /// <summary>Receiving card sources (PingPongX, WorldFirst, …).</summary>
    DbSet<ReceivingCardSource> ReceivingCardSources { get; }

    /// <summary>Provider accounts under a receiving card source.</summary>
    DbSet<ReceivingSourceAccount> ReceivingSourceAccounts { get; }

    /// <summary>Platform users table.</summary>
    DbSet<ApplicationUser> Users { get; }

    /// <summary>Permissions table.</summary>
    DbSet<Permission> Permissions { get; }

    /// <summary>Role-permission mappings.</summary>
    DbSet<RolePermission> RolePermissions { get; }

    /// <summary>Refresh token rotation history (dọn theo retention, không phải dữ liệu nghiệp vụ).</summary>
    DbSet<RefreshToken> RefreshTokens { get; }

    /// <summary>Change tracker for manual DetectChanges / Clear during bulk sync.</summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>Returns the tracking entry for the given entity.</summary>
    EntityEntry Entry(object entity);

    /// <summary>Persists pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
