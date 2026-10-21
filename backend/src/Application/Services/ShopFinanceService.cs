using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Aggregates shop On hold snapshots with receiving-bank ledger income.
/// </summary>
public class ShopFinanceService : IShopFinanceService
{
    private readonly IApplicationDbContext _context;

    /// <summary>Initializes the service.</summary>
    public ShopFinanceService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<ShopFinanceDto> GetForAdminAsync(
        ShopFinanceQuery query,
        CancellationToken cancellationToken = default)
        => GetAsync(ownerId: query.OwnerUserId, query, cancellationToken);

    /// <inheritdoc />
    public Task<ShopFinanceDto> GetForSellerAsync(
        Guid ownerId,
        ShopFinanceQuery query,
        CancellationToken cancellationToken = default)
        => GetAsync(ownerId, query, cancellationToken);

    private async Task<ShopFinanceDto> GetAsync(
        Guid? ownerId,
        ShopFinanceQuery query,
        CancellationToken cancellationToken)
    {
        var shopsQuery = _context.Shops.AsNoTracking();
        if (ownerId.HasValue)
        {
            shopsQuery = shopsQuery.Where(s => s.OwnerId == ownerId.Value);
        }

        var status = string.IsNullOrWhiteSpace(query.Status) ? "Active" : query.Status.Trim();
        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            shopsQuery = shopsQuery.Where(s => s.Status == status);
        }

        var items = await shopsQuery
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => new ShopFinanceItemDto
            {
                ShopId = s.Id,
                Name = s.Name,
                ProfileName = s.ProfileName,
                AvatarUrl = s.AvatarUrl,
                Status = s.Status,
                Deli = s.Deli,
                OnHoldAmount = s.OnHoldAmount,
                OnHoldInTransitAmount = s.OnHoldInTransitAmount,
                OnHoldAwaitingSettlementAmount = s.OnHoldAwaitingSettlementAmount,
                OnHoldReserveAmount = s.OnHoldReserveAmount,
                ReceivingBankAccountId = s.ReceivingBankAccountId,
                TikTokProfileSyncedAt = s.TikTokProfileSyncedAt,
                OwnerId = s.OwnerId,
                OwnerFullName = s.Owner.FullName,
                OwnerUserName = s.Owner.UserName
            })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return new ShopFinanceDto();
        }

        var shopIds = items.Select(i => i.ShopId).ToList();
        var ledger = _context.ReceivingBankTransactions.AsNoTracking()
            .Where(t => t.ShopId != null && shopIds.Contains(t.ShopId.Value));

        var confirmed = ledger.Where(t =>
            t.ConfirmationStatus == ReceivingBankTransaction.ConfirmationConfirmed);
        if (query.From.HasValue)
        {
            confirmed = confirmed.Where(t => t.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            confirmed = confirmed.Where(t => t.OccurredAt <= query.To.Value);
        }

        var receivedRows = await confirmed
            .GroupBy(t => t.ShopId!.Value)
            .Select(g => new
            {
                ShopId = g.Key,
                Amount = g.Sum(t =>
                    t.Direction == ReceivingBankTransaction.DirectionCredit ? t.Amount : -t.Amount)
            })
            .ToListAsync(cancellationToken);

        var pendingRows = await ledger
            .Where(t => t.ConfirmationStatus == ReceivingBankTransaction.ConfirmationPending)
            .GroupBy(t => t.ShopId!.Value)
            .Select(g => new
            {
                ShopId = g.Key,
                Amount = g.Sum(t =>
                    t.Direction == ReceivingBankTransaction.DirectionCredit ? t.Amount : -t.Amount)
            })
            .ToListAsync(cancellationToken);

        var receivedByShop = receivedRows.ToDictionary(x => x.ShopId, x => x.Amount);
        var pendingByShop = pendingRows.ToDictionary(x => x.ShopId, x => x.Amount);

        foreach (var item in items)
        {
            item.MoneyReceived = receivedByShop.GetValueOrDefault(item.ShopId);
            item.PendingConfirmationAmount = pendingByShop.GetValueOrDefault(item.ShopId);
        }

        var acquisitionRows = await _context.CompanyLedgerEntries.AsNoTracking()
            .Where(e => e.Source == CompanyLedgerEntry.SourceShopAcquisition
                && e.ShopId != null
                && shopIds.Contains(e.ShopId.Value))
            .Select(e => new { ShopId = e.ShopId!.Value, e.Amount, e.OccurredAt })
            .ToListAsync(cancellationToken);
        var acquisitionByShop = acquisitionRows.ToDictionary(x => x.ShopId);
        foreach (var item in items)
        {
            if (acquisitionByShop.TryGetValue(item.ShopId, out var row))
            {
                item.AcquisitionCost = row.Amount;
                item.AcquisitionOccurredAt = row.OccurredAt;
            }
        }

        return new ShopFinanceDto
        {
            Summary = new ShopFinanceSummaryDto
            {
                ShopCount = items.Count,
                ShopsWithoutBankCount = items.Count(i => i.ReceivingBankAccountId == null),
                OnHoldAmount = items.Sum(i => i.OnHoldAmount ?? 0),
                OnHoldInTransitAmount = items.Sum(i => i.OnHoldInTransitAmount ?? 0),
                OnHoldAwaitingSettlementAmount = items.Sum(i => i.OnHoldAwaitingSettlementAmount ?? 0),
                OnHoldReserveAmount = items.Sum(i => i.OnHoldReserveAmount ?? 0),
                MoneyReceived = items.Sum(i => i.MoneyReceived),
                PendingConfirmationAmount = items.Sum(i => i.PendingConfirmationAmount),
                AcquisitionCostTotal = items.Sum(i => i.AcquisitionCost ?? 0)
            },
            Items = items
        };
    }
}
