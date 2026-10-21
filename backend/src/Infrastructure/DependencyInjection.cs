using System.Security.Claims;
using System.Text;
using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Authorization;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seeding;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Integrations.FourSeller;
using Infrastructure.Integrations.OpenAI;
using Infrastructure.Integrations.Xai;
using Infrastructure.Integrations.Shein;
using Infrastructure.Integrations.Telegram;
using Infrastructure.Integrations.PrintPackShip;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Shared.Constants;
using Shared.Helpers;

namespace Infrastructure;

/// <summary>
/// Infrastructure layer dependency injection extensions.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure services.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<TelegramSettings>(configuration.GetSection(TelegramSettings.SectionName));
        services.Configure<Application.Options.PrintPackShipOptions>(
            configuration.GetSection(Application.Options.PrintPackShipOptions.SectionName));
        services.Configure<OpenAiSettings>(configuration.GetSection(OpenAiSettings.SectionName));
        services.Configure<XaiSettings>(configuration.GetSection(XaiSettings.SectionName));
        services.Configure<Application.Options.XaiSizeChartOptions>(
            configuration.GetSection(Application.Options.XaiSizeChartOptions.SectionName));

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ISettingProvider, SettingProvider>();
        services.AddScoped<IDatabaseStorageStatsService, DatabaseStorageStatsService>();

        services.AddHttpClient<IBunnyStorageService, BunnyStorageService>();

        // 4Seller egress goes through a per-proxy HttpClient pool (residential proxy rotation
        // to survive EdgeOne 567 IP blocks). The API service resolves the right client per seller.
        services.AddSingleton<IFourSellerHttpClientProvider, FourSellerHttpClientProvider>();
        services.AddSingleton<IFourSellerProxyManager, FourSellerProxyManager>();
        services.AddSingleton<IFourSellerSessionCache, FourSellerSessionCache>();
        services.AddScoped<IFourSellerApiService, FourSellerApiService>();

        // SHEIN egress: one HttpClient per fulfil-account proxy, so accounts do not share an IP.
        services.AddSingleton<ISheinHttpClientProvider, SheinHttpClientProvider>();
        services.AddScoped<ISheinOrderClient, SheinOrderClient>();

        services.AddHttpClient<TelegramApiService>();
        services.AddScoped<ITelegramNotifier, TelegramNotifier>();

        // PrintPackShip USPS track-scan: create batch then send (send is not idempotent).
        services.AddHttpClient<IPrintPackShipScanClient, PrintPackShipScanClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<Application.Options.PrintPackShipOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
                    ? "https://label.printpackship.store/api/v1/"
                    : settings.BaseUrl.TrimEnd('/') + "/";
                client.BaseAddress = new Uri(baseUrl);
                var timeout = settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 60;
                client.Timeout = TimeSpan.FromSeconds(timeout);
            });
        services.AddSingleton<IUspsScanPushGate, UspsScanPushGate>();

        // OpenAI may geo-block VN egress — optional OpenAI:Proxy tunnels chat/completions.
        services.AddHttpClient<IOpenAiService, OpenAiApiService>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
                    ? "https://api.openai.com/v1/"
                    : settings.BaseUrl.TrimEnd('/') + "/";
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                var handler = new System.Net.Http.SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };

                if (!string.IsNullOrWhiteSpace(settings.Proxy)
                    && EgressProxyParser.TryBuild(settings.Proxy, out var proxy))
                {
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }
                else
                {
                    handler.UseProxy = false;
                }

                return handler;
            });

        services.AddHttpClient<IXaiImageService, XaiImageService>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<XaiSettings>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
                    ? "https://api.x.ai/v1/"
                    : settings.BaseUrl.TrimEnd('/') + "/";
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<XaiSettings>>().Value;
                var handler = new System.Net.Http.SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };

                if (!string.IsNullOrWhiteSpace(settings.Proxy)
                    && EgressProxyParser.TryBuild(settings.Proxy, out var proxy))
                {
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }
                else
                {
                    handler.UseProxy = false;
                }

                return handler;
            });

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IPasswordHasher<ApplicationUser>, BcryptPasswordHasher>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IFulfilPinHasher, FulfilPinHasher>();
        services.AddScoped<DataSeeder>();

        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JWT settings are not configured.");

        if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Secret must be configured (min 32 UTF-8 bytes). Set in appsettings or env Jwt__Secret.");
        }

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var userIdValue = context.Principal?.FindFirst(ClaimConstants.UserId)?.Value
                            ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? context.Principal?.FindFirst("sub")?.Value;

                        if (string.IsNullOrWhiteSpace(userIdValue) || !Guid.TryParse(userIdValue, out _))
                        {
                            context.Fail("Token không hợp lệ.");
                            return;
                        }

                        var userManager = context.HttpContext.RequestServices
                            .GetRequiredService<UserManager<ApplicationUser>>();
                        var user = await userManager.FindByIdAsync(userIdValue);
                        if (user is null || !user.IsActive)
                        {
                            context.Fail("Tài khoản không tồn tại hoặc đã bị vô hiệu hóa.");
                            return;
                        }

                        var stampClaim = context.Principal?.FindFirst(ClaimConstants.SecurityStamp)?.Value;
                        if (string.IsNullOrEmpty(stampClaim)
                            || !string.Equals(stampClaim, user.SecurityStamp, StringComparison.Ordinal))
                        {
                            context.Fail("Token đã bị thu hồi.");
                        }
                    }
                };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddSingleton<IOrderSyncOrchestrator, OrderSyncOrchestrator>();
        services.AddHostedService<OrderSyncBackgroundService>();
        // Singleton: the due clocks live on the orchestrator, a scoped one would forget them.
        services.AddSingleton<IOrderStatusSyncOrchestrator, OrderStatusSyncOrchestrator>();
        services.AddHostedService<OrderStatusSyncBackgroundService>();
        services.AddSingleton<IAfterSalesSyncOrchestrator, AfterSalesSyncOrchestrator>();
        services.AddHostedService<AfterSalesSyncBackgroundService>();
        services.AddHostedService<SheinSyncBackgroundService>();
        services.AddHostedService<SheinDetailSweepBackgroundService>();
        services.AddSingleton<ListingSyncLoopGate>();
        services.AddHostedService<ListingDraftSyncBackgroundService>();
        services.AddHostedService<ListingPublishedSyncBackgroundService>();
        services.AddHostedService<ProductDiscountSyncBackgroundService>();
        services.AddHostedService<ListingAutoPublishBackgroundService>();
        services.AddHostedService<OrderDailySummaryBackgroundService>();
        services.AddHostedService<OrderWorkflowSignalsVerifyBackgroundService>();
        services.AddHostedService<FourSellerCookieRecheckBackgroundService>();
        services.AddHostedService<UspsScanAutoPushBackgroundService>();
        services.AddHostedService<Persistence.DataRetentionBackgroundService>();
        services.AddHostedService<Integrations.Telegram.SyncWatchdogBackgroundService>();

        return services;
    }
}
