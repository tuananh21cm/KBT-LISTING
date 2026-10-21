using Application.DTOs.ReceivingBankAccount;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Receiving bank account management service.
/// </summary>
public class ReceivingBankAccountService : IReceivingBankAccountService
{
    private readonly IApplicationDbContext _context;

    /// <summary>Initializes the service.</summary>
    public ReceivingBankAccountService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<ReceivingCardSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ReceivingCardSources
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new ReceivingCardSourceDto
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                SupportsAccounts = s.SupportsAccounts,
                IsActive = s.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ReceivingSourceAccountDto>> GetSourceAccountsAsync(
        Guid? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ReceivingSourceAccounts.AsNoTracking();

        if (sourceId.HasValue)
        {
            query = query.Where(a => a.SourceId == sourceId.Value);
        }

        // List payload for table/edit form — skip SourceCode / timestamps.
        return await query
            .OrderBy(a => a.Source.Name)
            .ThenBy(a => a.Name)
            .Select(a => new ReceivingSourceAccountDto
            {
                Id = a.Id,
                SourceId = a.SourceId,
                SourceName = a.Source.Name,
                Name = a.Name,
                ExternalAccountId = a.ExternalAccountId,
                Username = a.Username,
                Note = a.Note,
                IsActive = a.IsActive,
                CardCount = a.ReceivingBankAccounts.Count + a.FulfilCards.Count
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ReceivingSourceAccountOptionDto>> GetSourceAccountOptionsAsync(
        Guid? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ReceivingSourceAccounts
            .AsNoTracking()
            .Where(a => a.IsActive);

        if (sourceId.HasValue)
        {
            query = query.Where(a => a.SourceId == sourceId.Value);
        }

        return await query
            .OrderBy(a => a.Name)
            .Select(a => new ReceivingSourceAccountOptionDto
            {
                Id = a.Id,
                SourceId = a.SourceId,
                Name = a.Name,
                Username = a.Username,
                IsActive = a.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReceivingSourceAccountDto> CreateSourceAccountAsync(
        CreateReceivingSourceAccountDto dto,
        CancellationToken cancellationToken = default)
    {
        var source = await FindSourceAsync(dto.SourceId, cancellationToken);
        if (!source.SupportsAccounts)
        {
            throw new BusinessException($"Nguồn {source.Name} không hỗ trợ tài khoản con.");
        }

        var account = new ReceivingSourceAccount
        {
            SourceId = source.Id,
            Name = dto.Name.Trim(),
            ExternalAccountId = Normalize(dto.ExternalAccountId),
            Username = Normalize(dto.Username),
            Note = Normalize(dto.Note),
            IsActive = dto.IsActive
        };

        _context.ReceivingSourceAccounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);
        return MapSourceAccountDto(account, source, 0);
    }

    /// <inheritdoc />
    public async Task<ReceivingSourceAccountDto> UpdateSourceAccountAsync(
        Guid id,
        UpdateReceivingSourceAccountDto dto,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.ReceivingSourceAccounts
            .Include(a => a.Source)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản nguồn.");

        account.Name = dto.Name.Trim();
        account.ExternalAccountId = Normalize(dto.ExternalAccountId);
        account.Username = Normalize(dto.Username);
        account.Note = Normalize(dto.Note);
        account.IsActive = dto.IsActive;
        account.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        var cardCount = await _context.ReceivingBankAccounts.CountAsync(
            c => c.SourceAccountId == id,
            cancellationToken)
            + await _context.FulfilCards.CountAsync(
                c => c.SourceAccountId == id,
                cancellationToken);
        return MapSourceAccountDto(account, account.Source, cardCount);
    }

    /// <inheritdoc />
    public async Task DeleteSourceAccountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _context.ReceivingSourceAccounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản nguồn.");

        var hasReceivingCards = await _context.ReceivingBankAccounts.AnyAsync(
            c => c.SourceAccountId == id,
            cancellationToken);
        if (hasReceivingCards)
        {
            throw new BusinessException("Không thể xóa tài khoản nguồn đang có thẻ nhận tiền.");
        }

        var hasFulfilCards = await _context.FulfilCards.AnyAsync(
            c => c.SourceAccountId == id,
            cancellationToken);
        if (hasFulfilCards)
        {
            throw new BusinessException("Không thể xóa tài khoản nguồn đang có thẻ thanh toán.");
        }

        _context.ReceivingSourceAccounts.Remove(account);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ReceivingBankAccountListItemDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ReceivingBankAccounts
            .AsNoTracking()
            .OrderBy(a => a.Source.Name)
            .ThenBy(a => a.AccountHolderName)
            .Select(a => new ReceivingBankAccountListItemDto
            {
                Id = a.Id,
                SourceId = a.SourceId,
                SourceName = a.Source.Name,
                SourceAccountId = a.SourceAccountId,
                SourceAccountName = a.SourceAccount != null ? a.SourceAccount.Name : null,
                Currency = a.Currency,
                AccountHolderName = a.AccountHolderName,
                AccountNumber = a.AccountNumber,
                IsActive = a.IsActive,
                ShopCount = a.Shops.Count,
                Shops = a.Shops
                    .OrderBy(s => s.Name)
                    .Select(s => new ReceivingBankLinkedShopDto
                    {
                        Id = s.Id,
                        Name = s.Name,
                        ProfileName = s.ProfileName
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReceivingBankAccountDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await FindWithRelationsAsync(id, cancellationToken);
        var shops = await _context.Shops
            .AsNoTracking()
            .Where(s => s.ReceivingBankAccountId == id)
            .OrderBy(s => s.Name)
            .Select(s => new ReceivingBankLinkedShopDto
            {
                Id = s.Id,
                Name = s.Name,
                ProfileName = s.ProfileName
            })
            .ToListAsync(cancellationToken);
        return MapToDto(account, shops);
    }

    /// <inheritdoc />
    public async Task<List<ReceivingBankAccountOptionDto>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAccountOptions(_context.ReceivingBankAccounts.AsNoTracking().Where(a => a.IsActive))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ReceivingBankAccountOptionDto>> GetOptionsForOwnerAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        return await QueryAccountOptions(
                _context.ReceivingBankAccounts
                    .AsNoTracking()
                    .Where(a => a.IsActive && a.Shops.Any(s => s.OwnerId == ownerUserId)))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<ReceivingBankAccountOptionDto> QueryAccountOptions(
        IQueryable<ReceivingBankAccount> q) =>
        q.OrderBy(a => a.Source.Name)
            .ThenBy(a => a.Name)
            .Select(a => new ReceivingBankAccountOptionDto
            {
                Id = a.Id,
                Name = a.Name,
                SourceId = a.SourceId,
                SourceName = a.Source.Name,
                SourceAccountId = a.SourceAccountId,
                SourceAccountName = a.SourceAccount != null ? a.SourceAccount.Name : null,
                Currency = a.Currency,
                AccountHolderName = a.AccountHolderName,
                AccountNumber = a.AccountNumber,
                BankName = a.BankName,
                IsActive = a.IsActive
            });

    /// <inheritdoc />
    public async Task<ReceivingBankAccountDto> CreateAsync(
        CreateReceivingBankAccountDto dto,
        CancellationToken cancellationToken = default)
    {
        var account = new ReceivingBankAccount();
        await ApplyFieldsAsync(account, dto.SourceId, dto.SourceAccountId, dto, cancellationToken);
        _context.ReceivingBankAccounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(account.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReceivingBankAccountDto> UpdateAsync(
        Guid id,
        UpdateReceivingBankAccountDto dto,
        CancellationToken cancellationToken = default)
    {
        var account = await FindWithRelationsAsync(id, cancellationToken);
        await ApplyFieldsAsync(account, dto.SourceId, dto.SourceAccountId, dto, cancellationToken);
        account.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var linkedShops = await _context.Shops
            .Where(s => s.ReceivingBankAccountId == id)
            .ToListAsync(cancellationToken);
        foreach (var shop in linkedShops)
        {
            shop.NewBankNumber = account.AccountNumber;
            shop.UpdatedAt = DateTime.UtcNow;
        }

        if (linkedShops.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return await GetByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await FindAsync(id, cancellationToken);
        var hasShops = await _context.Shops.AnyAsync(s => s.ReceivingBankAccountId == id, cancellationToken);
        if (hasShops)
        {
            throw new BusinessException("Không thể xóa thẻ nhận tiền đang được shop sử dụng.");
        }

        var hasTxns = await _context.ReceivingBankTransactions.AnyAsync(
            t => t.ReceivingBankAccountId == id,
            cancellationToken);
        if (hasTxns)
        {
            throw new BusinessException("Không thể xóa thẻ nhận tiền còn lịch sử giao dịch.");
        }

        _context.ReceivingBankAccounts.Remove(account);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReceivingBankIncomingSummaryDto> GetIncomingStatsAsync(
        ReceivingBankIncomingQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = ApplyIncomingStatsFilters(
            _context.ReceivingBankTransactions
                .AsNoTracking()
                .Include(t => t.ReceivingBankAccount!)
                    .ThenInclude(a => a.Source)
                .Include(t => t.ReceivingBankAccount!)
                    .ThenInclude(a => a.SourceAccount)
                .Include(t => t.Shop),
            query);

        var rows = await q.ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new ReceivingBankIncomingSummaryDto();
        }

        var tz = ResolveVietnamTz();
        var total = rows.Sum(SignedAmount);
        var credit = rows.Where(IsCredit).Sum(t => t.Amount);
        var debit = rows.Where(IsDebit).Sum(t => t.Amount);
        var settlementPaid = rows
            .Where(t => t.Source == ReceivingBankTransaction.SourceSettlementPaid)
            .Sum(SignedAmount);
        var manual = rows
            .Where(t => t.Source == ReceivingBankTransaction.SourceManual)
            .Sum(SignedAmount);
        var orderCount = rows.Where(t => t.OrderId.HasValue).Select(t => t.OrderId!.Value).Distinct().Count();

        var byCard = rows
            .GroupBy(t => t.ReceivingBankAccountId)
            .Select(g =>
            {
                var card = g.First().ReceivingBankAccount;
                var holder = card?.AccountHolderName?.Trim();
                var masked = MaskAccountNumber(card?.AccountNumber);
                var displayName = !string.IsNullOrWhiteSpace(holder) && !string.IsNullOrWhiteSpace(masked)
                    ? $"{holder} · {masked}"
                    : !string.IsNullOrWhiteSpace(holder)
                        ? holder
                        : !string.IsNullOrWhiteSpace(masked)
                            ? masked
                            : (card?.Name?.Trim() is { Length: > 0 } n ? n : "Thẻ không tên");
                var sourceName = card?.Source?.Name?.Trim();
                var currency = string.IsNullOrWhiteSpace(card?.Currency) ? null : card!.Currency.Trim();
                var extra = string.Join(
                    " · ",
                    new[] { sourceName, currency }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return BuildIncomingBreakdownItem(
                    g.Key,
                    displayName,
                    string.IsNullOrWhiteSpace(extra) ? null : extra,
                    g);
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var bySource = rows
            .GroupBy(t => t.ReceivingBankAccount?.SourceId)
            .Select(g =>
            {
                var source = g.Select(t => t.ReceivingBankAccount?.Source).FirstOrDefault(s => s != null);
                return BuildIncomingBreakdownItem(
                    g.Key,
                    g.Key == null
                        ? "Không gắn nguồn"
                        : (source?.Name?.Trim() is { Length: > 0 } n ? n : "Nguồn"),
                    extra: null,
                    g);
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var bySourceAccount = rows
            .GroupBy(t => t.ReceivingBankAccount?.SourceAccountId)
            .Select(g =>
            {
                var account = g.Select(t => t.ReceivingBankAccount?.SourceAccount).FirstOrDefault(a => a != null);
                var sourceName = g.Select(t => t.ReceivingBankAccount?.Source?.Name?.Trim())
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                return BuildIncomingBreakdownItem(
                    g.Key,
                    g.Key == null
                        ? "Không gắn TK nguồn"
                        : (account?.Name?.Trim() is { Length: > 0 } n ? n : "Tài khoản nguồn"),
                    g.Key == null ? null : sourceName,
                    g);
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var byShop = rows
            .GroupBy(t => t.ShopId)
            .Select(g =>
            {
                var shop = g.Select(t => t.Shop).FirstOrDefault(s => s != null);
                return BuildIncomingBreakdownItem(
                    g.Key,
                    g.Key == null
                        ? "Không gắn shop"
                        : (shop?.Name?.Trim() is { Length: > 0 } n ? n : "Shop"),
                    extra: null,
                    g);
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var days = rows
            .GroupBy(t => ToVietnamDateKey(t.OccurredAt, tz))
            .OrderBy(g => g.Key)
            .Select(g => new ReceivingBankIncomingDayDto
            {
                Date = g.Key,
                Amount = g.Sum(SignedAmount),
                DebitAmount = g.Where(IsDebit).Sum(t => t.Amount),
                CreditAmount = g.Where(IsCredit).Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .ToList();

        return new ReceivingBankIncomingSummaryDto
        {
            TotalAmount = total,
            CreditAmount = credit,
            DebitAmount = debit,
            TransactionCount = rows.Count,
            OrderCount = orderCount,
            CardCount = byCard.Count,
            SourceCount = bySource.Count(x => x.Id.HasValue),
            SettlementPaidAmount = settlementPaid,
            ManualAmount = manual,
            ByCard = byCard,
            BySource = bySource,
            BySourceAccount = bySourceAccount,
            ByShop = byShop,
            Days = days
        };
    }

    /// <inheritdoc />
    public async Task<List<ReceivingBankTransactionDto>> GetTransactionsAsync(
        ReceivingBankTransactionQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = ApplyTransactionFilters(
            _context.ReceivingBankTransactions
                .AsNoTracking()
                .Include(t => t.ReceivingBankAccount!)
                    .ThenInclude(a => a.Source)
                .Include(t => t.ReceivingBankAccount!)
                    .ThenInclude(a => a.SourceAccount)
                .Include(t => t.Shop!)
                    .ThenInclude(s => s.Owner)
                .Include(t => t.Order)
                .Include(t => t.CreatedByUser),
            query);

        var rows = await q
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(MapTransaction).ToList();
    }

    /// <inheritdoc />
    public async Task<ReceivingBankTransactionDto> GetTransactionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var txn = await LoadTransactionAsync(id, cancellationToken);
        return MapTransaction(txn);
    }

    /// <inheritdoc />
    public async Task<ReceivingBankTransactionDto> CreateTransactionAsync(
        CreateReceivingBankTransactionDto dto,
        Guid createdByUserId,
        CancellationToken cancellationToken = default,
        bool pendingConfirmation = false,
        Guid? ownerUserId = null)
    {
        if (dto.Amount < 0)
        {
            throw new BusinessException("Số tiền phải >= 0.");
        }

        var direction = NormalizeDirection(dto.Direction);
        if (ownerUserId.HasValue || pendingConfirmation)
        {
            if (direction != ReceivingBankTransaction.DirectionCredit)
            {
                throw new BusinessException("Seller chỉ được thêm giao dịch Tiền về.");
            }

            direction = ReceivingBankTransaction.DirectionCredit;
        }

        var cardId = await ResolveCardIdFromShopAsync(dto.ShopId, ownerUserId, cancellationToken);

        if (dto.OrderId.HasValue)
        {
            await EnsureOrderExistsAsync(dto.OrderId.Value, cancellationToken);
        }

        var externalTxnId = TrimOrNull(dto.ExternalTransactionId);
        await EnsureExternalTransactionIdUniqueAsync(externalTxnId, excludeId: null, cancellationToken);

        var txn = new ReceivingBankTransaction
        {
            ReceivingBankAccountId = cardId,
            ShopId = dto.ShopId,
            OrderId = dto.OrderId,
            Amount = dto.Amount,
            Direction = direction,
            Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant(),
            Note = TrimOrNull(dto.Note),
            ExternalTransactionId = externalTxnId,
            Source = ReceivingBankTransaction.SourceManual,
            ConfirmationStatus = pendingConfirmation
                ? ReceivingBankTransaction.ConfirmationPending
                : ReceivingBankTransaction.ConfirmationConfirmed,
            CreatedByUserId = createdByUserId,
            OccurredAt = dto.OccurredAt ?? DateTime.UtcNow
        };

        _context.ReceivingBankTransactions.Add(txn);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetTransactionByIdAsync(txn.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReceivingBankTransactionDto> UpdateTransactionAsync(
        Guid id,
        UpdateReceivingBankTransactionDto dto,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null)
    {
        var txn = await _context.ReceivingBankTransactions
            .Include(t => t.Shop)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy giao dịch.");

        if (dto.Amount < 0)
        {
            throw new BusinessException("Số tiền phải >= 0.");
        }

        if (ownerUserId.HasValue)
        {
            EnsureSellerCanMutatePending(txn, ownerUserId.Value);
            if (NormalizeDirection(dto.Direction) != ReceivingBankTransaction.DirectionCredit)
            {
                throw new BusinessException("Seller chỉ được sửa giao dịch Tiền về.");
            }
        }

        var externalTxnId = TrimOrNull(dto.ExternalTransactionId);
        await EnsureExternalTransactionIdUniqueAsync(externalTxnId, excludeId: id, cancellationToken);

        if (txn.Source == ReceivingBankTransaction.SourceSettlementPaid)
        {
            if (ownerUserId.HasValue)
            {
                throw new BusinessException("Không thể sửa giao dịch tự động từ Đã thanh toán.");
            }

            if (dto.OrderId.HasValue && dto.OrderId != txn.OrderId)
            {
                throw new BusinessException("Không thể đổi đơn hàng của giao dịch tự động từ Đã thanh toán.");
            }

            if (dto.ShopId != Guid.Empty && dto.ShopId != txn.ShopId)
            {
                throw new BusinessException("Không thể đổi shop của giao dịch tự động từ Đã thanh toán.");
            }

            // SettlementPaid stays Credit; card/order/shop locked; allow amount/note/txnId/occurredAt/currency.
            txn.Amount = dto.Amount;
            txn.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant();
            txn.Note = TrimOrNull(dto.Note);
            txn.ExternalTransactionId = externalTxnId;
            txn.OccurredAt = dto.OccurredAt;
            txn.UpdatedAt = DateTime.UtcNow;
        }
        else if (txn.Source == ReceivingBankTransaction.SourceTikTokPayout)
        {
            if (ownerUserId.HasValue)
            {
                throw new BusinessException("Không thể sửa giao dịch sync TTS Payout.");
            }

            if (dto.ShopId != Guid.Empty && dto.ShopId != txn.ShopId)
            {
                throw new BusinessException("Không thể đổi shop của giao dịch TTS Payout.");
            }

            // Keep bank amount / payment id / time from sync; admin may only adjust note.
            txn.Note = TrimOrNull(dto.Note);
            txn.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var cardId = await ResolveCardIdFromShopAsync(dto.ShopId, ownerUserId, cancellationToken);

            if (dto.OrderId.HasValue)
            {
                await EnsureOrderExistsAsync(dto.OrderId.Value, cancellationToken);
                txn.OrderId = dto.OrderId;
            }
            else
            {
                txn.OrderId = null;
            }

            txn.ReceivingBankAccountId = cardId;
            txn.ShopId = dto.ShopId;
            txn.Amount = dto.Amount;
            txn.Direction = ownerUserId.HasValue
                ? ReceivingBankTransaction.DirectionCredit
                : NormalizeDirection(dto.Direction);
            txn.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant();
            txn.Note = TrimOrNull(dto.Note);
            txn.ExternalTransactionId = externalTxnId;
            txn.OccurredAt = dto.OccurredAt;
            txn.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return await GetTransactionByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null)
    {
        var txn = await _context.ReceivingBankTransactions
            .Include(t => t.Shop)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy giao dịch.");

        if (ownerUserId.HasValue)
        {
            EnsureSellerCanMutatePending(txn, ownerUserId.Value);
        }

        if (txn.Source == ReceivingBankTransaction.SourceSettlementPaid && txn.OrderId.HasValue)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == txn.OrderId.Value, cancellationToken);
            if (order != null)
            {
                order.IsSettlementPaid = false;
                order.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (txn.Source == ReceivingBankTransaction.SourceTikTokPayout
                 && !string.IsNullOrWhiteSpace(txn.ExternalTransactionId)
                 && txn.ShopId.HasValue)
        {
            var paymentId = txn.ExternalTransactionId.Trim();
            var linked = await _context.Orders
                .Where(o => o.ShopId == txn.ShopId && o.TikTokPaymentId == paymentId)
                .ToListAsync(cancellationToken);
            foreach (var order in linked)
            {
                order.TikTokPaymentId = null;
                order.IsSettlementPaid = false;
                order.UpdatedAt = DateTime.UtcNow;
            }
        }

        _context.ReceivingBankTransactions.Remove(txn);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReceivingBankTransactionDto> ConfirmTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        SetConfirmationStatusAsync(id, ReceivingBankTransaction.ConfirmationConfirmed, cancellationToken);

    /// <inheritdoc />
    public Task<ReceivingBankTransactionDto> RejectTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        SetConfirmationStatusAsync(id, ReceivingBankTransaction.ConfirmationRejected, cancellationToken);

    /// <inheritdoc />
    public async Task<ReceivingBankTransactionDto> SetConfirmationStatusAsync(
        Guid id,
        string confirmationStatus,
        CancellationToken cancellationToken = default)
    {
        var status = NormalizeConfirmationStatus(confirmationStatus);
        var txn = await _context.ReceivingBankTransactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy giao dịch.");

        if (txn.Source == ReceivingBankTransaction.SourceSettlementPaid)
        {
            throw new BusinessException("Không đổi trạng thái giao dịch tự động từ Đã thanh toán.");
        }

        if (txn.Source is not (ReceivingBankTransaction.SourceManual
            or ReceivingBankTransaction.SourceTikTokPayout))
        {
            throw new BusinessException("Không đổi trạng thái nguồn giao dịch này.");
        }

        if (txn.ConfirmationStatus == status)
        {
            return await GetTransactionByIdAsync(id, cancellationToken);
        }

        txn.ConfirmationStatus = status;
        txn.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return await GetTransactionByIdAsync(id, cancellationToken);
    }

    private static string NormalizeConfirmationStatus(string? raw)
    {
        var status = (raw ?? string.Empty).Trim();
        if (string.Equals(status, ReceivingBankTransaction.ConfirmationPending, StringComparison.OrdinalIgnoreCase))
        {
            return ReceivingBankTransaction.ConfirmationPending;
        }

        if (string.Equals(status, ReceivingBankTransaction.ConfirmationConfirmed, StringComparison.OrdinalIgnoreCase))
        {
            return ReceivingBankTransaction.ConfirmationConfirmed;
        }

        if (string.Equals(status, ReceivingBankTransaction.ConfirmationRejected, StringComparison.OrdinalIgnoreCase))
        {
            return ReceivingBankTransaction.ConfirmationRejected;
        }

        throw new BusinessException("Trạng thái phải là Chờ duyệt, Đã duyệt hoặc Không duyệt.");
    }

    private async Task ApplyFieldsAsync(
        ReceivingBankAccount account,
        Guid sourceId,
        Guid? sourceAccountId,
        CreateReceivingBankAccountDto dto,
        CancellationToken cancellationToken)
    {
        var source = await FindSourceAsync(sourceId, cancellationToken);
        await AssignSourceAsync(account, source, sourceAccountId, cancellationToken);

        account.Name = BuildDisplayName(dto.AccountHolderName, dto.AccountNumber);
        account.Currency = dto.Currency.Trim().ToUpperInvariant();
        account.BankName = dto.BankName.Trim();
        account.BankLocation = Normalize(dto.BankLocation);
        account.AccountHolderName = dto.AccountHolderName.Trim();
        account.AccountType = Normalize(dto.AccountType);
        account.SwiftBic = Normalize(dto.SwiftBic)?.ToUpperInvariant();
        account.RoutingNumber = Normalize(dto.RoutingNumber);
        account.AccountNumber = dto.AccountNumber.Trim();
        account.BankAddress = Normalize(dto.BankAddress);
        account.IsActive = dto.IsActive;
    }

    private async Task ApplyFieldsAsync(
        ReceivingBankAccount account,
        Guid sourceId,
        Guid? sourceAccountId,
        UpdateReceivingBankAccountDto dto,
        CancellationToken cancellationToken)
    {
        var createShape = new CreateReceivingBankAccountDto
        {
            SourceId = dto.SourceId,
            SourceAccountId = dto.SourceAccountId,
            Currency = dto.Currency,
            BankName = dto.BankName,
            BankLocation = dto.BankLocation,
            AccountHolderName = dto.AccountHolderName,
            AccountType = dto.AccountType,
            SwiftBic = dto.SwiftBic,
            RoutingNumber = dto.RoutingNumber,
            AccountNumber = dto.AccountNumber,
            BankAddress = dto.BankAddress,
            IsActive = dto.IsActive
        };
        await ApplyFieldsAsync(account, sourceId, sourceAccountId, createShape, cancellationToken);
    }

    private async Task AssignSourceAsync(
        ReceivingBankAccount account,
        ReceivingCardSource source,
        Guid? sourceAccountId,
        CancellationToken cancellationToken)
    {
        if (source.SupportsAccounts)
        {
            if (!sourceAccountId.HasValue)
            {
                throw new BusinessException($"Nguồn {source.Name} yêu cầu chọn tài khoản nguồn.");
            }

            var sourceAccount = await _context.ReceivingSourceAccounts
                .FirstOrDefaultAsync(a => a.Id == sourceAccountId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tài khoản nguồn.");

            if (sourceAccount.SourceId != source.Id)
            {
                throw new BusinessException("Tài khoản nguồn không thuộc nguồn thẻ đã chọn.");
            }

            if (!sourceAccount.IsActive && account.SourceAccountId != sourceAccount.Id)
            {
                throw new BusinessException("Tài khoản nguồn đang bị vô hiệu.");
            }

            account.SourceId = source.Id;
            account.SourceAccountId = sourceAccount.Id;
            return;
        }

        if (sourceAccountId.HasValue)
        {
            throw new BusinessException($"Nguồn {source.Name} không dùng tài khoản con.");
        }

        account.SourceId = source.Id;
        account.SourceAccountId = null;
    }

    private async Task<ReceivingCardSource> FindSourceAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await _context.ReceivingCardSources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null || !source.IsActive)
        {
            throw new NotFoundException("Không tìm thấy nguồn thẻ.");
        }

        return source;
    }

    private async Task<ReceivingBankAccount> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await _context.ReceivingBankAccounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            throw new NotFoundException("Không tìm thấy thẻ nhận tiền.");
        }

        return account;
    }

    private async Task<ReceivingBankAccount> FindWithRelationsAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await _context.ReceivingBankAccounts
            .Include(a => a.Source)
            .Include(a => a.SourceAccount)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            throw new NotFoundException("Không tìm thấy thẻ nhận tiền.");
        }

        return account;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildDisplayName(string accountHolderName, string accountNumber)
    {
        var holder = accountHolderName.Trim();
        var number = accountNumber.Trim();
        if (string.IsNullOrEmpty(holder))
        {
            return number;
        }

        return string.IsNullOrEmpty(number) ? holder : $"{holder} · {number}";
    }

    private static ReceivingSourceAccountDto MapSourceAccountDto(
        ReceivingSourceAccount account,
        ReceivingCardSource source,
        int cardCount) => new()
    {
        Id = account.Id,
        SourceId = account.SourceId,
        SourceName = source.Name,
        SourceCode = source.Code,
        Name = account.Name,
        ExternalAccountId = account.ExternalAccountId,
        Username = account.Username,
        Note = account.Note,
        IsActive = account.IsActive,
        CardCount = cardCount,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt
    };

    private static ReceivingBankAccountDto MapToDto(
        ReceivingBankAccount account,
        List<ReceivingBankLinkedShopDto> shops) => new()
    {
        Id = account.Id,
        Name = account.Name,
        SourceId = account.SourceId,
        SourceCode = account.Source?.Code ?? string.Empty,
        SourceName = account.Source?.Name ?? string.Empty,
        SourceAccountId = account.SourceAccountId,
        SourceAccountName = account.SourceAccount?.Name,
        Currency = account.Currency,
        BankName = account.BankName,
        BankLocation = account.BankLocation,
        AccountHolderName = account.AccountHolderName,
        AccountType = account.AccountType,
        SwiftBic = account.SwiftBic,
        RoutingNumber = account.RoutingNumber,
        AccountNumber = account.AccountNumber,
        BankAddress = account.BankAddress,
        IsActive = account.IsActive,
        ShopCount = shops.Count,
        Shops = shops,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt
    };

    private async Task<ReceivingBankTransaction> LoadTransactionAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.ReceivingBankTransactions
            .AsNoTracking()
            .Include(t => t.ReceivingBankAccount!)
                .ThenInclude(a => a.Source)
            .Include(t => t.ReceivingBankAccount!)
                .ThenInclude(a => a.SourceAccount)
            .Include(t => t.Shop!)
                .ThenInclude(s => s.Owner)
            .Include(t => t.Order)
            .Include(t => t.CreatedByUser)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy giao dịch.");
    }

    private static IQueryable<ReceivingBankTransaction> ApplyTransactionFilters(
        IQueryable<ReceivingBankTransaction> q,
        ReceivingBankTransactionQuery query)
    {
        if (query.OwnerUserId.HasValue)
        {
            // Seller scope: only txns on shops owned by this user (exclude manual null-shop rows).
            q = q.Where(t => t.Shop != null && t.Shop.OwnerId == query.OwnerUserId.Value);
        }

        if (query.ReceivingBankAccountId.HasValue)
        {
            q = q.Where(t => t.ReceivingBankAccountId == query.ReceivingBankAccountId.Value);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(t => t.ShopId == query.ShopId.Value);
        }

        if (query.OrderId.HasValue)
        {
            q = q.Where(t => t.OrderId == query.OrderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            var source = query.Source.Trim();
            q = q.Where(t => t.Source == source);
        }

        if (query.From.HasValue)
        {
            q = q.Where(t => t.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            q = q.Where(t => t.OccurredAt <= query.To.Value);
        }

        return q;
    }

    private static IQueryable<ReceivingBankTransaction> ApplyIncomingStatsFilters(
        IQueryable<ReceivingBankTransaction> q,
        ReceivingBankIncomingQuery query)
    {
        q = q.Where(t => t.ConfirmationStatus == ReceivingBankTransaction.ConfirmationConfirmed);

        if (query.ReceivingBankAccountId.HasValue)
        {
            q = q.Where(t => t.ReceivingBankAccountId == query.ReceivingBankAccountId.Value);
        }

        if (query.SourceId.HasValue)
        {
            q = q.Where(t => t.ReceivingBankAccount != null
                && t.ReceivingBankAccount.SourceId == query.SourceId.Value);
        }

        if (query.SourceAccountId.HasValue)
        {
            q = q.Where(t => t.ReceivingBankAccount != null
                && t.ReceivingBankAccount.SourceAccountId == query.SourceAccountId.Value);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(t => t.ShopId == query.ShopId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.TxnSource))
        {
            var txnSource = query.TxnSource.Trim();
            q = q.Where(t => t.Source == txnSource);
        }

        if (query.From.HasValue)
        {
            q = q.Where(t => t.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            q = q.Where(t => t.OccurredAt <= query.To.Value);
        }

        return q;
    }

    private static ReceivingBankIncomingBreakdownItemDto BuildIncomingBreakdownItem(
        Guid? id,
        string name,
        string? extra,
        IEnumerable<ReceivingBankTransaction> group)
    {
        var list = group as IList<ReceivingBankTransaction> ?? group.ToList();
        return new ReceivingBankIncomingBreakdownItemDto
        {
            Id = id,
            Name = name,
            Extra = extra,
            Amount = list.Sum(SignedAmount),
            DebitAmount = list.Where(IsDebit).Sum(t => t.Amount),
            CreditAmount = list.Where(IsCredit).Sum(t => t.Amount),
            TransactionCount = list.Count,
            OrderCount = list.Where(t => t.OrderId.HasValue).Select(t => t.OrderId!.Value).Distinct().Count()
        };
    }

    private static bool IsCredit(ReceivingBankTransaction t) =>
        string.Equals(t.Direction, ReceivingBankTransaction.DirectionCredit, StringComparison.OrdinalIgnoreCase);

    private static bool IsDebit(ReceivingBankTransaction t) => !IsCredit(t);

    /// <summary>Credit positive, Debit negative (tiền về net).</summary>
    private static decimal SignedAmount(ReceivingBankTransaction t) =>
        IsCredit(t) ? t.Amount : -t.Amount;

    private static string ToVietnamDateKey(DateTime utcOrUnspecified, TimeZoneInfo tz)
    {
        var utc = utcOrUnspecified.Kind switch
        {
            DateTimeKind.Utc => utcOrUnspecified,
            DateTimeKind.Local => utcOrUnspecified.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz).ToString("yyyy-MM-dd");
    }

    private static TimeZoneInfo ResolveVietnamTz()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }

    private async Task EnsureReceivingBankExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        var exists = await _context.ReceivingBankAccounts.AnyAsync(a => a.Id == id, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Không tìm thấy thẻ nhận tiền.");
        }
    }

    /// <summary>Resolves receiving-bank card from shop; optionally enforces seller ownership.</summary>
    private async Task<Guid> ResolveCardIdFromShopAsync(
        Guid shopId,
        Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        var shop = await _context.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        if (ownerUserId.HasValue && shop.OwnerId != ownerUserId.Value)
        {
            throw new BusinessException("Shop không thuộc tài khoản của bạn.");
        }

        if (!shop.ReceivingBankAccountId.HasValue)
        {
            throw new BusinessException("Shop chưa gắn thẻ nhận tiền.");
        }

        await EnsureReceivingBankExistsAsync(shop.ReceivingBankAccountId.Value, cancellationToken);
        return shop.ReceivingBankAccountId.Value;
    }

    private async Task EnsureExternalTransactionIdUniqueAsync(
        string? externalTransactionId,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalTransactionId))
        {
            return;
        }

        var id = externalTransactionId.Trim();
        var exists = await _context.ReceivingBankTransactions.AnyAsync(
            t => t.ExternalTransactionId == id && (!excludeId.HasValue || t.Id != excludeId.Value),
            cancellationToken);
        if (exists)
        {
            throw new BusinessException("Transaction đã tồn tại — không được trùng mã giao dịch.");
        }
    }

    private static void EnsureSellerCanMutatePending(ReceivingBankTransaction txn, Guid ownerUserId)
    {
        if (txn.Source != ReceivingBankTransaction.SourceManual)
        {
            throw new BusinessException("Chỉ được sửa/xóa giao dịch thủ công đang chờ duyệt.");
        }

        if (txn.ConfirmationStatus != ReceivingBankTransaction.ConfirmationPending)
        {
            throw new BusinessException("Chỉ được sửa/xóa giao dịch đang chờ duyệt.");
        }

        if (txn.Shop == null || txn.Shop.OwnerId != ownerUserId)
        {
            throw new BusinessException("Giao dịch không thuộc shop của bạn.");
        }
    }

    private async Task EnsureShopExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        var exists = await _context.Shops.AnyAsync(s => s.Id == id, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }
    }

    private async Task EnsureOrderExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        var exists = await _context.Orders.AnyAsync(o => o.Id == id, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }
    }

    private static string NormalizeDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)
            || direction.Equals(ReceivingBankTransaction.DirectionCredit, StringComparison.OrdinalIgnoreCase))
        {
            return ReceivingBankTransaction.DirectionCredit;
        }

        if (direction.Equals(ReceivingBankTransaction.DirectionDebit, StringComparison.OrdinalIgnoreCase))
        {
            return ReceivingBankTransaction.DirectionDebit;
        }

        throw new BusinessException("Direction phải là Debit hoặc Credit.");
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? DisplayName(ApplicationUser? user) =>
        user == null
            ? null
            : (string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName);

    private static string MaskAccountNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }

        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length <= 8)
        {
            return digits;
        }

        return $"{digits[..4]}****{digits[^4..]}";
    }

    private static ReceivingBankTransactionDto MapTransaction(ReceivingBankTransaction t) => new()
    {
        Id = t.Id,
        ReceivingBankAccountId = t.ReceivingBankAccountId,
        ReceivingBankAccountName = t.ReceivingBankAccount?.Name,
        ReceivingBankAccountNumber = t.ReceivingBankAccount?.AccountNumber,
        ReceivingBankAccountNumberMasked = MaskAccountNumber(t.ReceivingBankAccount?.AccountNumber),
        ReceivingBankAccountCurrency = t.ReceivingBankAccount?.Currency,
        SourceName = t.ReceivingBankAccount?.Source?.Name,
        SourceAccountName = t.ReceivingBankAccount?.SourceAccount?.Name,
        AccountHolderName = t.ReceivingBankAccount?.AccountHolderName,
        ShopId = t.ShopId,
        ShopName = t.Shop?.Name,
        ShopProfileName = t.Shop?.ProfileName,
        ShopOwnerId = t.Shop?.OwnerId,
        ShopOwnerName = DisplayName(t.Shop?.Owner),
        OrderId = t.OrderId,
        PlatformOrderId = t.Order?.PlatformOrderId,
        FourSellerOrderId = t.Order?.FourSellerOrderId,
        Amount = t.Amount,
        Direction = string.IsNullOrWhiteSpace(t.Direction)
            ? ReceivingBankTransaction.DirectionCredit
            : t.Direction,
        Currency = t.Currency,
        Note = t.Note,
        ExternalTransactionId = t.ExternalTransactionId,
        Source = t.Source,
        PayoutOrders = DeserializePayoutOrders(t.PayoutDetailsJson),
        ConfirmationStatus = string.IsNullOrWhiteSpace(t.ConfirmationStatus)
            ? ReceivingBankTransaction.ConfirmationConfirmed
            : t.ConfirmationStatus,
        CreatedByUserId = t.CreatedByUserId,
        CreatedByUserName = DisplayName(t.CreatedByUser),
        OccurredAt = t.OccurredAt,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    private static List<TikTokPayoutOrderDetailDto>? DeserializePayoutOrders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var rows = System.Text.Json.JsonSerializer.Deserialize<List<TikTokPayoutOrderDetailDto>>(
                json,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            return rows is { Count: > 0 } ? rows : null;
        }
        catch
        {
            return null;
        }
    }
}
