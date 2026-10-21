using Application.Interfaces;
using Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Periodically re-checks sellers whose 4Seller cookie status is <c>expired</c>.
/// Only promotes back to <c>logged_in</c> on a confirmed is-login success —
/// never latches expired from this loop (avoids false-expiry loops).
/// </summary>
public sealed class FourSellerCookieRecheckBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FourSellerCookieRecheckBackgroundService> _logger;

    /// <summary>Initializes the recheck loop.</summary>
    public FourSellerCookieRecheckBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<FourSellerCookieRecheckBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation(
            "4Seller cookie recheck loop started (every {Minutes} min; expired → logged_in only)",
            Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecheckExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "4Seller cookie recheck tick failed unexpectedly");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RecheckExpiredAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, string? UserName)> expiredUsers;
        using (var listScope = _scopeFactory.CreateScope())
        {
            var db = listScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            expiredUsers = await db.Users
                .AsNoTracking()
                .Where(u =>
                    u.IsActive &&
                    u.FourSellerCookie != null &&
                    u.FourSellerCookie != string.Empty &&
                    u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.Expired)
                .Select(u => new ValueTuple<Guid, string?>(u.Id, u.UserName))
                .ToListAsync(cancellationToken);
        }

        if (expiredUsers.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "4Seller cookie recheck: probing {Count} expired seller(s)",
            expiredUsers.Count);

        var recovered = 0;
        var skipped = 0;
        foreach (var (userId, userName) in expiredUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var fourSeller = scope.ServiceProvider.GetRequiredService<IFourSellerApiService>();
                var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

                var check = await fourSeller.CheckLoginAsync(userId, null, cancellationToken);
                if (check.IsLoggedIn)
                {
                    await userService.SetFourSellerCookieLoginStatusAsync(
                        userId,
                        FourSellerCookieLoginStatuses.LoggedIn,
                        cancellationToken);
                    recovered++;
                    _logger.LogInformation(
                        "4Seller cookie recheck recovered seller {UserId} ({UserName})",
                        userId,
                        userName);
                }
                else
                {
                    // Blocked / Unverified / still Expired — leave status alone.
                    skipped++;
                    if (check.Blocked || check.Unverified)
                    {
                        _logger.LogDebug(
                            "4Seller cookie recheck skipped seller {UserId} ({UserName}): {Message}",
                            userId,
                            userName,
                            check.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                skipped++;
                _logger.LogWarning(
                    ex,
                    "4Seller cookie recheck failed for seller {UserId} ({UserName})",
                    userId,
                    userName);
            }
        }

        _logger.LogInformation(
            "4Seller cookie recheck finished: recovered={Recovered}, skipped={Skipped}",
            recovered,
            skipped);
    }
}
