using System.Reflection;
using Application.Interfaces;
using Application.Services;
using Application.Services.Shein;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

/// <summary>
/// Application layer dependency injection extensions.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers application services.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddAutoMapper(assembly);
        services.AddValidatorsFromAssembly(assembly);
        services.AddMemoryCache();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IShopService, ShopService>();
        services.AddScoped<IShopOwnershipTransferService, ShopOwnershipTransferService>();
        services.AddScoped<IShopFinanceService, ShopFinanceService>();
        services.AddScoped<ICompanyLedgerService, CompanyLedgerService>();
        services.AddScoped<IAgeRangeService, AgeRangeService>();
        services.AddScoped<ITikTokPayoutSyncService, TikTokPayoutSyncService>();
        services.AddScoped<ITikTokShopSyncLogWriter, TikTokShopSyncLogWriter>();
        services.AddScoped<ITikTokShopSyncLogService, TikTokShopSyncLogService>();
        services.AddScoped<IShopTaskService, ShopTaskService>();
        services.AddScoped<IShopOnlineSessionService, ShopOnlineSessionService>();
        services.AddScoped<IProductOpportunityService, ProductOpportunityService>();
        services.AddScoped<IShopOperationalPeriodService, ShopOperationalPeriodService>();
        services.AddScoped<IPlatformService, PlatformService>();
        services.AddScoped<IProductSourceService, ProductSourceService>();
        services.AddScoped<IVariationSpecTemplateService, VariationSpecTemplateService>();
        services.AddScoped<IShippingTemplateService, ShippingTemplateService>();
        services.AddScoped<IPricingTemplateService, PricingTemplateService>();
        services.AddScoped<ISettingService, SettingService>();
        services.AddScoped<IDataRetentionService, DataRetentionService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductScrapeService, ProductScrapeService>();
        services.AddScoped<IListingImportService, ListingImportService>();
        services.AddScoped<IOrderSyncService, OrderSyncService>();
        services.AddScoped<IUspsScanPushService, UspsScanPushService>();
        services.AddScoped<IUspsScanAutoActingUserResolver, UspsScanAutoActingUserResolver>();
        services.AddScoped<IUspsScanAutoPushOrchestrator, UspsScanAutoPushOrchestrator>();
        services.AddScoped<IOrderAfterSalesSyncService, OrderAfterSalesSyncService>();
        services.AddScoped<IOrderWorkflowSignalsVerifier, OrderWorkflowSignalsVerifier>();
        services.AddSingleton<IFulfilSessionTracker, FulfilSessionTracker>();
        services.AddScoped<IOrderIssueTagService, OrderIssueTagService>();
        services.AddScoped<IFulfilQueueRealtimePublisher, NullFulfilQueueRealtimePublisher>();
        services.AddHostedService<FulfilClaimExpirySweepService>();
        services.AddScoped<ISheinRealtimeSyncService, SheinRealtimeSyncService>();
        services.AddScoped<ISheinOrderDetailSweepService, SheinOrderDetailSweepService>();
        services.AddScoped<IListingSyncService, ListingSyncService>();
        services.AddScoped<IProductDiscountSyncService, ProductDiscountSyncService>();
        services.AddSingleton<IProductDiscountSyncJobRunner, ProductDiscountSyncJobRunner>();
        services.AddScoped<IListingAnalyticsService, ListingAnalyticsService>();
        services.AddScoped<ITikTokShopHealthRecordService, TikTokShopHealthRecordService>();
        services.AddScoped<ITikTokShopScoreService, TikTokShopScoreService>();
        services.AddScoped<IListingDelistSuggestionService, ListingDelistSuggestionService>();
        services.AddScoped<ISupplierProductInfoService, SupplierProductInfoService>();
        services.AddScoped<IListingAddOriginService, ListingAddOriginService>();
        services.AddScoped<ISupplierSellerInfoService, SupplierSellerInfoService>();
        services.AddSingleton<IPostHtmlDraftSyncScheduler, PostHtmlDraftSyncScheduler>();
        services.AddSingleton<IListingSellerSyncLease, ListingSellerSyncLease>();
        services.AddSingleton<IListingSyncJobRunner, ListingSyncJobRunner>();
        services.AddSingleton<IOrderSyncJobRunner, OrderSyncJobRunner>();
        services.AddScoped<IListingDescriptionAiService, ListingDescriptionAiService>();
        services.AddScoped<ISizeChartImageService, SizeChartImageService>();
        services.AddScoped<IListingSpecificsMapService, ListingSpecificsMapService>();
        services.AddScoped<ITikTokCategorySchemaProvider, TikTokCategorySchemaProvider>();
        services.AddScoped<ITikTokAttributeRejectionService, TikTokAttributeRejectionService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IFulfilService, FulfilService>();
        services.AddScoped<IReceivingBankAccountService, ReceivingBankAccountService>();
        services.AddScoped<IWhatToSellTodayService, WhatToSellTodayService>();
        services.AddScoped<IShopKeywordResearchService, ShopKeywordResearchService>();

        services.AddSingleton<IListingAutoPublishCancellationCoordinator, ListingAutoPublishCancellationCoordinator>();
        services.AddSingleton<IListingAutoPublishConcurrencyGate, ListingAutoPublishConcurrencyGate>();
        services.AddSingleton<IListingAutoPublishDispatchTrigger, ListingAutoPublishDispatchTrigger>();
        services.AddSingleton<IListingShopPublishLease, ListingShopPublishLease>();
        services.AddScoped<IListingAutoPublishPrepService, ListingAutoPublishPrepService>();
        services.AddScoped<IListingAutoPublishOrchestrator, ListingAutoPublishOrchestrator>();
        services.AddScoped<IListingAutoPublishRunService, ListingAutoPublishRunService>();
        services.AddScoped<IListingAutoPublishStepCheckpointService, ListingAutoPublishStepCheckpointService>();
        services.AddScoped<IListingAutoPublishStaleRunRecovery, ListingAutoPublishStaleRunRecovery>();
        services.AddScoped<IListingAutoPublishLogRetentionService, ListingAutoPublishLogRetentionService>();
        services.AddScoped<IListingAutoPublishQueueStatusResolver, ListingAutoPublishQueueStatusResolver>();
        services.AddScoped<IShopAutoPublishSchedulerStatusResolver, ShopAutoPublishSchedulerStatusResolver>();

        services.AddHttpClient("ListingAutoPublish", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        return services;
    }
}
