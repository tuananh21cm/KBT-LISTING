using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Application.Interfaces;
using Application.Services;

namespace Infrastructure.Persistence;

/// <summary>
/// Application database context with Identity.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
{
    /// <summary>
    /// Initializes the database context.
    /// </summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Permissions table.
    /// </summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>
    /// Role-permission mappings.
    /// </summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>
    /// Refresh tokens table.
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Shops table.
    /// </summary>
    public DbSet<Shop> Shops => Set<Shop>();

    /// <summary>
    /// Shop operational periods (seller-performance exposure history).
    /// </summary>
    public DbSet<ShopOperationalPeriod> ShopOperationalPeriods => Set<ShopOperationalPeriod>();

    /// <summary>
    /// Platforms table.
    /// </summary>
    public DbSet<Platform> Platforms => Set<Platform>();

    /// <summary>
    /// Product supply sources table.
    /// </summary>
    public DbSet<ProductSource> ProductSources => Set<ProductSource>();

    /// <summary>
    /// Variation spec templates (Color / Size / Number of Items).
    /// </summary>
    public DbSet<VariationSpecTemplate> VariationSpecTemplates => Set<VariationSpecTemplate>();

    /// <summary>
    /// Per-user shipping presets for product forms.
    /// </summary>
    public DbSet<ShippingTemplate> ShippingTemplates => Set<ShippingTemplate>();

    /// <summary>
    /// Cached 4Seller platform shipping carriers (shared catalog, TTL refresh).
    /// </summary>
    public DbSet<PlatformShippingService> PlatformShippingServices => Set<PlatformShippingService>();

    /// <summary>
    /// Mirrored 4Seller TikTok product-discount activities.
    /// </summary>
    public DbSet<ProductDiscountActivity> ProductDiscountActivities => Set<ProductDiscountActivity>();

    /// <summary>
    /// Per-user pricing formula presets for batch sale-price calculation.
    /// </summary>
    public DbSet<PricingTemplate> PricingTemplates => Set<PricingTemplate>();

    /// <summary>
    /// TikTok category catalog table.
    /// </summary>
    public DbSet<TikTokCategory> TikTokCategories => Set<TikTokCategory>();

    /// <summary>
    /// User files table.
    /// </summary>
    public DbSet<UserFile> UserFiles => Set<UserFile>();

    /// <summary>
    /// System settings table.
    /// </summary>
    public DbSet<Setting> Settings => Set<Setting>();

    /// <summary>
    /// Scrape submission batches.
    /// </summary>
    public DbSet<ScrapeBatch> ScrapeBatches => Set<ScrapeBatch>();

    /// <summary>
    /// URLs inside scrape batches.
    /// </summary>
    public DbSet<ScrapeBatchUrl> ScrapeBatchUrls => Set<ScrapeBatchUrl>();

    /// <summary>
    /// Owned 4Seller crawl drafts.
    /// </summary>
    public DbSet<ScrapeDraft> ScrapeDrafts => Set<ScrapeDraft>();

    /// <summary>
    /// Synced 4Seller orders.
    /// </summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>PrintPackShip USPS track-scan batches.</summary>
    public DbSet<UspsScanBatch> UspsScanBatches => Set<UspsScanBatch>();

    /// <summary>Items inside a USPS scan batch.</summary>
    public DbSet<UspsScanBatchItem> UspsScanBatchItems => Set<UspsScanBatchItem>();

    /// <summary>
    /// Order line items.
    /// </summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <summary>Fulfil purchase attempts (per Chrome-profile session history).</summary>
    public DbSet<OrderFulfilAttempt> OrderFulfilAttempts => Set<OrderFulfilAttempt>();

    /// <summary>Reusable issue-tag library.</summary>
    public DbSet<IssueTag> IssueTags => Set<IssueTag>();

    /// <summary>Preset shop audience age brackets.</summary>
    public DbSet<AgeRange> AgeRanges => Set<AgeRange>();

    /// <summary>Canonical TikTok trending keywords (unique by lead_id).</summary>
    public DbSet<ProductOpportunity> ProductOpportunities => Set<ProductOpportunity>();

    /// <summary>Shop membership for a canonical trending keyword.</summary>
    public DbSet<ProductOpportunityShopLink> ProductOpportunityShopLinks =>
        Set<ProductOpportunityShopLink>();

    /// <summary>Per-shop saved keywords (survives snapshot replace).</summary>
    public DbSet<ProductOpportunitySavedKeyword> ProductOpportunitySavedKeywords =>
        Set<ProductOpportunitySavedKeyword>();

    /// <summary>Persisted «Shop này bán gì» result (one row per shop).</summary>
    public DbSet<ShopKeywordResearch> ShopKeywordResearches => Set<ShopKeywordResearch>();

    /// <summary>Order ↔ issue tag assignments.</summary>
    public DbSet<OrderIssueTag> OrderIssueTags => Set<OrderIssueTag>();

    /// <summary>
    /// Mọi lần mua hàng NCC — đã gắn hay chưa gắn đơn sàn, sync hay nhập tay.
    /// </summary>
    public DbSet<SupplierPurchase> SupplierPurchases => Set<SupplierPurchase>();

    /// <summary>
    /// Nhật ký từng lô của Sync Order SHEIN (giữ 7 ngày) — dùng để truy hồi billno bị bỏ qua
    /// và lý do phải gọi lại detail.
    /// </summary>
    public DbSet<SheinSyncRunLog> SheinSyncRunLogs => Set<SheinSyncRunLog>();

    /// <inheritdoc />
    public DbSet<TikTokShopSyncLog> TikTokShopSyncLogs => Set<TikTokShopSyncLog>();

    /// <inheritdoc />
    public DbSet<TikTokShopHealthRecord> TikTokShopHealthRecords => Set<TikTokShopHealthRecord>();

    /// <inheritdoc />
    public DbSet<ShopTaskDefinition> ShopTaskDefinitions => Set<ShopTaskDefinition>();

    /// <inheritdoc />
    public DbSet<ShopTaskCompletion> ShopTaskCompletions => Set<ShopTaskCompletion>();

    /// <summary>
    /// Synced 4Seller listings (Draft / Published mirror).
    /// </summary>
    public DbSet<Listing> Listings => Set<Listing>();

    /// <summary>
    /// Staging origin (recommended vs manual) from extension post-html, applied after draft sync.
    /// </summary>
    public DbSet<ListingAddOrigin> ListingAddOrigins => Set<ListingAddOrigin>();

    /// <summary>
    /// Per-seller delta watermark so listing sync only pulls rows 4Seller changed since last run.
    /// </summary>
    public DbSet<ListingSyncWatermark> ListingSyncWatermarks => Set<ListingSyncWatermark>();

    /// <summary>
    /// Synced listing variations.
    /// </summary>
    public DbSet<ListingVariation> ListingVariations => Set<ListingVariation>();

    /// <summary>Auto publish attempt logs.</summary>
    public DbSet<ListingAutoPublishRun> ListingAutoPublishRuns => Set<ListingAutoPublishRun>();

    /// <summary>
    /// Daily TikTok Compass product metrics (one row per shop product per Vietnam day).
    /// </summary>
    public DbSet<ListingAnalyticsSnapshot> ListingAnalyticsSnapshots =>
        Set<ListingAnalyticsSnapshot>();

    /// <summary>
    /// Precomputed delist counters (one row per shop per Vietnam snapshot day).
    /// </summary>
    public DbSet<ListingDelistDaySummary> ListingDelistDaySummaries =>
        Set<ListingDelistDaySummary>();

    /// <inheritdoc />
    public DbSet<ShopOnlineSession> ShopOnlineSessions => Set<ShopOnlineSession>();

    /// <summary>
    /// Product attribute values TikTok refuses, learned from publish failures.
    /// </summary>
    public DbSet<TikTokAttributeValueRejection> TikTokAttributeValueRejections =>
        Set<TikTokAttributeValueRejection>();

    /// <inheritdoc />
    public DbSet<SupplierSellerInfo> SupplierSellerInfos => Set<SupplierSellerInfo>();

    /// <inheritdoc />
    public DbSet<SupplierProductInfo> SupplierProductInfos => Set<SupplierProductInfo>();

    /// <summary>
    /// Fulfil purchase accounts.
    /// </summary>
    public DbSet<FulfilAccount> FulfilAccounts => Set<FulfilAccount>();

    /// <summary>
    /// Fulfil account ↔ user assignees.
    /// </summary>
    public DbSet<FulfilAccountAssignee> FulfilAccountAssignees => Set<FulfilAccountAssignee>();

    /// <summary>
    /// Fulfil payment cards.
    /// </summary>
    public DbSet<FulfilCard> FulfilCards => Set<FulfilCard>();

    /// <summary>
    /// Fulfil account ↔ card links.
    /// </summary>
    public DbSet<FulfilAccountCard> FulfilAccountCards => Set<FulfilAccountCard>();

    /// <summary>
    /// Fulfil card ↔ user assignees.
    /// </summary>
    public DbSet<FulfilCardAssignee> FulfilCardAssignees => Set<FulfilCardAssignee>();

    /// <summary>
    /// Fulfil transaction ledger.
    /// </summary>
    public DbSet<FulfilTransaction> FulfilTransactions => Set<FulfilTransaction>();

    /// <summary>
    /// Receiving bank accounts (thẻ nhận tiền).
    /// </summary>
    public DbSet<ReceivingBankAccount> ReceivingBankAccounts => Set<ReceivingBankAccount>();

    /// <summary>
    /// Receiving bank transaction ledger (tiền về thẻ nhận tiền).
    /// </summary>
    public DbSet<ReceivingBankTransaction> ReceivingBankTransactions => Set<ReceivingBankTransaction>();

    /// <summary>
    /// Company operating income/expense ledger (sổ cái thu/chi hệ thống).
    /// </summary>
    public DbSet<CompanyLedgerEntry> CompanyLedgerEntries => Set<CompanyLedgerEntry>();

    /// <summary>
    /// Receiving card sources (PingPongX, WorldFirst, …).
    /// </summary>
    public DbSet<ReceivingCardSource> ReceivingCardSources => Set<ReceivingCardSource>();

    /// <summary>
    /// Provider accounts under a receiving card source.
    /// </summary>
    public DbSet<ReceivingSourceAccount> ReceivingSourceAccounts => Set<ReceivingSourceAccount>();

    /// <summary>
    /// Single writer for the denormalized columns on <c>Orders</c> that are derived from the order
    /// and its supplier links: the workflow signals (<see cref="Order.WorkflowTab"/> and the
    /// <c>Has…</c> flags) and the purchase-cost snapshot (<c>Charged*</c>).
    ///
    /// ~25 code paths write the source fields (4Seller sync, mark-ship, SHEIN ingest and
    /// attach/detach, cost/milestone updates, ops stamps, seeding). Stamping the columns at each of
    /// them would drift, so they are recomputed here from whatever the caller changed.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await RefreshOrderDenormalizedAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshOrderDenormalizedAsync(CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();

        // Orders added in this unit of work: links can only be the ones in memory.
        List<Order>? addedOrders = null;
        // Orders already in the database whose signals may be stale.
        HashSet<Guid>? staleIds = null;

        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    (addedOrders ??= []).Add(entry.Entity);
                    break;

                case EntityState.Modified when NeedsRefresh(entry):
                    (staleIds ??= []).Add(entry.Entity.Id);
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SupplierPurchase>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            // Both sides of an attach/detach: the link may have moved between orders, and
            // OrderId is null while it sits in the manual-match queue.
            if (entry.Entity.OrderId is { } current)
            {
                (staleIds ??= []).Add(current);
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted
                && entry.Property(p => p.OrderId).OriginalValue is { } original)
            {
                (staleIds ??= []).Add(original);
            }
        }

        if (addedOrders is not null)
        {
            foreach (var order in addedOrders)
            {
                Recompute(order);
                staleIds?.Remove(order.Id);
            }
        }

        if (staleIds is null || staleIds.Count == 0)
        {
            return;
        }

        // Include the links: an order whose collection was never loaded would otherwise look like
        // an order with no supplier purchase and get stamped with the wrong tab — and, for the cost
        // snapshot, get its whole purchase cost cleared.
        var stale = await Orders
            .Where(o => staleIds.Contains(o.Id))
            .Include(o => o.SupplierPurchases)
            .ToListAsync(cancellationToken);

        foreach (var order in stale)
        {
            if (Entry(order).State == EntityState.Deleted)
            {
                continue;
            }

            Recompute(order);
        }
    }

    /// <summary>
    /// Both denormalizations read the same link set, so they are recomputed together — one of them
    /// running on a different view of the links than the other is how the two columns drift apart.
    /// </summary>
    private void Recompute(Order order)
    {
        var links = EffectiveLinksFor(order);
        OrderWorkflowSignals.Apply(order, links);
        OrderSupplierCost.ApplyAggregates(order, links);
    }

    private static bool NeedsRefresh(EntityEntry<Order> entry)
    {
        // Never stamped (e.g. a row the backfill has not reached yet) → always recompute.
        if (entry.Entity.WorkflowTab is null)
        {
            return true;
        }

        foreach (var property in OrderWorkflowSignals.SourceProperties)
        {
            if (entry.Property(property).IsModified)
            {
                return true;
            }
        }

        // A caller that already stamped the cost snapshot itself may have done so from a navigation
        // that was never loaded, which zeroes the order's purchase cost. Recomputing here from the
        // Include'd links is what makes that unnoticeable instead of permanent.
        foreach (var property in OrderSupplierCost.SnapshotProperties)
        {
            if (entry.Property(property).IsModified)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The links the order will have once this unit of work commits: tracked rows minus the ones
    /// being deleted or moved to another order.
    ///
    /// Reads the foreign key rather than the navigation, so a link reassigned to another order
    /// drops out of the old order's set even before EF fixes up the collections. The navigation is
    /// still unioned in, for a freshly added graph whose keys are not fixed up yet.
    /// </summary>
    private List<SupplierPurchase> EffectiveLinksFor(Order order)
    {
        var links = new List<SupplierPurchase>();
        foreach (var entry in ChangeTracker.Entries<SupplierPurchase>())
        {
            if (entry.State is EntityState.Deleted or EntityState.Detached)
            {
                continue;
            }

            if (entry.Entity.OrderId == order.Id)
            {
                links.Add(entry.Entity);
            }
        }

        foreach (var link in order.SupplierPurchases)
        {
            if (links.Contains(link))
            {
                continue;
            }

            // Only trust the navigation while the FK is still unset (a new graph EF has not fixed
            // up yet); once it points elsewhere the link belongs to that other order, and a link
            // being deleted was already skipped above.
            if (link.OrderId is null)
            {
                links.Add(link);
            }
        }

        return links;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FullName).HasMaxLength(200);
            entity.Property(u => u.FourSellerCookieLoginStatus)
                .HasMaxLength(20)
                .HasDefaultValue("none");
            entity.Property(u => u.FourSellerProxy)
                .HasMaxLength(300);
            entity.Property(u => u.Theme)
                .HasMaxLength(10)
                .HasDefaultValue("light");
            entity.Property(u => u.FulfilPinHash)
                .HasMaxLength(64);
            entity.HasIndex(u => u.FulfilPinHash).IsUnique();
            entity.HasIndex(u => u.UserName).IsUnique();
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.Property(r => r.Description).HasMaxLength(500);
            entity.HasIndex(r => r.Name).IsUnique();
        });
    }
}
