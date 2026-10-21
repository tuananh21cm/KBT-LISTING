using System.Text.Json;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using Shared.Helpers;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Database seeding for roles, permissions, and default admin user.
/// </summary>
public class DataSeeder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataSeeder> _logger;

    /// <summary>
    /// Initializes the data seeder.
    /// </summary>
    public DataSeeder(IServiceProvider serviceProvider, ILogger<DataSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Seeds initial data if not present.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var previousTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(300);
        try
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            context.Database.SetCommandTimeout(previousTimeout);
        }

        await SeedRolesAsync(roleManager);
        await SeedPermissionsAsync(context, roleManager);
        await SeedPlatformsAsync(context);
        await SeedProductSourcesAsync(context);
        await SeedReceivingCardSourcesAsync(context);
        await SeedVariationSpecTemplatesAsync(context);
        // Flip nghĩa local/realtime trước seed default mới — tránh cài mới bị flip nhầm.
        await MigrateSheinHeaderSourceSemanticsAsync(context);
        await SeedSettingsAsync(context);
        await MigrateLegacyOrderSyncSettingsAsync(context);
        await MigrateLegacyListingSyncSettingsAsync(context);
        await SeedTikTokAttributeValueRejectionsAsync(context);
        await DemoteRejectionsWithoutValueIdAsync(context);
        await DemoteRejectionsUsedByLiveListingsAsync(context);
        await SeedShopTaskDefinitionsAsync(context);
        await SeedAdminUserAsync(userManager);
        await SeedSellerUserAsync(userManager);
        // Chạy trước SheinTimelineBackfill: dọn mốc Transit bịa xong thì đợt kia mới có chỗ trống để
        // stamp lại mốc rời kho thật từ payload SHEIN gốc.
        await FabricatedTransitCleanup.RunAsync(context, _logger, cancellationToken);
        // Timeline Refund/Hủy lệch cột trạng thái sàn (stamp tay / SHEIN hoàn NCC) → chuẩn theo TikTok.
        await ConflictingTerminalMilestoneCleanup.RunAsync(context, _logger, cancellationToken);
        // Chạy sau đợt trên: mốc Refund còn sót lại mới là bằng chứng hoàn của sàn để trả status về
        // REFUND cho đơn bị sync sau đẩy về shipped/COMPLETED (UI hiện oan "Đang giao").
        await RefundStatusLatchRepair.RunAsync(context, _logger, cancellationToken);
        await SheinTimelineBackfill.RunAsync(context, _logger, cancellationToken);
        await SheinUnpaidLinkBackfill.RunAsync(context, _logger, cancellationToken);
        // Tách usedWalletPrice / retail_delivery_fee từ RawPayload — phải trước dựng sổ cái.
        await SheinWalletCostBackfill.RunAsync(context, _logger, cancellationToken);
        // Chạy cuối: gỡ billno mua trước khi đơn được đặt rồi tính lại tổng và mốc, nên phải để các
        // đợt dựng lại giá mua / mốc chạy xong trước, bằng không chúng lại ghi số từ link vừa gỡ.
        await SheinCausalityLinkCleanup.RunAsync(context, _logger, cancellationToken);
        // Gỡ cờ hàng ảo dán oan trước khi dựng sổ cái: cờ đó quyết định dòng nào được tính tiền.
        await SheinVirtualFlagRepair.RunAsync(context, _logger, cancellationToken);
        // Chạy trước đợt dựng sổ cái: dòng chuyển sang `ignored` ở đây phải nằm ngoài tập được tính
        // tiền, bằng không đợt kia dựng lại đúng những dòng chi người dùng vừa xoá.
        await LedgerDeletedIgnoreBackfill.RunAsync(context, _logger, cancellationToken);
        // Chạy sau mọi đợt đổi tiền / cờ ở trên và ngay trước đợt dựng sổ cái: nó là chỗ chốt lại
        // Charged* + IsCharged, nên đứng sớm hơn thì các đợt kia lại làm lệch tiếp.
        await OrderChargedAggregatesRepair.RunAsync(context, _logger, cancellationToken);
        // Sau cùng hẳn: dựng sổ cái từ cờ IsCharged đã chốt, nên mọi đợt đổi giá mua phải xong trước.
        await RebuildSupplierPurchaseLedgerAsync(scope, context, userManager, cancellationToken);
        // Chạy sau mọi đợt sửa trạng thái / gắn-gỡ link ở trên: WorkflowTab suy ra từ chính những
        // trường đó, stamp trước thì lại phải stamp lại.
        // Clear legacy IN_TRANSIT_GROUP trước backfill để stamp lại thành Chờ lấy / Đang giao.
        await TransitTabSplitRepair.RunAsync(context, _logger, cancellationToken);
        await OrderWorkflowSignalsBackfill.RunAsync(context, _logger, cancellationToken);
        await TikTokPayoutMismatchPendingBackfill.RunAsync(context, _logger, cancellationToken);
        await SellerNetPlatformDiscountBackfill.RunAsync(context, _logger, cancellationToken);

        _logger.LogInformation("Database seeding completed.");
    }

    /// <summary>
    /// Đợt dựng lại sổ cái cần <see cref="IOrderSyncService"/> chứ không chỉ DbContext, nên nó không
    /// gọi trực tiếp được như các backfill khác. Ghi công cho tài khoản admin: dòng chi dựng lại là
    /// hành động của hệ thống, và <c>CreatedByUserId</c> là bắt buộc.
    /// </summary>
    private async Task RebuildSupplierPurchaseLedgerAsync(
        IServiceScope scope,
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var admin = await userManager.FindByNameAsync("admin");
        if (admin is null)
        {
            _logger.LogWarning("Bỏ qua đợt dựng lại sổ cái: chưa có tài khoản admin để ghi công.");
            return;
        }

        var orderSyncService = scope.ServiceProvider.GetRequiredService<IOrderSyncService>();
        await SupplierPurchaseLedgerBackfill.RunAsync(
            context, orderSyncService, admin.Id, _logger, cancellationToken);
    }

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var roleName in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant(),
                    Description = $"{roleName} role"
                });
            }
        }
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext context, RoleManager<ApplicationRole> roleManager)
    {
        var permissionDefinitions = new List<(string Code, string Name, string Module, string Application)>
        {
            ("seller.dashboard.view", "Xem Dashboard Seller", "Dashboard", ApplicationNames.SellerWeb),
            ("seller.orders.view", "Xem đơn hàng Seller", "Orders", ApplicationNames.SellerWeb),
            ("seller.orders.manage", "Quản lý đơn hàng Seller", "Orders", ApplicationNames.SellerWeb),
            ("seller.profile.view", "Xem hồ sơ Seller", "Profile", ApplicationNames.SellerWeb),
            ("seller.profile.edit", "Chỉnh sửa hồ sơ Seller", "Profile", ApplicationNames.SellerWeb),
            ("seller.shops.view", "Xem shop", "Shops", ApplicationNames.SellerWeb),
            ("seller.shops.edit", "Sửa shop", "Shops", ApplicationNames.SellerWeb),
            ("seller.shops.manage", "Quản lý shop", "Shops", ApplicationNames.SellerWeb),
            ("admin.shops.view", "Xem tất cả shop", "Shops", ApplicationNames.AdminWeb),
            ("admin.shops.manage", "Quản lý shop hệ thống", "Shops", ApplicationNames.AdminWeb),
            ("admin.product_opportunities.view", "Xem Product opportunities", "ProductOpportunities", ApplicationNames.AdminWeb),
            ("seller.product_opportunities.view", "Xem Product opportunities", "ProductOpportunities", ApplicationNames.SellerWeb),
            ("seller.products.view", "Xem sản phẩm", "Products", ApplicationNames.SellerWeb),
            ("seller.products.manage", "Quản lý sản phẩm", "Products", ApplicationNames.SellerWeb),
            ("seller.files.view", "Xem file", "Files", ApplicationNames.SellerWeb),
            ("seller.files.manage", "Quản lý file", "Files", ApplicationNames.SellerWeb),
            ("identity.profile.view", "Xem hồ sơ", "Identity", "platform"),
            ("identity.password.change", "Đổi mật khẩu", "Identity", "platform"),
            ("admin.users.view", "Xem người dùng", "Users", ApplicationNames.AdminWeb),
            ("admin.users.manage", "Quản lý người dùng", "Users", ApplicationNames.AdminWeb),
            ("admin.roles.view", "Xem vai trò & phân quyền", "Roles", ApplicationNames.AdminWeb),
            ("admin.roles.manage", "Quản lý vai trò & phân quyền", "Roles", ApplicationNames.AdminWeb),
            ("admin.products.view", "Xem sản phẩm hệ thống", "Products", ApplicationNames.AdminWeb),
            ("admin.products.manage", "Quản lý sản phẩm hệ thống", "Products", ApplicationNames.AdminWeb),
            ("admin.platforms.view", "Xem nền tảng", "Platforms", ApplicationNames.AdminWeb),
            ("admin.platforms.manage", "Quản lý nền tảng", "Platforms", ApplicationNames.AdminWeb),
            ("admin.product-sources.view", "Xem nguồn hàng", "ProductSources", ApplicationNames.AdminWeb),
            ("admin.product-sources.manage", "Quản lý nguồn hàng", "ProductSources", ApplicationNames.AdminWeb),
            ("admin.categories.view", "Xem danh mục TikTok", "Categories", ApplicationNames.AdminWeb),
            ("admin.categories.manage", "Đồng bộ & quản lý danh mục TikTok", "Categories", ApplicationNames.AdminWeb),
            ("admin.settings.view", "Xem cấu hình hệ thống", "Settings", ApplicationNames.AdminWeb),
            ("admin.settings.manage", "Quản lý cấu hình hệ thống", "Settings", ApplicationNames.AdminWeb),
            ("admin.orders.view", "Xem đơn hàng hệ thống", "Orders", ApplicationNames.AdminWeb),
            ("admin.orders.manage", "Đồng bộ & quản lý đơn hàng hệ thống", "Orders", ApplicationNames.AdminWeb),
            ("admin.listings.view", "Xem listing hệ thống", "Listings", ApplicationNames.AdminWeb),
            ("admin.listings.manage", "Đồng bộ & quản lý listing hệ thống", "Listings", ApplicationNames.AdminWeb),
            ("admin.promotions.view", "Xem promotions hệ thống", "Promotions", ApplicationNames.AdminWeb),
            ("admin.promotions.manage", "Đồng bộ & quản lý promotions hệ thống", "Promotions", ApplicationNames.AdminWeb),
            ("seller.promotions.view", "Xem promotions", "Promotions", ApplicationNames.SellerWeb),
            ("seller.promotions.manage", "Đồng bộ & quản lý promotions", "Promotions", ApplicationNames.SellerWeb),
            ("admin.supplier-sellers.view", "Xem danh sách shop NCC", "SupplierSellers", ApplicationNames.AdminWeb),
            ("seller.supplier-sellers.view", "Xem danh sách shop NCC", "SupplierSellers", ApplicationNames.SellerWeb),
            ("admin.dashboard.view", "Xem Dashboard tổng quan", "Dashboard", ApplicationNames.AdminWeb),
            ("admin.fulfil.view", "Xem quản lý fulfil", "Fulfil", ApplicationNames.AdminWeb),
            ("admin.fulfil.manage", "Quản lý tài khoản / thẻ / giao dịch fulfil", "Fulfil", ApplicationNames.AdminWeb),
            ("admin.company-ledger.view", "Xem sổ cái thu/chi hệ thống", "Finance", ApplicationNames.AdminWeb),
            ("admin.company-ledger.manage", "Quản lý sổ cái thu/chi hệ thống", "Finance", ApplicationNames.AdminWeb),
            ("seller.company-ledger.view", "Xem sổ cái thu/chi được gán", "Finance", ApplicationNames.SellerWeb),
            ("admin.shop_tasks.view", "Xem nhiệm vụ hệ thống", "ShopTasks", ApplicationNames.AdminWeb),
            ("admin.shop_tasks.manage", "Cấu hình nhiệm vụ hệ thống", "ShopTasks", ApplicationNames.AdminWeb),
            ("seller.shop_tasks.view", "Xem nhiệm vụ hệ thống", "ShopTasks", ApplicationNames.SellerWeb)
        };

        var permissions = new List<Permission>();
        foreach (var definition in permissionDefinitions)
        {
            var existing = await context.Permissions.FirstOrDefaultAsync(p => p.Code == definition.Code);
            if (existing is null)
            {
                existing = new Permission
                {
                    Code = definition.Code,
                    Name = definition.Name,
                    Module = definition.Module,
                    Application = definition.Application
                };
                context.Permissions.Add(existing);
            }

            permissions.Add(existing);
        }

        await context.SaveChangesAsync();

        await SyncAdminRolePermissionsAsync(context, roleManager);

        var sellerPermissionIds = permissions
            .Where(p =>
                (p.Application == ApplicationNames.SellerWeb && p.Code != "seller.shops.manage")
                || p.Application == "platform")
            .Select(p => p.Id)
            .ToList();

        await AssignPermissionsToRoleAsync(context, roleManager, RoleNames.Seller, sellerPermissionIds);

        await SyncSellerRolePermissionsAsync(context, roleManager);

        var fulfilPermissionIds = permissions
            .Where(p =>
                p.Code is "admin.orders.view"
                    or "admin.orders.manage"
                    or "admin.product-sources.view")
            .Select(p => p.Id)
            .ToList();
        await AssignPermissionsToRoleAsync(context, roleManager, RoleNames.Fulfil, fulfilPermissionIds);
    }

    private static async Task SeedPlatformsAsync(ApplicationDbContext context)
    {
        var defaults = new List<(string Code, string Name)>
        {
            ("TikTok", "TikTok Shop"),
            ("Amazon", "Amazon"),
            ("Shopify", "Shopify")
        };

        foreach (var (code, name) in defaults)
        {
            var existing = await context.Platforms.FirstOrDefaultAsync(p => p.Code == code);
            if (existing is null)
            {
                context.Platforms.Add(new Platform
                {
                    Code = code,
                    Name = name,
                    IsActive = true
                });
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedProductSourcesAsync(ApplicationDbContext context)
    {
        // Domains bắt buộc để extension nhận diện PDP (us.shein.com khớp shein.com).
        // Hàng seed cũ thiếu Domains → backfill khi restart API.
        var defaults = new List<(Guid Id, string Code, string Name, string? Description, string Domains)>
        {
            (new Guid("a1000000-0000-0000-0000-000000000001"), "SHEIN", "Shein", "Nguồn hàng Shein", "shein.com"),
            (new Guid("a1000000-0000-0000-0000-000000000002"), "TEMU", "Temu", "Nguồn hàng Temu", "temu.com"),
            (new Guid("a1000000-0000-0000-0000-000000000003"), "CJ", "CJDropshipping", "Nguồn hàng CJDropshipping", "cjdropshipping.com")
        };

        foreach (var (id, code, name, description, domains) in defaults)
        {
            var existing = await context.ProductSources.FirstOrDefaultAsync(p => p.Code == code);
            if (existing is null)
            {
                context.ProductSources.Add(new ProductSource
                {
                    Id = id,
                    Code = code,
                    Name = name,
                    Description = description,
                    Domains = domains,
                    IsActive = true
                });
            }
            else if (string.IsNullOrWhiteSpace(existing.Domains))
            {
                existing.Domains = domains;
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedReceivingCardSourcesAsync(ApplicationDbContext context)
    {
        var defaults = new List<(Guid Id, string Code, string Name, string? Description, bool SupportsAccounts)>
        {
            (new Guid("b2000000-0000-0000-0000-000000000001"), "PINGPONGX", "PingPongX", "Nguồn thẻ PingPongX (nhiều tài khoản)", true),
            (new Guid("b2000000-0000-0000-0000-000000000002"), "WORLDFIRST", "WorldFirst", "Nguồn thẻ WorldFirst (nhiều tài khoản)", true),
            (new Guid("b2000000-0000-0000-0000-000000000003"), "OTHER", "Khác", "Nguồn thẻ / ngân hàng khác", false)
        };

        foreach (var (id, code, name, description, supportsAccounts) in defaults)
        {
            var existing = await context.ReceivingCardSources.FirstOrDefaultAsync(p => p.Code == code);
            if (existing is null)
            {
                context.ReceivingCardSources.Add(new ReceivingCardSource
                {
                    Id = id,
                    Code = code,
                    Name = name,
                    Description = description,
                    SupportsAccounts = supportsAccounts,
                    IsActive = true
                });
            }
            else
            {
                existing.Name = name;
                existing.Description = description;
                existing.SupportsAccounts = supportsAccounts;
                existing.IsActive = true;
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedVariationSpecTemplatesAsync(ApplicationDbContext context)
    {
        if (await context.VariationSpecTemplates.AnyAsync())
        {
            return;
        }

        var colors = new[] { "White", "Pink", "Red", "Wine", "Blue", "Gold", "Green" };
        var sizes = new[] { "XXS", "XS", "S", "M", "L", "XL", "XXL", "3XL", "4XL", "5XL" };
        var items = new[] { "1", "2", "3", "4", "5", "6", "8", "10", "12" };

        void AddAll(string specType, IEnumerable<string> values)
        {
            var order = 0;
            foreach (var value in values)
            {
                context.VariationSpecTemplates.Add(new VariationSpecTemplate
                {
                    SpecType = specType,
                    Value = value,
                    SortOrder = order++,
                    IsActive = true
                });
            }
        }

        AddAll("Color", colors);
        AddAll("Size", sizes);
        AddAll("Number of Items", items);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Nhiệm vụ mẫu để admin có sẵn khuôn mà sửa. Seed tắt sẵn nên không tự giao việc cho seller
    /// khi nâng cấp; idempotent theo URL.
    /// </summary>
    private static async Task SeedShopTaskDefinitionsAsync(ApplicationDbContext context)
    {
        const string sampleUrl = "https://seller-us.tiktok.com/homepage";
        if (await context.ShopTaskDefinitions.AnyAsync())
        {
            return;
        }

        context.ShopTaskDefinitions.Add(new ShopTaskDefinition
        {
            Name = "Vào trang chủ Seller Center",
            Url = sampleUrl,
            Description = "Mở Seller Center mỗi ngày để TikTok ghi nhận shop có hoạt động.",
            SortOrder = 0,
            IsActive = false
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the one rejection observed before the learning existed: TikTok withdrew
    /// <c>Do not dry clean</c> from <c>Washing instructions</c> (100401) on category 903824 while
    /// 4Seller still offers it with id 1001198, which failed 10 publishes.
    /// <para>
    /// Deliberately no row for <c>Hand wash only</c> / <c>Machine washable</c>: an earlier fix assumed
    /// TikTok rejects those, but no failure ever named them and hundreds of live listings use them.
    /// </para>
    /// </summary>
    private static async Task SeedTikTokAttributeValueRejectionsAsync(ApplicationDbContext context)
    {
        const string categoryId = "903824";
        const string attributeId = "100401";
        const string valueName = "Do not dry clean";
        const string valueNameNorm = "donotdryclean";

        var exists = await context.TikTokAttributeValueRejections.AnyAsync(r =>
            r.CategoryId == categoryId
            && r.AttributeId == attributeId
            && r.ValueNameNorm == valueNameNorm);
        if (exists)
        {
            return;
        }

        var now = DateTime.UtcNow;
        context.TikTokAttributeValueRejections.Add(new TikTokAttributeValueRejection
        {
            CategoryId = categoryId,
            AttributeId = attributeId,
            AttributeName = "Washing instructions",
            ValueId = "1001198",
            ValueName = valueName,
            ValueNameNorm = valueNameNorm,
            RejectionCount = 0,
            FirstSeenAt = now,
            LastSeenAt = now,
            Source = TikTokAttributeRejectionSources.Seed,
            SampleError =
                "Operation Not Allowed. Cannot use custom value names for product attribute "
                + "`Washing instructions`: attribute ID `100401` does not support custom value names. "
                + "Remove or replace the custom value `Do not dry clean` and retry."
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Demotes rejections learned before the service could tell the two causes apart. A row with no
    /// <c>ValueId</c> only proves we sent a bare value name, so blocking that value emptied a mandatory
    /// attribute and turned "custom value" errors into "Specifics.&lt;attribute&gt;" ones. Seeded and
    /// manual rows keep their meaning.
    /// </summary>
    private static async Task DemoteRejectionsWithoutValueIdAsync(ApplicationDbContext context)
    {
        var poisoned = await context.TikTokAttributeValueRejections
            .Where(r => r.Source == TikTokAttributeRejectionSources.PublishError && r.ValueId == null)
            .ToListAsync();
        if (poisoned.Count == 0)
        {
            return;
        }

        foreach (var row in poisoned)
        {
            row.Source = TikTokAttributeRejectionSources.MissingValueId;
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Xếp lại các dòng denylist có <c>valueId</c> theo bằng chứng từ listing **lên sàn sau** lần bị
    /// từ chối: có listing như vậy dùng đúng id đó → lỗi nằm ở payload chứ không phải TikTok thu hồi
    /// giá trị, hạ xuống <c>contradicted_by_live</c> (không chặn, vì chặn sai làm rỗng thuộc tính bắt
    /// buộc và đổi lỗi thành <c>Specifics.&lt;tên attribute&gt;</c>); không có nữa thì đưa trở lại thành
    /// dòng chặn. Listing lên sàn **trước** lần từ chối không phải bằng chứng: TikTok thu hồi giá trị
    /// khỏi một category bất kỳ lúc nào và listing cũ vẫn giữ giá trị đó. Số dòng denylist rất nhỏ nên
    /// mỗi dòng chỉ soi tối đa 20 payload; idempotent.
    /// </summary>
    private async Task DemoteRejectionsUsedByLiveListingsAsync(ApplicationDbContext context)
    {
        await DemoteCustomValueNameRejectionsAsync(context);

        var rows = await context.TikTokAttributeValueRejections
            .Where(r =>
                r.ValueId != null
                && r.CategoryId != TikTokAttributeValueRejection.AllCategories
                && (r.Source == TikTokAttributeRejectionSources.PublishError
                    || r.Source == TikTokAttributeRejectionSources.Seed
                    || r.Source == TikTokAttributeRejectionSources.ContradictedByLive))
            .ToListAsync();
        if (rows.Count == 0)
        {
            return;
        }

        var changed = 0;
        foreach (var row in rows)
        {
            var payloads = await context.Listings
                .AsNoTracking()
                .Where(l =>
                    l.CategoryId == row.CategoryId
                    && l.PageType == ListingPageTypes.Published
                    && l.SiteStatus == ListingSiteStatuses.Active
                    && l.SyncError == null
                    && l.RawPayload != null
                    && l.PlatformUpdateTime >= row.FirstSeenAt)
                .OrderByDescending(l => l.PlatformUpdateTime)
                .Select(l => l.RawPayload!)
                .Take(20)
                .ToListAsync();

            var live = payloads.Any(payload =>
            {
                try
                {
                    return TikTokAttributeRejectionErrors.ReadSentValueId(
                        payload,
                        row.AttributeId,
                        row.ValueName) is not null;
                }
                catch (JsonException)
                {
                    return false;
                }
            });

            var target = live
                ? TikTokAttributeRejectionSources.ContradictedByLive
                : row.Source == TikTokAttributeRejectionSources.ContradictedByLive
                    ? TikTokAttributeRejectionSources.PublishError
                    : row.Source;
            if (target == row.Source)
            {
                continue;
            }

            row.Source = target;
            changed++;
            _logger.LogWarning(
                "Xếp lại denylist {AttributeId}/{ValueName} ({ValueId}) ở category {CategoryId}: "
                + "{Source} (listing lên sàn sau lần từ chối {Live} dùng đúng valueId này)",
                row.AttributeId,
                row.ValueName,
                row.ValueId,
                row.CategoryId,
                target,
                live ? "có" : "không");
        }

        if (changed > 0)
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Hạ mọi dòng denylist học từ thông điệp "cannot use custom value names" xuống mức chẩn đoán.
    /// <para>
    /// Thông điệp đó chỉ nói giá trị tới TikTok dưới dạng chữ trần, tức payload của ta thiếu id — không
    /// phải TikTok gỡ giá trị khỏi enum. Bản build trước coi đó là bằng chứng nên chặn mất 5 trong 6 giá
    /// trị của <c>903824/100401</c>, làm thuộc tính bắt buộc rỗng và lỗi đổi thành
    /// <c>Specifics.Washing instructions</c>. Idempotent.
    /// </para>
    /// </summary>
    private async Task DemoteCustomValueNameRejectionsAsync(ApplicationDbContext context)
    {
        var rows = await context.TikTokAttributeValueRejections
            .Where(r =>
                r.Source == TikTokAttributeRejectionSources.PublishError
                && r.SampleError != null
                && r.SampleError.Contains("custom value"))
            .ToListAsync();
        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            row.Source = TikTokAttributeRejectionSources.MissingValueId;
            _logger.LogWarning(
                "Bỏ chặn denylist {AttributeId}/{ValueName} ở category {CategoryId}: lỗi gốc là "
                + "\"custom value names\", tức payload thiếu valueId chứ không phải TikTok gỡ giá trị.",
                row.AttributeId,
                row.ValueName,
                row.CategoryId);
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedSettingsAsync(ApplicationDbContext context)
    {
        var defaults = new List<(string Group, string Key, string Label, string Value, string Type, string? Description)>
        {
            ("Bunny Config", "bunny.access_key", "Access Key", "", "password", "Bunny CDN Access Key"),
            ("Bunny Config", "bunny.storage_zone", "Storage Zone", "", "text", "Tên storage zone trên Bunny"),
            ("Bunny Config", "bunny.storage_hostname", "Storage Hostname", "sg.storage.bunnycdn.com", "text", "Chỉ hostname Storage API (vd: sg.storage.bunnycdn.com), không kèm https://"),
            ("Bunny Config", "bunny.cdn_hostname", "CDN Hostname", "", "text", "CDN pull zone (vd: xxx.b-cdn.net)"),
            ("Bunny Config", "bunny.max_file_size_mb", "Max File Size (MB)", "100", "number", "Giới hạn kích thước file upload (MB)"),
            ("Đồng bộ đơn hàng mới", OrderSyncSettingKeys.NewEnabled,
                "Tự động lấy đơn hàng mới", "true", "boolean",
                "Job nền nhẹ: kéo đơn pending/on_hold/to_ship/canceled trong cửa sổ lookback ngắn"),
            ("Đồng bộ đơn hàng mới", OrderSyncSettingKeys.NewIntervalMinutes,
                "Chu kỳ lấy đơn mới (phút)",
                OrderSyncSettingKeys.DefaultNewIntervalMinutes.ToString(), "number",
                $"Khoảng cách giữa các lần lấy đơn mới sau khi lượt trước kết thúc ({OrderSyncSettingKeys.MinIntervalMinutes}–{OrderSyncSettingKeys.MaxIntervalMinutes})"),
            ("Đồng bộ đơn hàng mới", OrderSyncSettingKeys.NewLookbackDays,
                "Lookback đơn mới (ngày VN)",
                OrderSyncSettingKeys.DefaultNewLookbackDays.ToString(), "number",
                $"Cửa sổ create-date giờ VN cho luồng đơn mới; bộ lọc 4Seller là cùng khoảng tuyệt đối theo America/Los_Angeles ({OrderSyncSettingKeys.MinNewLookbackDays}–{OrderSyncSettingKeys.MaxNewLookbackDays})"),
            ("Đồng bộ đơn hàng mới", OrderSyncSettingKeys.NewBatchSize,
                "Số tài khoản mỗi lô (đơn mới)",
                OrderSyncSettingKeys.DefaultNewBatchSize.ToString(), "number",
                $"Số tài khoản 4Seller chạy song song trong một lô; các lô chạy tuần tự đến hết ({OrderSyncSettingKeys.MinBatchSize}–{OrderSyncSettingKeys.MaxBatchSize}). Mỗi tài khoản kéo đơn của mọi shop thuộc cookie đó"),
            ("Đồng bộ đơn hàng mới", OrderSyncSettingKeys.WatchdogEnabled,
                "Cảnh báo Telegram khi job sync đứng", "true", "boolean",
                "Gửi vào Chat ID xử lý đơn khi job đơn mới / trạng thái trễ quá ngưỡng, hoặc khi một vòng lặp nền vừa crash và phải khởi động lại"),
            ("Đồng bộ đơn hàng mới", OrderSyncSettingKeys.WatchdogOverdueMinutes,
                "Ngưỡng cảnh báo trễ (phút)",
                OrderSyncSettingKeys.DefaultWatchdogOverdueMinutes.ToString(), "number",
                $"Trễ quá số phút này so với thời điểm tới hạn thì cảnh báo ({OrderSyncSettingKeys.MinWatchdogOverdueMinutes}–{OrderSyncSettingKeys.MaxWatchdogOverdueMinutes})"),
            ("Đồng bộ trạng thái đơn hàng", OrderSyncSettingKeys.StatusEnabled,
                "Tự động cập nhật trạng thái đơn", "true", "boolean",
                "Job nền nặng: quét cả 6 bucket 4Seller trong cửa sổ lookback dài để bắt đổi trạng thái muộn. Giữ quyền ghi trọn lượt nên luồng đơn mới tạm nghỉ đến khi xong"),
            ("Đồng bộ trạng thái đơn hàng", OrderSyncSettingKeys.StatusIntervalHours,
                "Chu kỳ cập nhật trạng thái (giờ)",
                OrderSyncSettingKeys.DefaultStatusIntervalHours.ToString(), "number",
                $"Khoảng cách giữa các lần cập nhật trạng thái sau khi lượt trước kết thúc ({OrderSyncSettingKeys.MinIntervalHours}–{OrderSyncSettingKeys.MaxIntervalHours})"),
            ("Đồng bộ trạng thái đơn hàng", OrderSyncSettingKeys.StatusLookbackDays,
                "Lookback trạng thái (ngày VN)",
                OrderSyncSettingKeys.DefaultStatusLookbackDays.ToString(), "number",
                $"Cửa sổ create-date giờ VN cho luồng trạng thái; bộ lọc 4Seller là cùng khoảng tuyệt đối theo America/Los_Angeles ({OrderSyncSettingKeys.MinStatusLookbackDays}–{OrderSyncSettingKeys.MaxStatusLookbackDays})"),
            ("Đồng bộ trạng thái đơn hàng", OrderSyncSettingKeys.StatusBatchSize,
                "Số tài khoản mỗi lô (trạng thái)",
                OrderSyncSettingKeys.DefaultStatusBatchSize.ToString(), "number",
                $"Số tài khoản 4Seller chạy song song trong một lô; các lô chạy tuần tự đến hết ({OrderSyncSettingKeys.MinBatchSize}–{OrderSyncSettingKeys.MaxBatchSize}). Mỗi tài khoản kéo đơn của mọi shop thuộc cookie đó"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.TriggerEnabled,
                "Sync đơn sau bán từ TikTok Shop qua 4Seller", "false", "boolean",
                "Job nền yêu cầu 4Seller kéo yêu cầu hoàn/trả mới từ TikTok Shop về kho dữ liệu của 4Seller"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.TriggerIntervalMinutes,
                "Chu kỳ sync từ TikTok (phút)",
                AfterSalesSyncSettingKeys.DefaultTriggerIntervalMinutes.ToString(), "number",
                $"Khoảng cách giữa các lần yêu cầu 4Seller kéo dữ liệu ({AfterSalesSyncSettingKeys.MinIntervalMinutes}–{AfterSalesSyncSettingKeys.MaxIntervalMinutes})"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.PullEnabled,
                "Sync đơn refund cần xử lý từ 4Seller", "false", "boolean",
                "Job nền kéo danh sách hoàn/trả từ 4Seller về chuẩn trạng thái REFUND cho đơn local"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.PullIntervalMinutes,
                "Chu kỳ sync từ 4Seller (phút)",
                AfterSalesSyncSettingKeys.DefaultPullIntervalMinutes.ToString(), "number",
                $"Khoảng cách giữa các lần kéo danh sách hoàn/trả ({AfterSalesSyncSettingKeys.MinIntervalMinutes}–{AfterSalesSyncSettingKeys.MaxIntervalMinutes})"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.NotifyEnabled,
                "Thông báo đơn cần xử lý sau bán", "true", "boolean",
                "Gửi Telegram cho đơn có thay đổi trạng thái hoàn/trả trong lượt sync. Lượt chạy đầu tiên chỉ đồng bộ, không gửi"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.TelegramChatId,
                "Chat ID nhóm xử lý sau bán", AfterSalesSyncSettingKeys.DefaultTelegramChatId, "text",
                "Nhóm Telegram nhận thông báo đơn hoàn/trả cần xử lý"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.Watermark,
                "Mốc đã đồng bộ (nội bộ)", "", "text",
                "Con trỏ JSON theo từng seller, job tự ghi. Xoá để quét lại từ đầu (lượt kế tiếp sẽ không gửi thông báo)"),
            // Hàng phải tồn tại trước: SettingProvider.SetValueAsync chỉ update, không insert — thiếu
            // hàng thì đợt repair không ghi được cờ `done` và chạy lại mỗi tick.
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.RefundedAtCreateTimeRepair,
                "Đợt sửa mốc hoàn (nội bộ)", "", "text",
                "Cờ một lần: job kéo lại toàn bộ case hoàn và stamp RefundedAt theo platformCreateTime. Xoá để chạy lại"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.OrphanRefundReconcile,
                "Đợt dọn REFUND mồ côi (nội bộ, đã thay thế)", "", "text",
                "Cờ một lần đã dùng xong; luật của nó bị đảo bởi đợt bám danh sách return bên dưới"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.RefundCasePresenceReconcile,
                "Đợt bám danh sách hoàn 4Seller (nội bộ)", "", "text",
                "Cờ một lần: job kéo lại toàn bộ case và đồng bộ REFUND theo đúng danh sách return của 4Seller — đơn có case (kể cả case còn mở) vào tab hoàn với mốc = platformCreateTime, đơn không có case nào thì gỡ REFUND. Xoá để chạy lại"),
            ("Đồng bộ đơn sau bán (4Seller)", AfterSalesSyncSettingKeys.ReturnStatusBackfill,
                "Đợt backfill trạng thái return (nội bộ)", "", "text",
                "Cờ một lần: job ghi Order.ReturnStatus từ returnStatus 4Seller lên mọi đơn hoàn. Xoá để chạy lại"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.Enabled, "Tự động đồng bộ đơn SHEIN", "false", "boolean",
                "Khi bật: Realtime = extension Auto Sync (chrome.alarms, chạy cả khi đóng panel); Local = legacy backend session"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.IntervalValue, "Chu kỳ đồng bộ", "60", "number",
                "Giá trị chu kỳ auto-sync (kết hợp với đơn vị giây/phút); tối thiểu 1"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.IntervalUnit, "Đơn vị chu kỳ", "seconds", "text",
                "Đơn vị chu kỳ auto-sync: seconds hoặc minutes"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.UpdateMode, "Chế độ cập nhật", "new", "text",
                "Chỉ áp dụng cho Sync Order thủ công (Auto Sync luôn new: nông ~20 đơn, cứ 10 chu kỳ Lookback). new = list Lookback + gắn đơn mới / cập nhật delta trên pool fulfil trong cửa sổ lead; all = kéo hết list + khớp lại mọi đơn trong lead, thu hồi mã NCC gắn sai và ghi đè giá/tracking/status"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.ConcurrentThreads, "Số luồng lấy chi tiết", "10", "number",
                "Số request get_order_info chạy song song khi Sync Order (1–20)"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.RefreshLookbackDays, "Lookback list (ngày)", "14", "number",
                "Cửa sổ ngày: order/list SHEIN + pool TikTok khi chế độ Cập nhật tất cả (1–60). Không build open-set từ DB"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.RefreshMaxBillnos, "Số billno refresh tối đa", "300", "number",
                "Không còn dùng — detail lấy từ needsDetail sau khi sync list SHEIN"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.CaptureRawPayload, "Lưu JSON gốc SHEIN", "false", "boolean",
                "Chỉ bật khi chẩn đoán: extension gửi kèm JSON gốc từ SHEIN và backend lưu vào link NCC. Làm phình payload mỗi lượt sync"),
            ("Đồng bộ SHEIN (Extension)", ExtensionSheinSyncSettingKeys.ForceBillnos, "Billno ép lấy lại chi tiết", "", "text",
                "Legacy — extension Sync Order không đọc. Để trống. Ép chi tiết dùng endpoint chẩn đoán Dev nếu cần"),
            ("Đồng bộ chi tiết SHEIN (Backend)", SheinDetailSyncSettingKeys.Enabled, "Auto Sync Order detail SHEIN", "false", "boolean",
                "Job nền: mỗi chu kỳ snapshot toàn bộ đơn còn thiếu mã carrier hoặc chưa Mark Ship, gọi get_order_info theo lô, Mark Ship khi có mã, rồi nghỉ. Dùng cookie + header extension trên tài khoản mua hàng — không cần mở trình duyệt"),
            ("Đồng bộ chi tiết SHEIN (Backend)", SheinDetailSyncSettingKeys.IntervalMinutes, "Chu kỳ cập nhật (phút)", "10", "number",
                $"Nghỉ sau khi quét hết danh sách ứng viên trước chu kỳ mới ({SheinDetailSyncSettingKeys.MinIntervalMinutes}–{SheinDetailSyncSettingKeys.MaxIntervalMinutes})"),
            ("Đồng bộ chi tiết SHEIN (Backend)", SheinDetailSyncSettingKeys.MaxBillnos, "Số billno mỗi lượt", "40", "number",
                $"Kích thước mỗi lô trong chu kỳ ({SheinDetailSyncSettingKeys.MinMaxBillnos}–{SheinDetailSyncSettingKeys.MaxMaxBillnos}). Chu kỳ vẫn xử lý hết snapshot"),
            ("Đồng bộ chi tiết SHEIN (Backend)", SheinDetailSyncSettingKeys.MaxMarkShipPerPass, "Số Mark Ship mỗi lượt", "10", "number",
                $"Trần lần gọi 4Seller Mark Ship mỗi lô ({SheinDetailSyncSettingKeys.MinMaxMarkShipPerPass}–{SheinDetailSyncSettingKeys.MaxMaxMarkShipPerPass}). Reset mỗi lô; job nền không bị Cloudflare cắt request"),
            ("Đồng bộ chi tiết SHEIN (Backend)", SheinDetailSyncSettingKeys.NotifyCarrierTracking, "Thông báo Add track", "false", "boolean",
                "Gửi Telegram khi vòng quét nền lần đầu lấy được mã vận đơn thật cho một đơn — kèm kết quả Mark Ship (thành công/thất bại). Gửi sau khi Mark Ship đã chạy"),
            ("Đồng bộ chi tiết SHEIN (Backend)", SheinDetailSyncSettingKeys.TelegramChatId, "Chat ID Add track",
                SheinDetailSyncSettingKeys.DefaultTelegramChatId, "text",
                "Nhóm Telegram nhận thông báo Add track của vòng quét chi tiết SHEIN"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.Enabled, "Tự động đồng bộ profile TikTok Shop", "false", "boolean",
                "Khi bật: extension Auto Sync (chrome.alarms) scrape seller-us + PUT /shops/tiktok-profile theo chu kỳ"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.IntervalValue, "Chu kỳ đồng bộ", "60", "number",
                "Giá trị chu kỳ auto-sync (kết hợp với đơn vị giây/phút); tối thiểu 1"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.IntervalUnit, "Đơn vị chu kỳ", "seconds", "text",
                "Đơn vị chu kỳ auto-sync: seconds hoặc minutes"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.NotifyEmailChange, "Cảnh báo đổi email", "true", "boolean",
                "Gửi Telegram khi email sync khác email đã lưu (không báo lần ghi đầu)"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.NotifyMobileChange, "Cảnh báo đổi mobile", "true", "boolean",
                "Gửi Telegram khi mobile sync khác mobile đã lưu (không báo lần ghi đầu)"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.NotifyBankChange, "Cảnh báo đổi bank", "true", "boolean",
                "Gửi Telegram khi settlement bank sync khác bank đã lưu (không báo lần ghi đầu)"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.NotifyBankLast4Mismatch, "Cảnh báo bank không khớp hệ thống", "true", "boolean",
                "Gửi Telegram khi đuôi 4 số settlement bank không khớp ReceivingBankAccounts nào"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.NotifyStatusChange, "Cảnh báo đổi trạng thái shop", "true", "boolean",
                "Gửi Telegram khi ops Status shop đổi (Active / Deactivated / Suspended) — từ TTS sync hoặc admin/seller chỉnh tay"),
            ("Đồng bộ TikTok Shop (Extension)", ExtensionTikTokShopSyncSettingKeys.TelegramChatId, "Chat ID cảnh báo profile", ExtensionTikTokShopSyncSettingKeys.DefaultTelegramChatId, "text",
                "Chat/group id cho cảnh báo thay đổi email/mobile/bank/trạng thái TikTok Shop"),
            ("Đồng bộ payout TTS (Extension)", ExtensionTikTokPayoutSyncSettingKeys.Enabled, "Tự động đồng bộ payout", "false", "boolean",
                "Khi bật: extension Auto Sync scrape payment list seller-us + POST /shops/tiktok-payouts/sync theo chu kỳ"),
            ("Đồng bộ payout TTS (Extension)", ExtensionTikTokPayoutSyncSettingKeys.IntervalValue, "Chu kỳ đồng bộ", "120", "number",
                "Giá trị chu kỳ auto-sync payout (kết hợp với đơn vị giây/phút); tối thiểu 1"),
            ("Đồng bộ payout TTS (Extension)", ExtensionTikTokPayoutSyncSettingKeys.IntervalUnit, "Đơn vị chu kỳ", "seconds", "text",
                "Đơn vị chu kỳ auto-sync payout: seconds hoặc minutes"),
            ("Đồng bộ payout TTS (Extension)", ExtensionTikTokPayoutSyncSettingKeys.MaxDate, "Ngày tối thiểu lấy dữ liệu", "2026-07-01", "text",
                "ISO yyyy-MM-dd — không kéo payout có success time cũ hơn ngày này (UTC). Để trống = chỉ theo cursor DB"),
            ("Đồng bộ payout TTS (Extension)", ExtensionTikTokPayoutSyncSettingKeys.NotifyNewPayment, "Thông báo thanh toán mới", "true", "boolean",
                "Gửi Telegram khi sync insert payout TikTok mới vào ledger"),
            ("Đồng bộ payout TTS (Extension)", ExtensionTikTokPayoutSyncSettingKeys.TelegramChatId, "Chat ID cảnh báo payout", ExtensionTikTokPayoutSyncSettingKeys.DefaultTelegramChatId, "text",
                "Chat/group id cho thông báo thanh toán mới TikTok Shop"),
            ("Đồng bộ keywords TTS (Extension)", ExtensionTikTokOpportunitySyncSettingKeys.Enabled, "Tự động đồng bộ Product opportunities", "false", "boolean",
                "Khi bật: extension Auto Sync scrape trending keywords seller-us + POST /shops/tiktok-opportunities/sync theo chu kỳ"),
            ("Đồng bộ keywords TTS (Extension)", ExtensionTikTokOpportunitySyncSettingKeys.IntervalValue, "Chu kỳ đồng bộ", "30", "number",
                "Giá trị chu kỳ auto-sync keywords (kết hợp với đơn vị giây/phút); tối thiểu 1"),
            ("Đồng bộ keywords TTS (Extension)", ExtensionTikTokOpportunitySyncSettingKeys.IntervalUnit, "Đơn vị chu kỳ", "minutes", "text",
                "Đơn vị chu kỳ auto-sync keywords: seconds hoặc minutes"),
            ("Sync All TTS (Extension)", ExtensionTikTokUnifiedSyncSettingKeys.StepInfo, "Sync All — Info", "true", "boolean",
                "Khi tắt: Sync All / Sync Fast bỏ qua bước Info (profile). Tách biệt Auto Sync chu kỳ."),
            ("Sync All TTS (Extension)", ExtensionTikTokUnifiedSyncSettingKeys.StepFinance, "Sync All — Finance", "true", "boolean",
                "Khi tắt: Sync All / Sync Fast bỏ qua bước Finance (payout). Tách biệt Auto Sync chu kỳ."),
            ("Sync All TTS (Extension)", ExtensionTikTokUnifiedSyncSettingKeys.StepKeywords, "Sync All — Keywords", "true", "boolean",
                "Khi tắt: Sync All bỏ qua bước Keywords (opportunities). Tách biệt Auto Sync chu kỳ."),
            ("Sync All TTS (Extension)", ExtensionTikTokUnifiedSyncSettingKeys.StepAnalytics, "Sync All — Analytics", "true", "boolean",
                "Khi tắt: Sync All bỏ qua bước Analytics (Compass listing)."),
            ("Sync All TTS (Extension)", ExtensionTikTokUnifiedSyncSettingKeys.StepHealthCenter, "Sync All — Health Center", "true", "boolean",
                "Khi tắt: Sync All / Sync Fast bỏ qua bước Health Center."),
            ("Sync All TTS (Extension)", ExtensionTikTokUnifiedSyncSettingKeys.StepShopScore, "Sync All — Shop Score", "true", "boolean",
                "Khi tắt: Sync All / Sync Fast bỏ qua bước Shop Score."),
            ("Listing Sync — Draft", ListingSyncSettingKeys.DraftEnabled, "Tự động đồng bộ Draft", "true", "boolean",
                "Bật job nền pull listing Draft mới nhất từ 4Seller (page + batch-detail). Chu kỳ tính từ lúc lần sync hoàn tất."),
            ("Listing Sync — Draft", ListingSyncSettingKeys.DraftIntervalMinutes, "Chu kỳ Draft (phút)",
                ListingSyncSettingKeys.DefaultDraftIntervalMinutes.ToString(), "number",
                "Số phút chờ sau khi lần đồng bộ Draft hoàn tất trước lần kế (tối thiểu 1)"),
            ("Listing Sync — Published", ListingSyncSettingKeys.PublishedEnabled, "Tự động đồng bộ Published", "true", "boolean",
                "Bật job nền refresh trạng thái listing Published (active/pending/…) theo cửa sổ ngày tạo. Chu kỳ tính từ lúc lần sync hoàn tất."),
            ("Listing Sync — Published", ListingSyncSettingKeys.PublishedIntervalMinutes, "Chu kỳ Published (phút)",
                ListingSyncSettingKeys.DefaultPublishedIntervalMinutes.ToString(), "number",
                "Số phút chờ sau khi lần đồng bộ Published hoàn tất trước lần kế (tối thiểu 1)"),
            ("Listing Sync — Published", ListingSyncSettingKeys.PublishedLookbackDays, "Lookback Published (ngày)",
                ListingSyncSettingKeys.DefaultPublishedLookbackDays.ToString(), "number",
                "Số ngày VN (kể cả hôm nay) quét lại trạng thái listing theo createTime 4Seller"),
            ("Product Discount Sync", ProductDiscountSyncSettingKeys.Enabled, "Tự động đồng bộ Product Discount", "true", "boolean",
                "Bật job nền trên API server đồng bộ product-discount activity từ 4Seller (shop Active). Chu kỳ tính từ lúc lần sync tự động hoàn tất."),
            ("Product Discount Sync", ProductDiscountSyncSettingKeys.IntervalHours, "Chu kỳ cập nhật (giờ)", "2", "number",
                "Số giờ chờ sau khi lần đồng bộ Product Discount tự động hoàn tất trước lần kế (tối thiểu 1)"),
            ("Flash Deal Sync", FlashDealSyncSettingKeys.Enabled, "Tự động đồng bộ Flash Deal", "true", "boolean",
                "Bật job nền đồng bộ Flash Deal (FLASHSALE) từ 4Seller (shop Active). Chu kỳ tính từ lúc lần sync tự động hoàn tất."),
            ("Flash Deal Sync", FlashDealSyncSettingKeys.IntervalHours, "Chu kỳ cập nhật (giờ)", "2", "number",
                "Số giờ chờ sau khi lần đồng bộ Flash Deal tự động hoàn tất trước lần kế (tối thiểu 1)"),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.Enabled, "Bật auto publish toàn hệ thống",
                ListingAutoPublishSettingKeys.DefaultEnabled, "boolean",
                "Khi bật: scheduler tự publish draft listing lên 4Seller (cần shop bật riêng)"),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.SchedulerIntervalMinutes, "Chu kỳ scheduler (phút)",
                ListingAutoPublishSettingKeys.DefaultSchedulerIntervalMinutes, "number",
                "Khoảng cách giữa các lần scheduler chạy khi bật (tối thiểu 1)"),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.MaxConcurrentPublishes,
                "Số publish song song (toàn VPS)",
                ListingAutoPublishSettingKeys.DefaultMaxConcurrentPublishes, "number",
                "Số shop publish đồng thời tối đa trên VPS — mỗi shop vẫn tuân interval riêng"),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.DefaultIntervalSeconds,
                "Khoảng cách đăng mặc định (giây)",
                ListingAutoPublishSettingKeys.DefaultIntervalSecondsValue, "number",
                "Mặc định thời gian giữa các lần auto publish per shop (shop có thể override)"),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes,
                "Thời gian chờ draft mặc định (phút)",
                ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutesValue, "number",
                "Mặc định thời gian tối thiểu draft phải tồn tại trước khi auto publish pick (shop có thể override)"),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.LogRetentionDays, "Giữ log auto publish (ngày)",
                ListingAutoPublishSettingKeys.DefaultLogRetentionDays, "number",
                "Tự xóa ListingAutoPublishRuns cũ hơn N ngày"),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.CategoryVerifyEnabled,
                "Đối chiếu category bằng AI ở B2",
                ListingAutoPublishSettingKeys.DefaultCategoryVerifyEnabled, "boolean",
                "Bật (mặc định): B2 luôn gọi AI 4Seller để đối chiếu category mà 4Seller gán khi crawl, "
                + "và ghi đè khi category đó thuộc nhánh rủi ro. Tắt: giữ nguyên category có sẵn."),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.CategoryRiskyAncestorIds,
                "Category cha rủi ro (external id, phẩy)",
                ListingAutoPublishSettingKeys.DefaultCategoryRiskyAncestorIds, "text",
                "Listing rơi vào nhánh này sẽ bị ghi đè bằng category AI nếu title không có từ khoá costume. "
                + "Mặc định 843656 (Costumes & Accessories) — nhánh có enum thuộc tính riêng khiến TikTok "
                + "từ chối Specifics của hàng thời trang thường."),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.CategoryRiskyNameKeywords,
                "Từ khoá tên category rủi ro (phẩy)",
                ListingAutoPublishSettingKeys.DefaultCategoryRiskyNameKeywords, "text",
                "Tên category chứa các từ này cũng bị coi là nhánh rủi ro, kể cả khi chưa sync path."),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.CategoryRiskyTitleKeywords,
                "Từ khoá title xác nhận hàng costume (phẩy)",
                ListingAutoPublishSettingKeys.DefaultCategoryRiskyTitleKeywords, "text",
                "Title chứa các từ này thì giữ category nhánh costume, không ghi đè. Chỉ dùng từ gọi tên "
                + "loại hàng (costume, cosplay); từ chỉ dịp như halloween/carnival là câu quảng cáo, "
                + "xuất hiện cả trên áo thường nên không tính."),
            ("Listing Auto Publish", ListingAutoPublishSettingKeys.CategoryBlockedRemap,
                "Category leaf bị chặn → leaf thay thế (id:id, phẩy)",
                ListingAutoPublishSettingKeys.DefaultCategoryBlockedRemap, "text",
                "Cặp blockedId:replacementId. Mặc định 903824:903952 — Costume Tops & Corsets bị TikTok "
                + "từ chối Specifics 100401 (Washing instructions) trên một số shop dù payload đã có "
                + "valueId; Costume Sets nhận đúng giá trị đó. Áp dụng ở AI recommend thủ công và B2 "
                + "Auto Publish (kể cả khi AI tắt / lỗi)."),
            ("Listing Rating", ListingRatingSettingKeys.SuccessMin, "Ngưỡng sao tốt (xanh)", "4.4", "number",
                "Rating ≥ ngưỡng này → ngôi sao màu success (xanh). Mặc định 4.4"),
            ("Listing Rating", ListingRatingSettingKeys.WarningMin, "Ngưỡng sao cảnh báo (vàng/đỏ)", "4", "number",
                "Rating ≥ ngưỡng này và < ngưỡng tốt → vàng; dưới ngưỡng này → đỏ. Mặc định 4.0"),
            ("Listing Rewrite", ListingRewriteSettingKeys.TitleMode, "Chế độ tiêu đề khi Rewrite",
                ListingRewriteSettingKeys.DefaultTitleMode, "text",
                "strip_brands = AI xóa từ thương hiệu, giữ wording gốc; nếu tiêu đề còn dưới 40 ký tự thì tạo SEO mới (Women …, keyword đồng bộ mô tả); rewrite = AI viết lại Machine-first SEO 150–250 ký tự từ title gốc + synonym cùng món (không bịa thuộc tính)"),
            ("Listing Rewrite", ListingRewriteSettingKeys.AllowRepeat, "Cho phép Rewrite lại",
                ListingRewriteSettingKeys.DefaultAllowRepeat, "boolean",
                "Tắt (mặc định): khóa nút Rewrite sau lần đầu, dùng Hoàn tác nếu muốn viết lại. Bật: cho phép bấm Rewrite lại khi đã có tiêu đề/mô tả AI (tốn thêm token)."),
            ("Listing Specifics", ListingSpecificsSettingKeys.AllowRepeat, "Cho phép tạo lại Specifics",
                ListingSpecificsSettingKeys.DefaultAllowRepeat, "boolean",
                "Tắt (mặc định): khóa nút Auto Specifics khi đã map Specifics (ngoài Brand/cert). Bật: cho phép chạy Auto Specifics lại (tốn thêm token). Background auto vẫn chỉ chạy 1 lần khi Specifics trống."),
            ("Listing Size Chart", ListingSizeChartSettingKeys.AutoGenerateEnabled,
                "Tạo ảnh Size Chart tự động",
                ListingSizeChartSettingKeys.DefaultAutoGenerateEnabled, "boolean",
                "Bật nút Grok AI trên trang sửa listing (admin + seller)."),
            ("Telegram", TelegramSettingKeys.NotifyNewOrder, "Thông báo đơn hàng mới", "true", "boolean",
                "Gửi Telegram khi sync phát hiện đơn mới (nhóm báo đơn)"),
            ("Telegram", TelegramSettingKeys.NotifyCancelRefund, "Thông báo hủy/refund", "true", "boolean",
                "Gửi Telegram khi sync phát hiện đơn hủy hoặc refund (nhóm xử lý đơn)"),
            ("Telegram", TelegramSettingKeys.OrdersChatId, "Chat ID báo đơn / summary", "-1004406324922", "text",
                "Chat/group id cho đơn mới + tổng hợp đơn ngày + tổng hợp Seller. Để trống → dùng Telegram:ChatId trong appsettings"),
            ("Telegram", TelegramSettingKeys.CancelRefundChatId, "Chat ID xử lý đơn", "-1004462111161", "text",
                "Chat/group id cho hủy/refund + USPS + track kho + tổng hợp fulfil + thông báo message + đơn N giờ chưa track"),
            ("Telegram", TelegramSettingKeys.DailySummaryEnabled, "Tổng hợp đơn trong ngày", "true", "boolean",
                "Gửi tổng hợp trạng thái đơn theo giờ hẹn (Asia/Ho_Chi_Minh)"),
            ("Telegram", TelegramSettingKeys.DailySummaryHour, "Giờ đẩy tổng hợp đơn", "08:00", "text",
                "Giờ local HH:mm (múi giờ Việt Nam), ví dụ 08:00 hoặc 23:59 — gửi tổng hợp trạng thái đơn"),
            ("Telegram", TelegramSettingKeys.DailySummaryLastSentDate, "Ngày đã gửi tổng hợp", "", "text",
                "Nội bộ: yyyy-MM-dd lần gửi tổng hợp gần nhất (tránh gửi trùng)"),
            ("Telegram", TelegramSettingKeys.SellerSummaryEnabled, "Tổng hợp Seller trong ngày", "true", "boolean",
                "Gửi Listed/Order/Revenue theo từng user có role Seller (stats theo OwnerId) theo giờ hẹn"),
            ("Telegram", TelegramSettingKeys.SellerSummaryHour, "Giờ đẩy tổng hợp Seller", "08:00", "text",
                "Giờ local HH:mm (múi giờ Việt Nam), ví dụ 08:00 hoặc 23:59 — gửi tổng hợp Seller"),
            ("Telegram", TelegramSettingKeys.SellerSummaryLastSentDate, "Ngày đã gửi tổng hợp Seller", "", "text",
                "Nội bộ: yyyy-MM-dd lần gửi tổng hợp Seller gần nhất (tránh gửi trùng)"),
            ("Telegram", TelegramSettingKeys.FulfilSummaryEnabled, "Tổng hợp xử lý đơn hàng", "true", "boolean",
                "Gửi tổng hợp Đã mua / Chờ mua / 12h theo seller + người xử lý theo giờ hẹn (nhóm xử lý đơn)"),
            ("Telegram", TelegramSettingKeys.FulfilSummaryHour, "Giờ đẩy tổng hợp xử lý đơn", "08:00", "text",
                "Giờ local HH:mm (múi giờ Việt Nam) — gửi tổng hợp xử lý đơn hàng"),
            ("Telegram", TelegramSettingKeys.FulfilSummaryLastSentDate, "Ngày đã gửi tổng hợp xử lý đơn", "", "text",
                "Nội bộ: yyyy-MM-dd lần gửi tổng hợp xử lý đơn gần nhất (tránh gửi trùng)"),
            ("Telegram", TelegramSettingKeys.NotifyUsps, "Thông báo USPS cần xử lý", "true", "boolean",
                "Gửi Telegram khi đơn lần đầu vào hàng đợi USPS (BuyTrack) — nhóm xử lý đơn"),
            ("Telegram", TelegramSettingKeys.NotifyWarehouseTrack, "Thông báo track kho Shein", "true", "boolean",
                "Gửi Telegram khi lần đầu thấy mã track kho Shein (C…/N… / warehouse) — nhóm xử lý đơn"),
            ("Telegram", TelegramSettingKeys.NotifyOrderMessage, "Thông báo xử lý đơn (message)", "true", "boolean",
                "Gửi Telegram khi Lưu Thông báo trên đơn (hết size, giá cao…) — nhóm xử lý đơn"),
            ("Telegram", TelegramSettingKeys.NotifyStaleNoTracking, "Thông báo đơn N giờ chưa track", "true", "boolean",
                "Gửi danh sách đơn tab Đã mua quá N giờ chưa có tracking thật (không gồm hoàn/hủy) — cùng giờ tổng hợp xử lý đơn, nhóm xử lý đơn"),
            ("Telegram", TelegramSettingKeys.NotifyStaleNoTrackingHours, "Số giờ chưa có tracking thật",
                TelegramSettingKeys.DefaultStaleNoTrackingHours.ToString(), "number",
                "Số giờ kể từ lúc mua hàng trước khi đưa vào digest. Mặc định 72"),
            ("Telegram", TelegramSettingKeys.NotifyStaleNoTrackingLastSentDate, "Ngày đã gửi đơn N giờ chưa track", "", "text",
                "Nội bộ: yyyy-MM-dd lần gửi digest đơn chưa track gần nhất (tránh gửi trùng)"),
            ("4Seller Proxy", FourSellerProxySettingKeys.Enabled, "Bật pool proxy dự phòng", "false", "boolean",
                "Khi bật, seller chưa có proxy riêng sẽ dùng pool dự phòng. Proxy gắn trên từng tài khoản luôn được dùng (không phụ thuộc toggle này)"),
            ("4Seller Proxy", FourSellerProxySettingKeys.Pool, "Danh sách proxy (pool)", "", "text",
                "Mỗi dòng 1 proxy dự phòng: ip:port:user:pass hoặc ip:port (whitelist IP); thêm socks5:// để dùng SOCKS5"),
            ("4Seller Proxy", FourSellerProxySettingKeys.CooldownMinutes, "Cooldown khi bị chặn (phút)", "10", "number",
                "Số phút tạm loại 1 proxy khỏi vòng xoay sau khi bị 4Seller chặn (567); tối thiểu 1"),
            ("4Seller Proxy", FourSellerSessionSettingKeys.ValidateTtlMinutes, "TTL cache is-login (phút)",
                FourSellerSessionSettingKeys.DefaultValidateTtlMinutes.ToString(), "number",
                "Số phút tái sử dụng kết quả is-login thành công trước khi probe lại (giảm burst). Tối thiểu 1; mặc định 15"),
            ("Phí xử lý đơn", OrderHandlingSettingKeys.BuyTrack, "Phí BuyTrack (USD)", "0.5", "number",
                "Phí mua scan track USPS — cộng vào chi phí mua hàng khi đánh dấu đã xử lý USPS"),
            ("Phí xử lý đơn", OrderHandlingSettingKeys.ThankCard, "Phí ThankCard (USD)", "1", "number",
                "Phí mua thank card gửi khách — cộng vào chi phí mua hàng khi đánh dấu đã mua ThankCard"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.AutoEnabled, "Tự động đẩy track scan", "false", "boolean",
                "Bật BackgroundService đẩy USPS track lên PrintPackShip theo khung giờ làm việc US + tuổi tối thiểu"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.DelayHours, "Tuổi tối thiểu của mã VC (giờ)",
                UspsScanAutoSettingKeys.DefaultDelayHours.ToString(), "number",
                $"Chỉ đẩy đơn đủ tuổi kể từ TrackingAddedAt (không tính từ ngày tạo đơn) — chặn track vừa thêm; giờ đẩy do khung giờ US quyết định. 1–168; mặc định {UspsScanAutoSettingKeys.DefaultDelayHours}"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.ChunkSize, "Số mã mỗi lần gửi",
                UspsScanAutoSettingKeys.DefaultChunkSize.ToString(), "number",
                "Mỗi chu kỳ lấy hết track đủ điều kiện. N>0: gửi từng gói N mã; N=0: gửi tất cả một lần (vẫn chia theo MaxTrackingPerBatch nếu vượt). 0–100; mặc định 10"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.IntervalMinutes, "Khoảng chờ giữa các chu kỳ (phút)",
                UspsScanAutoSettingKeys.DefaultIntervalMinutes.ToString(), "number",
                "Sau khi gửi hết các gói mới đếm N phút rồi quét lại. 1–1440; mặc định 10"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.WindowEnabled, "Chỉ đẩy trong giờ làm việc US", "true", "boolean",
                "Nhân viên bưu điện Mỹ chỉ scan trong giờ hành chính. Tắt = đẩy suốt ngày đêm (chỉ còn tuổi tối thiểu)"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.WeekdayStart, "T2–T6 mở cửa (HH:mm, giờ US Miền Đông)",
                UspsScanWindow.FormatClock(UspsScanWindow.DefaultWeekdayStart), "text",
                "Giờ bắt đầu đẩy T2–T6 theo America/New_York (tự khớp DST). 08:00 ET ≈ 20:00 VN mùa đông, 19:00 VN mùa hè"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.WeekdayEnd, "T2–T6 đóng cửa (HH:mm, giờ US Miền Đông)",
                UspsScanWindow.FormatClock(UspsScanWindow.DefaultWeekdayEnd), "text",
                "Giờ hết đẩy T2–T6 theo America/New_York. 17:00 ET ≈ 05:00 VN hôm sau mùa đông, 04:00 VN mùa hè"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.SaturdayEnabled, "Đẩy cả sáng thứ 7", "true", "boolean",
                "Bưu điện Mỹ làm sáng thứ 7. Chủ nhật luôn nghỉ, không đẩy"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.SaturdayStart, "Thứ 7 mở cửa (HH:mm, giờ US Miền Đông)",
                UspsScanWindow.FormatClock(UspsScanWindow.DefaultSaturdayStart), "text",
                "Giờ bắt đầu đẩy thứ 7 theo America/New_York"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.SaturdayEnd, "Thứ 7 đóng cửa (HH:mm, giờ US Miền Đông)",
                UspsScanWindow.FormatClock(UspsScanWindow.DefaultSaturdayEnd), "text",
                "Giờ hết đẩy thứ 7 theo America/New_York. 12:00 ET ≈ 00:00 CN VN mùa đông, 23:00 T7 VN mùa hè"),
            ("USPS Track Scan", UspsScanAutoSettingKeys.CutoffMarginMinutes, "Đệm trước giờ đóng cửa (phút)",
                UspsScanAutoSettingKeys.DefaultCutoffMarginMinutes.ToString(), "number",
                $"Ngừng đẩy N phút trước giờ đóng để lô cuối kịp được scan; cũng cắt các gói còn lại nếu chu kỳ chạy quá giờ. 0–240; mặc định {UspsScanAutoSettingKeys.DefaultCutoffMarginMinutes}"),
            ("Fulfil Claim", FulfilClaimSettingKeys.TtlMinutes, "TTL giữ đơn (phút)", FulfilClaimSettingKeys.DefaultTtlMinutes, "number",
                $"Thời gian lease khi fulfil click vào đơn ({FulfilClaimSettingKeys.MinTtlMinutes}–{FulfilClaimSettingKeys.MaxTtlMinutes}). Heartbeat 5 phút gia hạn."),
            ("Fulfil Claim", FulfilClaimSettingKeys.MaxPerUser, "Số đơn tối đa mỗi fulfil", FulfilClaimSettingKeys.DefaultMaxPerUser, "number",
                "Trần số đơn đang giữ (đếm theo đơn, không theo phiên Chrome). Chặn vét cả hàng đợi."),
            (DataRetentionSettingKeys.Group, DataRetentionSettingKeys.AnalyticsSnapshotDays,
                "Giữ snapshot Compass (ngày)",
                DataRetentionSettingKeys.DefaultAnalyticsSnapshotDays, "number",
                "Xoá ListingAnalyticsSnapshots cũ hơn N ngày VN. UI chỉ vẽ 90 ngày. 0 = không dọn"),
            (DataRetentionSettingKeys.Group, DataRetentionSettingKeys.TikTokSyncLogDays,
                "Giữ nhật ký sync TikTok Shop (ngày)",
                DataRetentionSettingKeys.DefaultTikTokSyncLogDays, "number",
                "Xoá TikTokShopSyncLogs cũ hơn N ngày. Chỉ dùng để chẩn đoán lượt vừa chạy. 0 = không dọn"),
            (DataRetentionSettingKeys.Group, DataRetentionSettingKeys.RefreshTokenDays,
                "Giữ refresh token đã hết hạn (ngày)",
                DataRetentionSettingKeys.DefaultRefreshTokenDays, "number",
                "Xoá RefreshTokens đã revoke hoặc đã hết hạn quá N ngày. Token đang sống không bị chạm. 0 = không dọn"),
            (DataRetentionSettingKeys.Group, DataRetentionSettingKeys.ShopOnlineSessionDays,
                "Giữ lịch sử online shop (ngày)",
                DataRetentionSettingKeys.DefaultShopOnlineSessionDays, "number",
                "Xoá ShopOnlineSessions cũ hơn N ngày (theo EndedAt/LastSeenAt). 0 = không dọn"),
        };

        foreach (var (group, key, label, value, type, description) in defaults)
        {
            var existing = await context.Settings.FirstOrDefaultAsync(s => s.Key == key);
            if (existing is null)
            {
                context.Settings.Add(new Setting
                {
                    Group = group,
                    Key = key,
                    Label = label,
                    Value = value,
                    Type = type,
                    Description = description
                });
                continue;
            }

            // Một lần / idempotent: nâng telegram.*.hour từ số 0–23 → HH:mm (text).
            if (key is TelegramSettingKeys.DailySummaryHour
                or TelegramSettingKeys.SellerSummaryHour
                or TelegramSettingKeys.FulfilSummaryHour)
            {
                existing.Type = "text";
                existing.Label = label;
                existing.Description = description;
                var raw = existing.Value?.Trim() ?? string.Empty;
                if (int.TryParse(raw, out var legacyHour) && legacyHour is >= 0 and <= 23)
                {
                    existing.Value = $"{legacyHour:D2}:00";
                }
            }

            // Refresh label/desc for Telegram routing rename (orders vs ops chat).
            if (key is TelegramSettingKeys.OrdersChatId
                or TelegramSettingKeys.CancelRefundChatId
                or TelegramSettingKeys.NotifyNewOrder
                or TelegramSettingKeys.NotifyCancelRefund
                or TelegramSettingKeys.NotifyUsps
                or TelegramSettingKeys.NotifyWarehouseTrack
                or TelegramSettingKeys.NotifyStaleNoTracking
                or TelegramSettingKeys.NotifyStaleNoTrackingHours
                or TelegramSettingKeys.FulfilSummaryEnabled)
            {
                existing.Label = label;
                existing.Description = description;
                existing.Group = group;
            }

            // Refresh label/desc after USPS auto scan moved from pure delay → US business-hours window.
            // Value is left alone: DB rows seeded with the old 12h default stay put until admin lowers it.
            if (key is UspsScanAutoSettingKeys.AutoEnabled
                or UspsScanAutoSettingKeys.DelayHours
                or UspsScanAutoSettingKeys.ChunkSize
                or UspsScanAutoSettingKeys.IntervalMinutes)
            {
                existing.Label = label;
                existing.Description = description;
                existing.Group = group;
            }

            if (key is ListingRewriteSettingKeys.TitleMode or ListingRewriteSettingKeys.AllowRepeat
                or ListingSpecificsSettingKeys.AllowRepeat
                or ListingSizeChartSettingKeys.AutoGenerateEnabled
                or ListingAutoPublishSettingKeys.Enabled
                or ListingAutoPublishSettingKeys.SchedulerIntervalMinutes
                or ListingAutoPublishSettingKeys.MaxConcurrentPublishes
                or ListingAutoPublishSettingKeys.DefaultIntervalSeconds
                or ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes
                or ListingAutoPublishSettingKeys.LogRetentionDays
                or ListingAutoPublishSettingKeys.CategoryVerifyEnabled
                or ListingAutoPublishSettingKeys.CategoryRiskyAncestorIds
                or ListingAutoPublishSettingKeys.CategoryRiskyNameKeywords
                or ListingAutoPublishSettingKeys.CategoryRiskyTitleKeywords
                or ListingAutoPublishSettingKeys.CategoryBlockedRemap)
            {
                existing.Label = label;
                existing.Description = description;
                existing.Group = group;
            }

            // Group cơ bản -5204324132 đã upgrade → supergroup; giữ ID cũ khiến sendMessage 400
            // ("group chat was upgraded") và nuốt toàn bộ thông báo đơn mới + summary.
            // Rỗng / ID chết → nhóm báo đơn -1004406324922 (đơn mới + daily/seller summary).
            if (key == TelegramSettingKeys.OrdersChatId)
            {
                var current = existing.Value?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(current)
                    || string.Equals(current, "-5204324132", StringComparison.Ordinal))
                {
                    existing.Value = "-1004406324922";
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// One-shot: copy the old single-loop order-sync settings into the two new loops, then
    /// remove the three legacy rows. Idempotent — no legacy rows means no-op.
    /// </summary>
    private static async Task MigrateLegacyOrderSyncSettingsAsync(ApplicationDbContext context)
    {
        var legacyKeys = new[]
        {
            OrderSyncSettingKeys.LegacyEnabled,
            OrderSyncSettingKeys.LegacyIntervalMinutes,
            OrderSyncSettingKeys.LegacyLookbackDays
        };

        var legacy = await context.Settings
            .Where(s => legacyKeys.Contains(s.Key))
            .ToListAsync();
        if (legacy.Count == 0)
        {
            return;
        }

        string? ValueOf(string key) =>
            legacy.FirstOrDefault(s => s.Key == key)?.Value;

        var legacyEnabled = ValueOf(OrderSyncSettingKeys.LegacyEnabled);
        var legacyInterval = ValueOf(OrderSyncSettingKeys.LegacyIntervalMinutes);

        var targets = await context.Settings
            .Where(s =>
                s.Key == OrderSyncSettingKeys.NewEnabled
                || s.Key == OrderSyncSettingKeys.StatusEnabled
                || s.Key == OrderSyncSettingKeys.NewIntervalMinutes)
            .ToListAsync();

        void CopyIfPresent(string key, string? value)
        {
            if (value is null)
            {
                return;
            }

            var row = targets.FirstOrDefault(s => s.Key == key);
            if (row is null)
            {
                return;
            }

            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }

        // Both loops inherit the old on/off so a previously-disabled deploy stays quiet.
        CopyIfPresent(OrderSyncSettingKeys.NewEnabled, legacyEnabled);
        CopyIfPresent(OrderSyncSettingKeys.StatusEnabled, legacyEnabled);
        // Only the light loop inherits the old minute interval; status defaults to 12 hours.
        CopyIfPresent(OrderSyncSettingKeys.NewIntervalMinutes, legacyInterval);

        context.Settings.RemoveRange(legacy);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// One-shot: copy the old single-loop listing-sync settings into the two new loops, then
    /// remove the four legacy rows. Idempotent — no legacy rows means no-op.
    /// </summary>
    private static async Task MigrateLegacyListingSyncSettingsAsync(ApplicationDbContext context)
    {
        var legacyKeys = new[]
        {
            ListingSyncSettingKeys.LegacyEnabled,
            ListingSyncSettingKeys.LegacyIntervalMinutes,
            ListingSyncSettingKeys.LegacySyncDraft,
            ListingSyncSettingKeys.LegacySyncPublished
        };

        var legacy = await context.Settings
            .Where(s => legacyKeys.Contains(s.Key))
            .ToListAsync();
        if (legacy.Count == 0)
        {
            return;
        }

        string? ValueOf(string key) =>
            legacy.FirstOrDefault(s => s.Key == key)?.Value;

        static bool IsTruthy(string? raw, bool defaultWhenMissing = true) =>
            string.IsNullOrWhiteSpace(raw)
                ? defaultWhenMissing
                : string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";

        var legacyEnabled = IsTruthy(ValueOf(ListingSyncSettingKeys.LegacyEnabled));
        var legacySyncDraft = IsTruthy(ValueOf(ListingSyncSettingKeys.LegacySyncDraft));
        var legacySyncPublished = IsTruthy(ValueOf(ListingSyncSettingKeys.LegacySyncPublished));
        var legacyInterval = ValueOf(ListingSyncSettingKeys.LegacyIntervalMinutes);

        var targets = await context.Settings
            .Where(s =>
                s.Key == ListingSyncSettingKeys.DraftEnabled
                || s.Key == ListingSyncSettingKeys.DraftIntervalMinutes
                || s.Key == ListingSyncSettingKeys.PublishedEnabled)
            .ToListAsync();

        void SetIfPresent(string key, string value)
        {
            var row = targets.FirstOrDefault(s => s.Key == key);
            if (row is null)
            {
                return;
            }

            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }

        // Each loop inherits master ∧ its own toggle so a previously-disabled deploy stays quiet.
        SetIfPresent(
            ListingSyncSettingKeys.DraftEnabled,
            legacyEnabled && legacySyncDraft ? "true" : "false");
        SetIfPresent(
            ListingSyncSettingKeys.PublishedEnabled,
            legacyEnabled && legacySyncPublished ? "true" : "false");
        // Only the light draft loop inherits the old minute interval; published keeps default 60.
        if (legacyInterval is not null)
        {
            SetIfPresent(ListingSyncSettingKeys.DraftIntervalMinutes, legacyInterval);
        }

        context.Settings.RemoveRange(legacy);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Một lần: đảo giá trị <c>local</c> ↔ <c>realtime</c> cho khớp nghĩa mới
    /// (realtime = extension, local = backend+proxy). Chạy trước <see cref="SeedSettingsAsync"/>.
    /// </summary>
    private static async Task MigrateSheinHeaderSourceSemanticsAsync(ApplicationDbContext context)
    {
        var marker = await context.Settings.FirstOrDefaultAsync(
            s => s.Key == ExtensionSheinSyncSettingKeys.HeaderSourceRenamedMarker);
        if (marker is not null && marker.Value == "done")
        {
            return;
        }

        var setting = await context.Settings.FirstOrDefaultAsync(
            s => s.Key == ExtensionSheinSyncSettingKeys.HeaderSource);
        if (setting is not null)
        {
            if (string.Equals(setting.Value, ExtensionSheinSyncSettingKeys.HeaderSourceLocal, StringComparison.OrdinalIgnoreCase))
            {
                setting.Value = ExtensionSheinSyncSettingKeys.HeaderSourceRealtime;
            }
            else if (string.Equals(setting.Value, ExtensionSheinSyncSettingKeys.HeaderSourceRealtime, StringComparison.OrdinalIgnoreCase))
            {
                setting.Value = ExtensionSheinSyncSettingKeys.HeaderSourceLocal;
            }

            setting.Label = "Nguồn đồng bộ";
            setting.Description =
                "realtime = extension gọi SHEIN trên trình duyệt; local = backend gọi SHEIN bằng cookie+proxy đã lưu trên tài khoản mua hàng";
        }

        if (marker is null)
        {
            context.Settings.Add(new Setting
            {
                Group = "Bảo trì dữ liệu",
                Key = ExtensionSheinSyncSettingKeys.HeaderSourceRenamedMarker,
                Label = "Đã đổi nghĩa Local/Realtime SHEIN sync",
                Value = "done",
                Type = "text",
                Description = "Nội bộ: đánh dấu đã flip extension.shein_sync.header_source (v1)."
            });
        }
        else
        {
            marker.Value = "done";
        }

        await context.SaveChangesAsync();
    }

    private static async Task AssignPermissionsToRoleAsync(
        ApplicationDbContext context,
        RoleManager<ApplicationRole> roleManager,
        string roleName,
        IReadOnlyList<Guid> permissionIds)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            return;
        }

        foreach (var permissionId in permissionIds)
        {
            var exists = await context.RolePermissions.AnyAsync(rp =>
                rp.RoleId == role.Id && rp.PermissionId == permissionId);

            if (!exists)
            {
                context.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permissionId
                });
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager)
    {
        const string adminUsername = "admin";
        const string adminPassword = "Admin@123";
        const string adminEmail = "admin@dropship.local";

        var adminUser = await userManager.FindByNameAsync(adminUsername);
        if (adminUser is not null)
        {
            return;
        }

        adminUser = new ApplicationUser
        {
            UserName = adminUsername,
            NormalizedUserName = adminUsername.ToUpperInvariant(),
            Email = adminEmail,
            NormalizedEmail = adminEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = "System Administrator",
            IsActive = true
        };

        var result = await userManager.CreateAsync(adminUser, adminPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(adminUser, RoleNames.Admin);
    }

    private static async Task SyncAdminRolePermissionsAsync(
        ApplicationDbContext context,
        RoleManager<ApplicationRole> roleManager)
    {
        var allPermissionIds = await context.Permissions.Select(p => p.Id).ToListAsync();
        await AssignPermissionsToRoleAsync(context, roleManager, RoleNames.Admin, allPermissionIds);
    }

    private static async Task SyncSellerRolePermissionsAsync(
        ApplicationDbContext context,
        RoleManager<ApplicationRole> roleManager)
    {
        var role = await roleManager.FindByNameAsync(RoleNames.Seller);
        if (role is null)
        {
            return;
        }

        var managePermission = await context.Permissions
            .FirstOrDefaultAsync(p => p.Code == "seller.shops.manage");
        if (managePermission is null)
        {
            return;
        }

        var staleAssignment = await context.RolePermissions.FirstOrDefaultAsync(rp =>
            rp.RoleId == role.Id && rp.PermissionId == managePermission.Id);

        if (staleAssignment is not null)
        {
            context.RolePermissions.Remove(staleAssignment);
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedSellerUserAsync(UserManager<ApplicationUser> userManager)
    {
        const string sellerUsername = "seller";
        const string sellerPassword = "Seller@123";
        const string sellerEmail = "seller@dropship.local";

        var sellerUser = await userManager.FindByNameAsync(sellerUsername);
        if (sellerUser is not null)
        {
            return;
        }

        sellerUser = new ApplicationUser
        {
            UserName = sellerUsername,
            NormalizedUserName = sellerUsername.ToUpperInvariant(),
            Email = sellerEmail,
            NormalizedEmail = sellerEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = "Demo Seller",
            IsActive = true
        };

        var result = await userManager.CreateAsync(sellerUser, sellerPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create seller user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(sellerUser, RoleNames.Seller);
    }
}
