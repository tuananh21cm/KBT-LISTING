using System.Text.Json;
using Application.DTOs.Fulfil;
using Application.DTOs.Shein;
using Application.Interfaces;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Fulfil accounts, cards, and transaction ledger.
/// </summary>
public class FulfilService : IFulfilService
{
    private readonly IApplicationDbContext _db;
    private readonly ISheinOrderClient _sheinClient;

    /// <summary>Initializes the fulfil service.</summary>
    public FulfilService(IApplicationDbContext db, ISheinOrderClient sheinClient)
    {
        _db = db;
        _sheinClient = sheinClient;
    }

    /// <inheritdoc />
    public async Task<List<FulfilAccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _db.FulfilAccounts
            .AsNoTracking()
            .Include(a => a.Assignees)
                .ThenInclude(x => x.User)
            .Include(a => a.ActiveCard!)
                .ThenInclude(c => c.Source)
            .Include(a => a.ActiveCard!)
                .ThenInclude(c => c.SourceAccount)
            .Include(a => a.CardLinks)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return accounts.Select(MapAccount).ToList();
    }

    /// <inheritdoc />
    public async Task<FulfilAccountDto> GetAccountByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await LoadAccountAsync(id, cancellationToken);
        return MapAccount(account);
    }

    /// <inheritdoc />
    public async Task<List<FulfilAccountOptionDto>> GetAccountOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.FulfilAccounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new FulfilAccountOptionDto
            {
                Id = a.Id,
                Name = a.Name,
                Email = a.Email,
                ActiveCardId = a.ActiveCardId
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FulfilAccountDto> CreateAccountAsync(CreateFulfilAccountDto dto, CancellationToken cancellationToken = default)
    {
        var assigneeIds = NormalizeIds(dto.AssigneeUserIds);
        await EnsureUsersExistAsync(assigneeIds, "Người được cấp tài khoản", cancellationToken);

        var username = TrimOrNull(dto.Username);
        var email = TrimOrNull(dto.Email);
        await EnsureUniqueUsernameAndEmailAsync(username, email, excludeAccountId: null, cancellationToken);

        var account = new FulfilAccount
        {
            Name = dto.Name.Trim(),
            Username = username,
            Password = TrimOrNull(dto.Password),
            Email = email,
            PassMail = TrimOrNull(dto.PassMail),
            TwoFactorAuth = TrimOrNull(dto.TwoFactorAuth),
            SheinProxy = TrimOrNull(dto.SheinProxy),
            IsActive = dto.IsActive
        };

        foreach (var userId in assigneeIds)
        {
            account.Assignees.Add(new FulfilAccountAssignee
            {
                UserId = userId,
                FulfilAccount = account
            });
        }

        _db.FulfilAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAccountByIdAsync(account.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FulfilAccountDto> UpdateAccountAsync(Guid id, UpdateFulfilAccountDto dto, CancellationToken cancellationToken = default)
    {
        var account = await _db.FulfilAccounts
            .Include(a => a.Assignees)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");

        var assigneeIds = NormalizeIds(dto.AssigneeUserIds);
        await EnsureUsersExistAsync(assigneeIds, "Người được cấp tài khoản", cancellationToken);

        var username = TrimOrNull(dto.Username);
        var email = TrimOrNull(dto.Email);
        await EnsureUniqueUsernameAndEmailAsync(username, email, excludeAccountId: id, cancellationToken);

        account.Name = dto.Name.Trim();
        account.Username = username;
        account.Password = TrimOrNull(dto.Password);
        account.Email = email;
        account.PassMail = TrimOrNull(dto.PassMail);
        account.TwoFactorAuth = TrimOrNull(dto.TwoFactorAuth);
        account.SheinProxy = TrimOrNull(dto.SheinProxy);
        account.IsActive = dto.IsActive;
        account.UpdatedAt = DateTime.UtcNow;
        SyncAccountAssigneeLinks(account, assigneeIds);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetAccountByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAccountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _db.FulfilAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");

        var hasTxns = await _db.FulfilTransactions.AnyAsync(t => t.FulfilAccountId == id, cancellationToken);
        if (hasTxns)
        {
            throw new BusinessException("Không thể xóa tài khoản đang có lịch sử giao dịch.");
        }

        // Orders / supplier links may still reference the account after ledger rows were deleted.
        // Unlink them (OrderSupplierLink FK is Restrict) then remove the account.
        await UnlinkFulfilAccountAsync(id, cancellationToken);

        account.ActiveCardId = null;
        await _db.SaveChangesAsync(cancellationToken);

        _db.FulfilAccounts.Remove(account);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FulfilAccountDto> SetActiveCardAsync(
        Guid accountId,
        SetActiveFulfilCardDto dto,
        CancellationToken cancellationToken = default)
    {
        await SetActiveCardInternalAsync(accountId, dto.CardId, cancellationToken);
        return await GetAccountByIdAsync(accountId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SheinSessionSavedDto> SaveSheinSessionAsync(
        SaveSheinSessionDto dto,
        CancellationToken cancellationToken = default)
    {
        var account = await ResolveSheinSessionAccountAsync(dto, cancellationToken);

        var headers = SanitizeSheinHeaders(dto.Headers);
        var missing = SheinSessionKeys.MissingRequiredHeaders(headers);
        if (missing.Count > 0)
        {
            throw new BusinessException(
                $"Session SHEIN chưa đủ header bắt buộc: {string.Join(", ", missing)}.");
        }

        var cookie = TrimOrNull(dto.Cookie);
        if (cookie is not null && cookie.Length > SheinSessionKeys.MaxCookieLength)
        {
            throw new BusinessException("Cookie SHEIN quá dài, không lưu được.");
        }

        var origin = NormalizeSheinOrigin(dto.Origin);
        var capturedAt = dto.CapturedAt is { } at && at != default
            ? at.ToUniversalTime()
            : DateTime.UtcNow;

        account.SheinHeadersJson = JsonSerializer.Serialize(headers);
        // Keep the previous cookie when the extension could not read one this round; the
        // security headers alone are useless and re-capturing cookies needs an open browser.
        if (cookie is not null)
        {
            account.SheinCookie = cookie;
        }

        account.SheinOrigin = origin;
        account.SheinSessionCapturedAt = capturedAt;
        account.SheinSessionLastError = null;
        account.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var hasCookie = !string.IsNullOrWhiteSpace(account.SheinCookie);
        return new SheinSessionSavedDto
        {
            AccountId = account.Id,
            AccountName = account.Name,
            AccountEmail = account.Email,
            HeaderCount = headers.Count,
            HasCookie = hasCookie,
            HasProxy = !string.IsNullOrWhiteSpace(account.SheinProxy),
            Ready = hasCookie,
            MissingHeaders = [],
            CapturedAt = capturedAt
        };
    }

    /// <inheritdoc />
    public async Task<SheinSessionDetailDto> GetSheinSessionAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await _db.FulfilAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");

        var headers = DeserializeSheinHeaders(account.SheinHeadersJson);
        var missing = SheinSessionKeys.MissingRequiredHeaders(headers);
        var hasCookie = !string.IsNullOrWhiteSpace(account.SheinCookie);

        return new SheinSessionDetailDto
        {
            AccountId = account.Id,
            AccountName = account.Name,
            Origin = account.SheinOrigin,
            Cookie = account.SheinCookie,
            Headers = headers,
            UserAgent = headers.GetValueOrDefault("user-agent"),
            Proxy = account.SheinProxy,
            CapturedAt = account.SheinSessionCapturedAt,
            LastCheckedAt = account.SheinSessionLastCheckedAt,
            LastError = account.SheinSessionLastError,
            MissingHeaders = missing,
            Ready = hasCookie && missing.Count == 0
        };
    }

    /// <inheritdoc />
    public async Task<SheinSessionSavedDto> SaveSheinSessionManualAsync(
        Guid accountId,
        SaveSheinSessionManualDto dto,
        CancellationToken cancellationToken = default)
    {
        var account = await _db.FulfilAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");

        // Khối dán tay trước, dict sau — người dán một dict cụ thể là đang sửa từng header.
        var merged = SheinSessionHeaderTextParser.Parse(dto.HeadersText);
        if (dto.Headers is { Count: > 0 })
        {
            foreach (var (key, value) in SanitizeSheinHeaders(dto.Headers))
            {
                merged[key] = value;
            }
        }

        var userAgent = TrimOrNull(dto.UserAgent);
        if (userAgent is not null)
        {
            merged["user-agent"] = userAgent;
        }

        var cookie = TrimOrNull(dto.Cookie);

        // Rỗng cả hai = xoá session. Đây là cách duy nhất bỏ một session hỏng mà không phải
        // xoá tài khoản; vòng quét nền sẽ chỉ bỏ qua account này thay vì đốt request 403.
        if (merged.Count == 0 && cookie is null)
        {
            account.SheinHeadersJson = null;
            account.SheinCookie = null;
            account.SheinSessionCapturedAt = null;
            account.SheinSessionLastError = null;
            account.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return new SheinSessionSavedDto
            {
                AccountId = account.Id,
                AccountName = account.Name,
                AccountEmail = account.Email,
                HeaderCount = 0,
                HasCookie = false,
                HasProxy = !string.IsNullOrWhiteSpace(account.SheinProxy),
                Ready = false,
                MissingHeaders = [.. SheinSessionKeys.RequiredHeaders],
                CapturedAt = DateTime.UtcNow
            };
        }

        var missing = SheinSessionKeys.MissingRequiredHeaders(merged);
        if (missing.Count > 0)
        {
            throw new BusinessException(
                $"Session SHEIN chưa đủ header bắt buộc: {string.Join(", ", missing)}.");
        }

        if (cookie is null)
        {
            throw new BusinessException("Thiếu cookie SHEIN — header bảo mật một mình không gọi được API.");
        }

        if (cookie.Length > SheinSessionKeys.MaxCookieLength)
        {
            throw new BusinessException("Cookie SHEIN quá dài, không lưu được.");
        }

        var capturedAt = DateTime.UtcNow;
        account.SheinHeadersJson = JsonSerializer.Serialize(merged);
        account.SheinCookie = cookie;
        account.SheinOrigin = NormalizeSheinOrigin(dto.Origin ?? account.SheinOrigin);
        account.SheinSessionCapturedAt = capturedAt;
        account.SheinSessionLastError = null;
        account.UpdatedAt = capturedAt;

        await _db.SaveChangesAsync(cancellationToken);

        return new SheinSessionSavedDto
        {
            AccountId = account.Id,
            AccountName = account.Name,
            AccountEmail = account.Email,
            HeaderCount = merged.Count,
            HasCookie = true,
            HasProxy = !string.IsNullOrWhiteSpace(account.SheinProxy),
            Ready = true,
            MissingHeaders = [],
            CapturedAt = capturedAt
        };
    }

    /// <inheritdoc />
    public async Task<TestSheinSessionResultDto> TestSheinSessionAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await _db.FulfilAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");

        var session = SheinSessionContext.TryCreate(
            account.Id,
            account.Name,
            account.Email,
            account.SheinHeadersJson,
            account.SheinCookie,
            account.SheinOrigin,
            account.SheinProxy);

        if (session is null)
        {
            var missing = SheinSessionKeys.MissingRequiredHeaders(
                DeserializeSheinHeaders(account.SheinHeadersJson));
            return new TestSheinSessionResultDto
            {
                Ok = false,
                SessionInvalid = true,
                Error = missing.Count > 0
                    ? $"Session chưa đủ header bắt buộc: {string.Join(", ", missing)}."
                    : "Chưa có cookie SHEIN — bật Auto Sync trên extension hoặc dán tay."
            };
        }

        // Đơn mới nhất của chính tài khoản này: get_order_info là đúng request vòng quét nền sẽ
        // gọi, nên nó test đúng thứ cần test. Không có đơn thì đo bằng trang list.
        var billno = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(l => l.FulfilAccountId == accountId && l.SupplierOrderNo != null)
            .OrderByDescending(l => l.LastSeenAt)
            .Select(l => l.SupplierOrderNo)
            .FirstOrDefaultAsync(cancellationToken);

        var startedAt = DateTime.UtcNow;
        var probe = string.IsNullOrWhiteSpace(billno)
            ? await _sheinClient.FetchOrderListPageAsync(session, 1, 1, cancellationToken)
            : await _sheinClient.FetchOrderDetailAsync(session, billno, cancellationToken);
        var elapsedMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;

        var checkedAt = DateTime.UtcNow;
        account.SheinSessionLastCheckedAt = checkedAt;
        if (probe.Ok)
        {
            account.SheinSessionLastError = null;
        }
        else if (probe.SessionInvalid)
        {
            // Chỉ SHEIN từ chối session mới bắt người vận hành bắt lại cookie. Proxy/mạng
            // chết thì trả lỗi trên DTO lần test này, không nhuộm badge tài khoản.
            account.SheinSessionLastError = probe.Error is { Length: > 500 }
                ? probe.Error[..500]
                : probe.Error;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new TestSheinSessionResultDto
        {
            Ok = probe.Ok,
            Error = probe.Error,
            SessionInvalid = probe.SessionInvalid,
            ProxyFailed = probe.ProxyFailed,
            Billno = string.IsNullOrWhiteSpace(billno) ? null : billno,
            ElapsedMs = elapsedMs
        };
    }

    /// <summary>Reads the stored header dictionary, treating unparsable JSON as "no session".</summary>
    private static Dictionary<string, string> DeserializeSheinHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<FulfilAccount> ResolveSheinSessionAccountAsync(
        SaveSheinSessionDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.AccountId is { } id && id != Guid.Empty)
        {
            return await _db.FulfilAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");
        }

        var email = TrimOrNull(dto.AccountEmail);
        if (email is null)
        {
            throw new BusinessException("Thiếu email tài khoản SHEIN để gắn session.");
        }

        var normalized = email.ToLowerInvariant();
        return await _db.FulfilAccounts
                   .FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == normalized, cancellationToken)
               ?? throw new NotFoundException(
                   $"Chưa có tài khoản fulfil nào dùng email SHEIN '{email}'. Hãy thêm email này vào tài khoản mua hàng trước.");
    }

    /// <summary>
    /// Keeps only usable request headers: drops per-connection ones, caps size, lowercases names.
    /// </summary>
    private static Dictionary<string, string> SanitizeSheinHeaders(Dictionary<string, string>? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (raw is null)
        {
            return result;
        }

        foreach (var (key, value) in raw)
        {
            if (result.Count >= SheinSessionKeys.MaxHeaders)
            {
                break;
            }

            var name = key?.Trim().ToLowerInvariant();
            var content = value?.Trim();
            if (string.IsNullOrEmpty(name) ||
                string.IsNullOrEmpty(content) ||
                SheinSessionKeys.DeniedHeaders.Contains(name) ||
                content.Length > SheinSessionKeys.MaxHeaderValueLength)
            {
                continue;
            }

            result[name] = content;
        }

        return result;
    }

    /// <summary>Normalizes a storefront origin to <c>scheme://host</c> without a trailing slash.</summary>
    private static string NormalizeSheinOrigin(string? raw)
    {
        var value = TrimOrNull(raw);
        if (value is null)
        {
            return SheinSessionKeys.DefaultOrigin;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? $"{uri.Scheme}://{uri.Authority}"
            : SheinSessionKeys.DefaultOrigin;
    }

    /// <inheritdoc />
    public async Task<List<FulfilCardDto>> GetCardsAsync(Guid? accountId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.FulfilCards
            .AsNoTracking()
            .Include(c => c.Source)
            .Include(c => c.SourceAccount)
            .Include(c => c.AccountLinks)
                .ThenInclude(l => l.FulfilAccount)
            .Include(c => c.Assignees)
                .ThenInclude(a => a.User)
            .AsQueryable();

        if (accountId.HasValue)
        {
            query = query.Where(c => c.AccountLinks.Any(l => l.FulfilAccountId == accountId.Value));
        }

        var cards = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var cardIds = cards.Select(c => c.Id).ToList();
        var orderCounts = await _db.Orders
            .AsNoTracking()
            .Where(o => o.FulfilCardId != null && cardIds.Contains(o.FulfilCardId.Value))
            .GroupBy(o => o.FulfilCardId!.Value)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CardId, x => x.Count, cancellationToken);

        return cards.Select(c => MapCard(c, orderCounts.GetValueOrDefault(c.Id))).ToList();
    }

    /// <inheritdoc />
    public async Task<FulfilCardDto> GetCardByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var card = await LoadCardAsync(id, cancellationToken);
        var orderCount = await _db.Orders.CountAsync(o => o.FulfilCardId == id, cancellationToken);
        return MapCard(card, orderCount);
    }

    /// <inheritdoc />
    public async Task<List<FulfilCardOptionDto>> GetCardOptionsAsync(Guid? accountId = null, CancellationToken cancellationToken = default)
    {
        Guid? preferredCardId = null;
        var query = _db.FulfilCards
            .AsNoTracking()
            .Include(c => c.Source)
            .Include(c => c.SourceAccount)
            .Where(c => c.IsActive);

        if (accountId.HasValue)
        {
            preferredCardId = await _db.FulfilAccounts.AsNoTracking()
                .Where(a => a.Id == accountId.Value)
                .Select(a => a.ActiveCardId)
                .FirstOrDefaultAsync(cancellationToken);

            query = query.Where(c => c.AccountLinks.Any(l => l.FulfilAccountId == accountId.Value));
        }

        var cards = await query
            .OrderByDescending(c => preferredCardId != null && c.Id == preferredCardId)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return cards.Select(c => new FulfilCardOptionDto
        {
            Id = c.Id,
            Name = c.Name,
            CardNumberMasked = MaskCardNumber(c.CardNumber),
            CardBrand = c.CardBrand,
            Currency = string.IsNullOrWhiteSpace(c.Currency) ? "USD" : c.Currency,
            SourceId = c.SourceId,
            SourceName = c.Source?.Name,
            SourceAccountId = c.SourceAccountId,
            SourceAccountName = c.SourceAccount?.Name,
            IsActive = c.IsActive,
            IsPreferredForAccount = preferredCardId != null && c.Id == preferredCardId
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<FulfilCardDto> CreateCardAsync(CreateFulfilCardDto dto, CancellationToken cancellationToken = default)
    {
        var accountIds = NormalizeIds(dto.AccountIds);
        var assigneeIds = NormalizeIds(dto.AssigneeUserIds);
        await EnsureAccountsExistAsync(accountIds, cancellationToken);
        await EnsureUsersExistAsync(assigneeIds, "Người được cấp thẻ", cancellationToken);

        var card = new FulfilCard
        {
            Name = string.Empty, // filled by AssignCardSourceAsync
            CardNumber = NormalizeCardNumber(dto.CardNumber),
            CardBrand = NormalizeBrand(dto.CardBrand),
            ExpiryMonth = dto.ExpiryMonth,
            ExpiryYear = dto.ExpiryYear,
            HolderName = TrimOrNull(dto.HolderName),
            Cvv = TrimOrNull(dto.Cvv),
            Currency = NormalizeCurrency(dto.Currency),
            IsActive = dto.IsActive
        };
        await AssignCardSourceAsync(card, dto.SourceId, dto.SourceAccountId, cancellationToken);

        foreach (var accountId in accountIds)
        {
            card.AccountLinks.Add(new FulfilAccountCard
            {
                FulfilAccountId = accountId,
                FulfilCard = card
            });
        }

        foreach (var userId in assigneeIds)
        {
            card.Assignees.Add(new FulfilCardAssignee
            {
                UserId = userId,
                FulfilCard = card
            });
        }

        _db.FulfilCards.Add(card);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetCardByIdAsync(card.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FulfilCardDto> UpdateCardAsync(Guid id, UpdateFulfilCardDto dto, CancellationToken cancellationToken = default)
    {
        var card = await _db.FulfilCards
            .Include(c => c.AccountLinks)
            .Include(c => c.Assignees)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy thẻ thanh toán.");

        var accountIds = NormalizeIds(dto.AccountIds);
        var assigneeIds = NormalizeIds(dto.AssigneeUserIds);
        await EnsureAccountsExistAsync(accountIds, cancellationToken);
        await EnsureUsersExistAsync(assigneeIds, "Người được cấp thẻ", cancellationToken);
        await AssignCardSourceAsync(card, dto.SourceId, dto.SourceAccountId, cancellationToken);

        card.CardNumber = NormalizeCardNumber(dto.CardNumber);
        card.CardBrand = NormalizeBrand(dto.CardBrand);
        card.ExpiryMonth = dto.ExpiryMonth;
        card.ExpiryYear = dto.ExpiryYear;
        card.HolderName = TrimOrNull(dto.HolderName);
        card.Cvv = TrimOrNull(dto.Cvv);
        card.Currency = NormalizeCurrency(dto.Currency);
        card.IsActive = dto.IsActive;
        card.UpdatedAt = DateTime.UtcNow;

        SyncAccountLinks(card, accountIds);
        SyncAssigneeLinks(card, assigneeIds);

        // Clear preferred card on accounts that no longer link this card.
        var nextAccountIds = accountIds.ToHashSet();
        var accountsToClear = await _db.FulfilAccounts
            .Where(a => a.ActiveCardId == id && !nextAccountIds.Contains(a.Id))
            .ToListAsync(cancellationToken);
        foreach (var account in accountsToClear)
        {
            account.ActiveCardId = null;
            account.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetCardByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var card = await _db.FulfilCards.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy thẻ thanh toán.");

        var hasTxns = await _db.FulfilTransactions.AnyAsync(t => t.FulfilCardId == id, cancellationToken);
        if (hasTxns)
        {
            throw new BusinessException("Không thể xóa thẻ đang có lịch sử giao dịch.");
        }

        await UnlinkFulfilCardAsync(id, cancellationToken);

        var accounts = await _db.FulfilAccounts.Where(a => a.ActiveCardId == id).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            account.ActiveCardId = null;
            account.UpdatedAt = DateTime.UtcNow;
        }

        _db.FulfilCards.Remove(card);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FulfilTransactionListPageDto> GetTransactionsAsync(
        FulfilTransactionQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = query.PageCurrent < 1 ? 1 : query.PageCurrent;
        var pageSize = query.PageSize < 1 ? 100 : Math.Min(query.PageSize, 200);

        // Count without Includes so Skip/Take is not over a join graph.
        var filtered = ApplyTransactionFilters(
            _db.FulfilTransactions.AsNoTracking(),
            query.AccountId,
            query.CardId,
            query.SourceId,
            query.SourceAccountId,
            query.OrderId,
            query.ShopId,
            query.SellerId,
            query.PaidByUserId,
            query.From,
            query.To,
            query.Keyword,
            query.OwnerUserId);

        var total = await filtered.CountAsync(cancellationToken);
        var summary = await LoadListSummaryAsync(filtered, total, cancellationToken);

        var pageIds = await filtered
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((pageCurrent - 1) * pageSize)
            .Take(pageSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (pageIds.Count == 0)
        {
            return new FulfilTransactionListPageDto
            {
                Items = [],
                Total = total,
                PageCurrent = pageCurrent,
                PageSize = pageSize,
                Summary = summary
            };
        }

        var loaded = await _db.FulfilTransactions
            .AsNoTracking()
            .Include(t => t.FulfilAccount)
            .Include(t => t.FulfilCard!)
                .ThenInclude(c => c.Source)
            .Include(t => t.FulfilCard!)
                .ThenInclude(c => c.SourceAccount)
            .Include(t => t.Order!)
                .ThenInclude(o => o.Owner)
            .Include(t => t.SupplierPurchase)
            .Include(t => t.PaidByUser)
            .Include(t => t.CreatedByUser)
            .Where(t => pageIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var pageOrderIds = loaded
            .Where(t => t.OrderId.HasValue)
            .Select(t => t.OrderId!.Value)
            .Distinct()
            .ToList();
        var orderCostById = await LoadOrderCostLookupAsync(pageOrderIds, cancellationToken);

        var byId = loaded.ToDictionary(t => t.Id);
        var items = pageIds
            .Where(id => byId.ContainsKey(id))
            .Select(id =>
            {
                var t = byId[id];
                decimal? orderCost = null;
                if (t.OrderId.HasValue && orderCostById.TryGetValue(t.OrderId.Value, out var cost))
                {
                    orderCost = cost;
                }

                return MapTransaction(t, orderCost);
            })
            .ToList();

        return new FulfilTransactionListPageDto
        {
            Items = items,
            Total = total,
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            Summary = summary
        };
    }

    /// <summary>
    /// Scalar KPI on the same filtered set as the paged list (not the current page).
    /// Thẻ / Cost-gross / Coupon / Wallet / Club / Points = Σ từng lần mua (ledger).
    /// Đơn / Track / Thank / Profit = 1 lần / <c>OrderId</c> (joint shipment + multi-Shein không nhân đôi).
    /// </summary>
    private async Task<FulfilTransactionListSummaryDto> LoadListSummaryAsync(
        IQueryable<FulfilTransaction> filtered,
        int total,
        CancellationToken cancellationToken)
    {
        if (total == 0)
        {
            return new FulfilTransactionListSummaryDto();
        }

        var money = await filtered
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Card = g.Sum(t =>
                    t.Direction == FulfilTransaction.DirectionCredit ? -t.Amount : t.Amount),
                Cost = g.Sum(t =>
                    t.Source == FulfilTransaction.SourceCostUpdate
                        ? t.Amount
                          + (t.WalletAmount ?? 0m)
                          + (t.CouponAmount ?? 0m)
                          + (t.MembershipAmount ?? 0m)
                          + (t.PointsAmount ?? 0m)
                        : 0m),
                Coupon = g.Sum(t => t.CouponAmount ?? 0m),
                Wallet = g.Sum(t => t.WalletAmount ?? 0m),
                Club = g.Sum(t => t.MembershipAmount ?? 0m),
                Points = g.Sum(t => t.PointsAmount ?? 0m),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var summary = new FulfilTransactionListSummaryDto
        {
            CardAmount = money?.Card ?? 0m,
            CostAmount = money?.Cost ?? 0m,
            CouponAmount = money?.Coupon ?? 0m,
            WalletAmount = money?.Wallet ?? 0m,
            MembershipAmount = money?.Club ?? 0m,
            PointsAmount = money?.Points ?? 0m,
        };

        // Materialize distinct ids so Profit/Track/Thank cannot be inflated by multi-row joins.
        var distinctOrderIds = await filtered
            .Where(t => t.OrderId != null)
            .Select(t => t.OrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        summary.OrderCount = distinctOrderIds.Count;
        if (distinctOrderIds.Count == 0)
        {
            return summary;
        }

        var orderRows = (await _db.Orders.AsNoTracking()
            .Where(o => distinctOrderIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id,
                o.SellerNetAmount,
                o.TotalPaidAmount,
                o.Amount,
                o.ChargedSupplierSubtotal,
                o.ChargedSupplierShippingFee,
                o.ChargedSupplierTax,
                o.ChargedSupplierRetailDeliveryFee,
                o.ChargedSupplierOnTimeGuarantee,
                o.ChargedSupplierTotal,
                o.ChargedSupplierWalletPrice,
                o.ChargedSupplierCouponPrice,
                o.ChargedSupplierMembershipDiscount,
                o.ChargedSupplierPointsDiscount,
                o.BuyTrackAmount,
                o.ThankCardAmount,
            })
            .ToListAsync(cancellationToken))
            .Select(o => new FulfilTransactionListSummaryApply.OrderProfitRow(
                o.Id,
                o.SellerNetAmount,
                o.TotalPaidAmount,
                o.Amount,
                o.ChargedSupplierSubtotal,
                o.ChargedSupplierShippingFee,
                o.ChargedSupplierTax,
                o.ChargedSupplierRetailDeliveryFee,
                o.ChargedSupplierOnTimeGuarantee,
                o.ChargedSupplierTotal,
                o.ChargedSupplierWalletPrice,
                o.ChargedSupplierCouponPrice,
                o.ChargedSupplierMembershipDiscount,
                o.ChargedSupplierPointsDiscount,
                o.BuyTrackAmount,
                o.ThankCardAmount))
            .ToList();

        summary.BuyTrackAmount = orderRows.Sum(o => o.BuyTrackAmount);
        summary.ThankCardAmount = orderRows.Sum(o => o.ThankCardAmount);
        summary.CostAmount += summary.BuyTrackAmount + summary.ThankCardAmount;
        // Same formula as MapTransaction profitAmount — once per OrderId.
        summary.ProfitAmount = FulfilTransactionListSummaryApply.SumDistinctOrderProfits(orderRows);

        return summary;
    }

    /// <summary>
    /// Σ Cost theo đơn cho Profit trên sổ cái = gross NCC + BuyTrack + ThankCard (khớp Orders.totalCost).
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadOrderCostLookupAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var idList = orderIds as IList<Guid> ?? orderIds.ToList();
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => idList.Contains(o.Id))
            .ToListAsync(cancellationToken);

        // Các dòng mua đi kèm để `ResolveOrderTotalCost` còn đường lùi khi snapshot Charged* trống:
        // không có chúng thì cột Profit của sổ cái tụt về rỗng trong khi chính dòng CostUpdate ngay
        // trên hàng đó vẫn hiện đủ `purchaseCostAmount`. Projection thay cho Include vì bảng mua
        // giữ `RawPayload` ~55 KB/dòng mà phép cộng này không cần.
        var purchasesByOrder = (await _db.SupplierPurchases.AsNoTracking()
                .Where(p => p.OrderId != null && idList.Contains(p.OrderId.Value))
                .Select(p => new SupplierPurchase
                {
                    Id = p.Id,
                    OrderId = p.OrderId,
                    IsCharged = p.IsCharged,
                    SupplierSubtotal = p.SupplierSubtotal,
                    SupplierShippingFee = p.SupplierShippingFee,
                    SupplierTax = p.SupplierTax,
                    SupplierRetailDeliveryFee = p.SupplierRetailDeliveryFee,
                    SupplierOnTimeGuarantee = p.SupplierOnTimeGuarantee,
                    SupplierTotal = p.SupplierTotal,
                    SupplierWalletPrice = p.SupplierWalletPrice,
                    SupplierCouponPrice = p.SupplierCouponPrice,
                    SupplierMembershipDiscount = p.SupplierMembershipDiscount,
                    SupplierPointsDiscount = p.SupplierPointsDiscount,
                })
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.OrderId!.Value)
            .ToDictionary(g => g.Key, g => (ICollection<SupplierPurchase>)g.ToList());

        var result = new Dictionary<Guid, decimal>();
        foreach (var order in orders)
        {
            order.SupplierPurchases =
                purchasesByOrder.TryGetValue(order.Id, out var links) ? links : [];

            var totalCost = OrderSupplierCost.ResolveOrderTotalCost(order);
            if (totalCost.HasValue)
            {
                result[order.Id] = totalCost.Value;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<FulfilSpendingSummaryDto> GetSpendingStatsAsync(
        FulfilSpendingQuery query,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyTransactionFilters(
            _db.FulfilTransactions.AsNoTracking(),
            query.AccountId,
            query.CardId,
            query.SourceId,
            query.SourceAccountId,
            orderId: null,
            query.ShopId,
            query.SellerId,
            query.PaidByUserId,
            query.From,
            query.To);

        var breakdown = NormalizeSpendingBreakdown(query.Breakdown);

        // Project only columns needed for KPI + selected breakdown (no Include graphs).
        List<SpendingStatRow> rows = breakdown switch
        {
            "account" => await filtered
                .Select(t => new SpendingStatRow
                {
                    Amount = t.Amount,
                    Direction = t.Direction,
                    Source = t.Source,
                    OccurredAt = t.OccurredAt,
                    OrderId = t.OrderId,
                    FulfilCardId = t.FulfilCardId,
                    FulfilAccountId = t.FulfilAccountId,
                    AccountName = t.FulfilAccount != null ? t.FulfilAccount.Name : null
                })
                .ToListAsync(cancellationToken),
            "shop" => await filtered
                .Select(t => new SpendingStatRow
                {
                    Amount = t.Amount,
                    Direction = t.Direction,
                    Source = t.Source,
                    OccurredAt = t.OccurredAt,
                    OrderId = t.OrderId,
                    FulfilCardId = t.FulfilCardId,
                    FulfilAccountId = t.FulfilAccountId,
                    ShopId = t.Order != null ? t.Order.ShopId : null,
                    ShopName = t.Order != null && t.Order.Shop != null ? t.Order.Shop.Name : null
                })
                .ToListAsync(cancellationToken),
            "paidBy" => await filtered
                .Select(t => new SpendingStatRow
                {
                    Amount = t.Amount,
                    Direction = t.Direction,
                    Source = t.Source,
                    OccurredAt = t.OccurredAt,
                    OrderId = t.OrderId,
                    FulfilCardId = t.FulfilCardId,
                    FulfilAccountId = t.FulfilAccountId,
                    PaidByUserId = t.PaidByUserId,
                    PaidByFullName = t.PaidByUser != null ? t.PaidByUser.FullName : null,
                    PaidByUserName = t.PaidByUser != null ? t.PaidByUser.UserName : null
                })
                .ToListAsync(cancellationToken),
            _ => await filtered
                .Select(t => new SpendingStatRow
                {
                    Amount = t.Amount,
                    Direction = t.Direction,
                    Source = t.Source,
                    OccurredAt = t.OccurredAt,
                    OrderId = t.OrderId,
                    FulfilCardId = t.FulfilCardId,
                    FulfilAccountId = t.FulfilAccountId,
                    AccountName = t.FulfilAccount != null ? t.FulfilAccount.Name : null,
                    CardName = t.FulfilCard != null ? t.FulfilCard.Name : null,
                    CardNumber = t.FulfilCard != null ? t.FulfilCard.CardNumber : null,
                    CardCurrency = t.FulfilCard != null ? t.FulfilCard.Currency : null,
                    SourceName = t.FulfilCard != null && t.FulfilCard.Source != null
                        ? t.FulfilCard.Source.Name
                        : null,
                    SourceAccountName = t.FulfilCard != null && t.FulfilCard.SourceAccount != null
                        ? t.FulfilCard.SourceAccount.Name
                        : null
                })
                .ToListAsync(cancellationToken)
        };

        if (rows.Count == 0)
        {
            return new FulfilSpendingSummaryDto();
        }

        var tz = ResolveVietnamTz();
        var total = rows.Sum(SignedAmountRow);
        var debit = rows.Where(IsDebitRow).Sum(t => t.Amount);
        var credit = rows.Where(IsCreditRow).Sum(t => t.Amount);
        var costUpdate = rows.Where(t => t.Source == FulfilTransaction.SourceCostUpdate).Sum(SignedAmountRow);
        var manual = rows.Where(t => t.Source == FulfilTransaction.SourceManual).Sum(SignedAmountRow);
        var orderCount = rows.Where(t => t.OrderId.HasValue).Select(t => t.OrderId!.Value).Distinct().Count();
        var cardCount = rows
            .Where(t => t.FulfilCardId.HasValue)
            .Select(t => t.FulfilCardId!.Value)
            .Distinct()
            .Count();
        var accountCount = rows
            .Where(t => t.FulfilAccountId.HasValue)
            .Select(t => t.FulfilAccountId!.Value)
            .Distinct()
            .Count();
        List<FulfilSpendingBreakdownItemDto> byCard = [];
        List<FulfilSpendingBreakdownItemDto> byAccount = [];
        List<FulfilSpendingBreakdownItemDto> byShop = [];
        List<FulfilSpendingBreakdownItemDto> byPaidBy = [];

        switch (breakdown)
        {
            case "account":
                byAccount = rows
                    .GroupBy(t => t.FulfilAccountId)
                    .Select(g => BuildBreakdownItemRow(
                        g.Key,
                        g.Key == null
                            ? "Không gắn tài khoản"
                            : (g.First().AccountName?.Trim() is { Length: > 0 } n ? n : "Tài khoản"),
                        extra: null,
                        g))
                    .OrderByDescending(x => x.Amount)
                    .ToList();
                break;

            case "shop":
                byShop = rows
                    .GroupBy(t => t.ShopId)
                    .Select(g => BuildBreakdownItemRow(
                        g.Key,
                        g.Key == null
                            ? "Không gắn shop"
                            : (g.Select(t => t.ShopName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                                   ?.Trim() is { Length: > 0 } n
                                ? n
                                : "Shop"),
                        extra: null,
                        g))
                    .OrderByDescending(x => x.Amount)
                    .ToList();
                break;

            case "paidBy":
                byPaidBy = rows
                    .GroupBy(t => t.PaidByUserId)
                    .Select(g =>
                    {
                        string name;
                        if (g.Key == null)
                        {
                            name = "Chưa gán";
                        }
                        else
                        {
                            var first = g.First();
                            name = !string.IsNullOrWhiteSpace(first.PaidByFullName)
                                ? first.PaidByFullName!
                                : (!string.IsNullOrWhiteSpace(first.PaidByUserName)
                                    ? first.PaidByUserName!
                                    : g.Key.Value.ToString());
                        }

                        return BuildBreakdownItemRow(g.Key, name, extra: null, g);
                    })
                    .OrderByDescending(x => x.Amount)
                    .ToList();
                break;

            default:
                byCard = rows
                    .GroupBy(t => t.FulfilCardId)
                    .Select(g =>
                    {
                        if (g.Key == null)
                        {
                            return BuildBreakdownItemRow(null, "Không gắn thẻ", extra: null, g);
                        }

                        var first = g.First();
                        var masked = MaskCardNumber(first.CardNumber);
                        var sourceName = first.SourceName?.Trim();
                        var sourceAccountName = first.SourceAccountName?.Trim();
                        var displayName = !string.IsNullOrWhiteSpace(sourceAccountName) && !string.IsNullOrWhiteSpace(sourceName)
                            ? $"{sourceName} · {sourceAccountName}"
                            : !string.IsNullOrWhiteSpace(sourceName)
                                ? sourceName
                                : first.CardName?.Trim() is { Length: > 0 } n ? n : "Thẻ không tên";
                        var currency = string.IsNullOrWhiteSpace(first.CardCurrency) ? null : first.CardCurrency.Trim();
                        var accountIdsUsed = g
                            .Where(t => t.FulfilAccountId.HasValue)
                            .Select(t => t.FulfilAccountId!.Value)
                            .Distinct()
                            .ToList();
                        var accountNames = g
                            .Where(t => t.FulfilAccountId.HasValue)
                            .Select(t => t.AccountName?.Trim())
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(2)
                            .ToList();
                        var accountExtra = accountIdsUsed.Count == 0
                            ? "Không gắn TK"
                            : string.Join(", ", accountNames)
                              + (accountIdsUsed.Count > accountNames.Count ? "…" : "");
                        var extra = string.Join(
                            " · ",
                            new[] { masked, currency, accountExtra }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        return BuildBreakdownItemRow(
                            g.Key,
                            displayName,
                            string.IsNullOrWhiteSpace(extra) ? null : extra,
                            g);
                    })
                    .OrderByDescending(x => x.Amount)
                    .ToList();
                break;
        }

        // Chart only needs net amount; keep debit/credit/count for API compatibility.
        var days = rows
            .GroupBy(t => ToVietnamDateKey(t.OccurredAt, tz))
            .OrderBy(g => g.Key)
            .Select(g => new FulfilSpendingDayDto
            {
                Date = g.Key,
                Amount = g.Sum(SignedAmountRow),
                DebitAmount = g.Where(IsDebitRow).Sum(t => t.Amount),
                CreditAmount = g.Where(IsCreditRow).Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .ToList();

        return new FulfilSpendingSummaryDto
        {
            TotalAmount = total,
            DebitAmount = debit,
            CreditAmount = credit,
            TransactionCount = rows.Count,
            OrderCount = orderCount,
            CardCount = cardCount,
            AccountCount = accountCount,
            CostUpdateAmount = costUpdate,
            ManualAmount = manual,
            ByCard = byCard,
            ByAccount = byAccount,
            ByShop = byShop,
            ByPaidBy = byPaidBy,
            Days = days
        };
    }

    private sealed class SpendingStatRow
    {
        public decimal Amount { get; init; }
        public string Direction { get; init; } = FulfilTransaction.DirectionDebit;
        public string Source { get; init; } = string.Empty;
        public DateTime OccurredAt { get; init; }
        public Guid? OrderId { get; init; }
        public Guid? FulfilCardId { get; init; }
        public Guid? FulfilAccountId { get; init; }
        public string? AccountName { get; init; }
        public string? CardName { get; init; }
        public string? CardNumber { get; init; }
        public string? CardCurrency { get; init; }
        public string? SourceName { get; init; }
        public string? SourceAccountName { get; init; }
        public Guid? ShopId { get; init; }
        public string? ShopName { get; init; }
        public Guid? PaidByUserId { get; init; }
        public string? PaidByFullName { get; init; }
        public string? PaidByUserName { get; init; }
    }

    private static string NormalizeSpendingBreakdown(string? breakdown)
    {
        if (string.IsNullOrWhiteSpace(breakdown))
        {
            return "card";
        }

        return breakdown.Trim().ToLowerInvariant() switch
        {
            "account" => "account",
            "shop" => "shop",
            "paidby" or "paid_by" or "paid-by" => "paidBy",
            _ => "card"
        };
    }

    private static FulfilSpendingBreakdownItemDto BuildBreakdownItemRow(
        Guid? id,
        string name,
        string? extra,
        IEnumerable<SpendingStatRow> group)
    {
        var list = group as IList<SpendingStatRow> ?? group.ToList();
        return new FulfilSpendingBreakdownItemDto
        {
            Id = id,
            Name = name,
            Extra = extra,
            Amount = list.Sum(SignedAmountRow),
            DebitAmount = list.Where(IsDebitRow).Sum(t => t.Amount),
            CreditAmount = list.Where(IsCreditRow).Sum(t => t.Amount),
            TransactionCount = list.Count,
            OrderCount = list.Where(t => t.OrderId.HasValue).Select(t => t.OrderId!.Value).Distinct().Count()
        };
    }

    private static bool IsCreditRow(SpendingStatRow t) =>
        string.Equals(t.Direction, FulfilTransaction.DirectionCredit, StringComparison.OrdinalIgnoreCase);

    private static bool IsDebitRow(SpendingStatRow t) => !IsCreditRow(t);

    private static decimal SignedAmountRow(SpendingStatRow t) => IsCreditRow(t) ? -t.Amount : t.Amount;

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

    private static IQueryable<FulfilTransaction> ApplyTransactionFilters(
        IQueryable<FulfilTransaction> q,
        Guid? accountId,
        Guid? cardId,
        Guid? sourceId,
        Guid? sourceAccountId,
        Guid? orderId,
        Guid? shopId,
        Guid? sellerId,
        Guid? paidByUserId,
        DateTime? from,
        DateTime? to,
        string? keyword = null,
        Guid? ownerUserId = null)
    {
        if (accountId.HasValue)
        {
            q = q.Where(t => t.FulfilAccountId == accountId.Value);
        }

        if (cardId.HasValue)
        {
            q = q.Where(t => t.FulfilCardId == cardId.Value);
        }

        if (sourceId.HasValue)
        {
            q = q.Where(t => t.FulfilCard != null && t.FulfilCard.SourceId == sourceId.Value);
        }

        if (sourceAccountId.HasValue)
        {
            q = q.Where(t => t.FulfilCard != null && t.FulfilCard.SourceAccountId == sourceAccountId.Value);
        }

        if (orderId.HasValue)
        {
            q = q.Where(t => t.OrderId == orderId.Value);
        }

        if (shopId.HasValue)
        {
            q = q.Where(t => t.Order != null && t.Order.ShopId == shopId.Value);
        }

        if (ownerUserId.HasValue)
        {
            // Seller scope: own orders + orphan Manual created by self.
            var ownerId = ownerUserId.Value;
            q = q.Where(t =>
                (t.Order != null && t.Order.OwnerId == ownerId) ||
                (t.OrderId == null && t.CreatedByUserId == ownerId));
        }
        else if (sellerId.HasValue)
        {
            q = q.Where(t => t.Order != null && t.Order.OwnerId == sellerId.Value);
        }

        if (paidByUserId.HasValue)
        {
            q = q.Where(t => t.PaidByUserId == paidByUserId.Value);
        }

        if (from.HasValue)
        {
            q = q.Where(t => t.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            q = q.Where(t => t.OccurredAt <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            q = q.Where(t =>
                (t.Note != null && t.Note.Contains(search)) ||
                (t.FulfilAccount != null && t.FulfilAccount.Name.Contains(search)) ||
                (t.FulfilCard != null && (
                    t.FulfilCard.Name.Contains(search) ||
                    t.FulfilCard.CardNumber.Contains(search) ||
                    (t.FulfilCard.HolderName != null && t.FulfilCard.HolderName.Contains(search)) ||
                    (t.FulfilCard.Source != null && t.FulfilCard.Source.Name.Contains(search)) ||
                    (t.FulfilCard.SourceAccount != null && t.FulfilCard.SourceAccount.Name.Contains(search)))) ||
                (t.SupplierPurchase != null && (
                    (t.SupplierPurchase.SupplierOrderNo != null
                        && t.SupplierPurchase.SupplierOrderNo.Contains(search)) ||
                    (t.SupplierPurchase.TrackingNo != null
                        && t.SupplierPurchase.TrackingNo.Contains(search)) ||
                    (t.SupplierPurchase.Carrier != null
                        && t.SupplierPurchase.Carrier.Contains(search)))) ||
                (t.Order != null && (
                    (t.Order.PlatformOrderId != null && t.Order.PlatformOrderId.Contains(search)) ||
                    t.Order.FourSellerOrderId.ToString().Contains(search) ||
                    (t.Order.TrackingNo != null && t.Order.TrackingNo.Contains(search)) ||
                    (t.Order.ShippingService != null && t.Order.ShippingService.Contains(search)) ||
                    t.Order.SupplierPurchases.Any(p =>
                        (p.SupplierOrderNo != null && p.SupplierOrderNo.Contains(search)) ||
                        (p.TrackingNo != null && p.TrackingNo.Contains(search)) ||
                        (p.Carrier != null && p.Carrier.Contains(search))) ||
                    (t.Order.Owner != null && (
                        (t.Order.Owner.FullName != null && t.Order.Owner.FullName.Contains(search)) ||
                        (t.Order.Owner.UserName != null && t.Order.Owner.UserName.Contains(search)) ||
                        (t.Order.Owner.Email != null && t.Order.Owner.Email.Contains(search)))))) ||
                (t.PaidByUser != null && (
                    (t.PaidByUser.FullName != null && t.PaidByUser.FullName.Contains(search)) ||
                    (t.PaidByUser.UserName != null && t.PaidByUser.UserName.Contains(search)) ||
                    (t.PaidByUser.Email != null && t.PaidByUser.Email.Contains(search)))) ||
                (t.CreatedByUser.FullName != null && t.CreatedByUser.FullName.Contains(search)) ||
                (t.CreatedByUser.UserName != null && t.CreatedByUser.UserName.Contains(search)) ||
                (t.CreatedByUser.Email != null && t.CreatedByUser.Email.Contains(search)));
        }

        return q;
    }

    /// <inheritdoc />
    public async Task<FulfilTransactionDto> GetTransactionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null)
    {
        var txn = await LoadTransactionAsync(id, cancellationToken);
        EnsureTransactionOwnerScope(txn, ownerUserId);
        decimal? orderCost = null;
        if (txn.OrderId.HasValue)
        {
            var lookup = await LoadOrderCostLookupAsync([txn.OrderId.Value], cancellationToken);
            if (lookup.TryGetValue(txn.OrderId.Value, out var cost))
            {
                orderCost = cost;
            }
        }

        return MapTransaction(txn, orderCost);
    }

    /// <inheritdoc />
    public async Task<FulfilTransactionDto> CreateTransactionAsync(
        CreateFulfilTransactionDto dto,
        Guid createdByUserId,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null)
    {
        await ValidateAccountCardPairAsync(dto.FulfilAccountId, dto.FulfilCardId, cancellationToken);
        if (dto.OrderId.HasValue)
        {
            await EnsureOrderExistsAsync(dto.OrderId.Value, cancellationToken, ownerUserId);
        }

        await EnsureUserExistsAsync(dto.PaidByUserId, "Người xử lý đơn", cancellationToken);

        if (dto.Amount < 0)
        {
            throw new BusinessException("Số tiền phải >= 0.");
        }

        var direction = NormalizeDirection(dto.Direction);

        var txn = new FulfilTransaction
        {
            FulfilAccountId = dto.FulfilAccountId,
            FulfilCardId = dto.FulfilCardId,
            OrderId = dto.OrderId,
            Amount = dto.Amount,
            Direction = direction,
            Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant(),
            Note = TrimOrNull(dto.Note),
            Source = FulfilTransaction.SourceManual,
            PaidByUserId = dto.PaidByUserId ?? createdByUserId,
            CreatedByUserId = createdByUserId,
            OccurredAt = dto.OccurredAt ?? DateTime.UtcNow
        };

        _db.FulfilTransactions.Add(txn);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetTransactionByIdAsync(txn.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FulfilTransactionDto> UpdateTransactionAsync(
        Guid id,
        UpdateFulfilTransactionDto dto,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null,
        bool allowCostUpdateMutate = true)
    {
        var txn = await _db.FulfilTransactions
            .Include(t => t.Order)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy giao dịch.");

        EnsureTransactionOwnerScope(txn, ownerUserId);
        EnsureCostUpdateMutateAllowed(txn, allowCostUpdateMutate);

        if (dto.Amount < 0)
        {
            throw new BusinessException("Số tiền phải >= 0.");
        }

        await ValidateAccountCardPairAsync(dto.FulfilAccountId, dto.FulfilCardId, cancellationToken);
        await EnsureUserExistsAsync(dto.PaidByUserId, "Người xử lý đơn", cancellationToken);

        if (txn.Source == FulfilTransaction.SourceCostUpdate)
        {
            if (dto.OrderId.HasValue && dto.OrderId != txn.OrderId)
            {
                throw new BusinessException("Không thể đổi đơn hàng của giao dịch tự động từ Cập nhật phí.");
            }
        }
        else if (dto.OrderId.HasValue)
        {
            await EnsureOrderExistsAsync(dto.OrderId.Value, cancellationToken, ownerUserId);
            txn.OrderId = dto.OrderId;
        }
        else
        {
            txn.OrderId = null;
        }

        txn.FulfilAccountId = dto.FulfilAccountId;
        txn.FulfilCardId = dto.FulfilCardId;
        txn.Amount = dto.Amount;
        txn.Direction = txn.Source == FulfilTransaction.SourceCostUpdate
            ? FulfilTransaction.DirectionDebit
            : NormalizeDirection(dto.Direction);
        txn.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant();
        txn.Note = TrimOrNull(dto.Note);
        txn.PaidByUserId = dto.PaidByUserId;
        txn.OccurredAt = dto.OccurredAt;
        txn.UpdatedAt = DateTime.UtcNow;

        // CostUpdate: lan xuống lần mua + đơn để lượt sync sau không revert.
        if (txn.Source == FulfilTransaction.SourceCostUpdate && dto.PaidByUserId.HasValue)
        {
            if (txn.SupplierPurchaseId is { } purchaseId)
            {
                var purchase = await _db.SupplierPurchases
                    .FirstOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);
                if (purchase is not null)
                {
                    purchase.PaidByUserId = dto.PaidByUserId;
                    purchase.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (txn.Order is { PaidByUserId: null } order)
            {
                order.PaidByUserId = dto.PaidByUserId;
                order.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetTransactionByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteTransactionAsync(
        Guid id,
        CancellationToken cancellationToken = default,
        Guid? ownerUserId = null,
        bool allowCostUpdateMutate = true)
    {
        var txn = await _db.FulfilTransactions
            .Include(t => t.Order)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy giao dịch.");

        EnsureTransactionOwnerScope(txn, ownerUserId);
        EnsureCostUpdateMutateAllowed(txn, allowCostUpdateMutate);

        await ClearCostUpdateOrderFulfilFieldsAsync([txn], cancellationToken);
        _db.FulfilTransactions.Remove(txn);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> BulkDeleteTransactionsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một giao dịch để xóa.");
        }

        var txns = await _db.FulfilTransactions
            .Where(t => distinctIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        if (txns.Count == 0)
        {
            throw new NotFoundException("Không tìm thấy giao dịch.");
        }

        await ClearCostUpdateOrderFulfilFieldsAsync(txns, cancellationToken);
        _db.FulfilTransactions.RemoveRange(txns);
        await _db.SaveChangesAsync(cancellationToken);
        return txns.Count;
    }

    /// <summary>
    /// Xóa dòng CostUpdate trên sổ cái = dọn sạch lần mua liên quan để sync gắn lại được sạch:
    /// <list type="bullet">
    /// <item>Xóa hẳn dòng <c>SupplierPurchases</c> (cả <c>shein</c> lẫn <c>manual</c>) gắn với dòng chi.</item>
    /// <item>Tính lại giá mua / mốc SHEIN còn bằng chứng trên đơn.</item>
    /// <item>Đơn hết sạch dòng CostUpdate → clear fulfil attribution; hết giá mua → clear mốc Mua.</item>
    /// </list>
    /// Joint Shipment giữ account/card khi vẫn còn dòng CostUpdate anh em. Lượt sync sau Upsert lại
    /// billno SHEIN như lần đầu — không còn khóa <c>ignored</c>/<c>ledger_deleted</c>.
    /// </summary>
    private async Task ClearCostUpdateOrderFulfilFieldsAsync(
        IReadOnlyList<FulfilTransaction> txns,
        CancellationToken cancellationToken)
    {
        var deleting = txns
            .Where(t => t.Source == FulfilTransaction.SourceCostUpdate && t.OrderId.HasValue)
            .ToList();
        if (deleting.Count == 0) return;

        var deletingIds = deleting.Select(t => t.Id).ToHashSet();
        var candidateOrderIds = deleting.Select(t => t.OrderId!.Value).Distinct().ToList();
        var purchaseIdsToDrop = deleting
            .Where(t => t.SupplierPurchaseId.HasValue)
            .Select(t => t.SupplierPurchaseId!.Value)
            .Distinct()
            .ToHashSet();

        var survivingOrderIds = (await _db.FulfilTransactions
            .AsNoTracking()
            .Where(t => t.Source == FulfilTransaction.SourceCostUpdate
                && t.OrderId.HasValue
                && candidateOrderIds.Contains(t.OrderId.Value)
                && !deletingIds.Contains(t.Id))
            .Select(t => t.OrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var orders = await _db.Orders
            .Include(o => o.SupplierPurchases)
            .Where(o => candidateOrderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var removedPurchaseIds = new HashSet<Guid>();

        foreach (var order in orders)
        {
            var doomed = (order.SupplierPurchases ?? [])
                .Where(p => purchaseIdsToDrop.Contains(p.Id))
                .ToList();

            foreach (var purchase in doomed)
            {
                order.SupplierPurchases!.Remove(purchase);
                _db.SupplierPurchases.Remove(purchase);
                removedPurchaseIds.Add(purchase.Id);
            }

            OrderSupplierCost.ApplyAggregates(order);
            OrderSheinMilestoneEvidence.ClearMilestonesWithoutLinkEvidence(order);
            OrderPurchaseEvidence.ClearAttributionWhenNothingPurchased(order);
        }

        // Dòng mua đã gỡ khỏi đơn trước đó (OrderId null) nhưng vẫn còn FK trên CostUpdate — xoá nốt.
        var leftoverIds = purchaseIdsToDrop.Where(id => !removedPurchaseIds.Contains(id)).ToList();
        if (leftoverIds.Count > 0)
        {
            var leftovers = await _db.SupplierPurchases
                .Where(p => leftoverIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
            if (leftovers.Count > 0)
            {
                _db.SupplierPurchases.RemoveRange(leftovers);
            }
        }

        foreach (var order in orders)
        {
            if (!survivingOrderIds.Contains(order.Id))
            {
                order.FulfilAccountId = null;
                order.FulfilCardId = null;
                foreach (var purchase in order.SupplierPurchases ?? [])
                {
                    purchase.FulfilAccountId = null;
                    purchase.FulfilCardId = null;
                }

                if (!(order.SupplierPurchases ?? []).Any(p =>
                        p.IsCharged
                        && (p.SupplierTotal.HasValue || (p.SupplierWalletPrice ?? 0m) > 0m)))
                {
                    OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.FulfilPurchased);
                    OrderFulfilTimeline.Normalize(order);
                }

                // Xoá dòng sổ cái ≠ đơn chưa từng được mua — chỉ wipe người mua khi stamp mua đã hết.
                if (order.FulfilPurchasedAt is null)
                {
                    order.PaidByUserId = null;
                    foreach (var purchase in order.SupplierPurchases ?? [])
                    {
                        purchase.PaidByUserId = null;
                    }
                }
            }

            order.UpdatedAt = now;
        }
    }

    private async Task UnlinkFulfilAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var orders = await _db.Orders
            .Where(o => o.FulfilAccountId == accountId)
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.FulfilAccountId = null;
            order.FulfilCardId = null;
            order.UpdatedAt = now;
        }

        var links = await _db.SupplierPurchases
            .Where(l => l.FulfilAccountId == accountId)
            .ToListAsync(cancellationToken);
        foreach (var link in links)
        {
            link.FulfilAccountId = null;
            link.FulfilCardId = null;
        }
    }

    private async Task UnlinkFulfilCardAsync(Guid cardId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var orders = await _db.Orders
            .Where(o => o.FulfilCardId == cardId)
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.FulfilCardId = null;
            order.UpdatedAt = now;
        }

        var links = await _db.SupplierPurchases
            .Where(l => l.FulfilCardId == cardId)
            .ToListAsync(cancellationToken);
        foreach (var link in links)
        {
            link.FulfilCardId = null;
        }
    }

    private async Task SetActiveCardInternalAsync(Guid accountId, Guid? cardId, CancellationToken cancellationToken)
    {
        var account = await _db.FulfilAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");

        if (cardId.HasValue)
        {
            var linked = await _db.FulfilAccountCards.AnyAsync(
                l => l.FulfilAccountId == accountId && l.FulfilCardId == cardId.Value,
                cancellationToken);
            if (!linked)
            {
                throw new BusinessException("Thẻ chưa được gắn với tài khoản này.");
            }

            var cardActive = await _db.FulfilCards.AnyAsync(c => c.Id == cardId.Value && c.IsActive, cancellationToken);
            if (!cardActive)
            {
                throw new BusinessException("Thẻ đang vô hiệu, không thể đặt làm thẻ đang dùng.");
            }

            account.ActiveCardId = cardId.Value;
        }
        else
        {
            account.ActiveCardId = null;
        }

        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateAccountCardPairAsync(Guid? accountId, Guid? cardId, CancellationToken cancellationToken)
    {
        if (accountId.HasValue
            && !await _db.FulfilAccounts.AnyAsync(a => a.Id == accountId.Value, cancellationToken))
        {
            throw new NotFoundException("Không tìm thấy tài khoản fulfil.");
        }

        if (!cardId.HasValue)
        {
            return;
        }

        var card = await _db.FulfilCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId.Value, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy thẻ thanh toán.");

        if (!card.IsActive)
        {
            throw new BusinessException("Thẻ đang vô hiệu.");
        }

        if (!accountId.HasValue)
        {
            return;
        }

        var linked = await _db.FulfilAccountCards.AnyAsync(
            l => l.FulfilAccountId == accountId.Value && l.FulfilCardId == cardId.Value,
            cancellationToken);
        if (!linked)
        {
            throw new BusinessException("Thẻ chưa được gắn với tài khoản mua hàng đã chọn.");
        }
    }

    private async Task EnsureAccountsExistAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return;
        }

        var count = await _db.FulfilAccounts.CountAsync(a => accountIds.Contains(a.Id), cancellationToken);
        if (count != accountIds.Count)
        {
            throw new NotFoundException("Một hoặc nhiều tài khoản fulfil không tồn tại.");
        }
    }

    private async Task AssignCardSourceAsync(
        FulfilCard card,
        Guid sourceId,
        Guid? sourceAccountId,
        CancellationToken cancellationToken)
    {
        var source = await _db.ReceivingCardSources
            .FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null || !source.IsActive)
        {
            throw new NotFoundException("Không tìm thấy nguồn thẻ.");
        }

        if (source.SupportsAccounts)
        {
            if (!sourceAccountId.HasValue)
            {
                throw new BusinessException($"Nguồn {source.Name} yêu cầu chọn tài khoản nguồn.");
            }

            var sourceAccount = await _db.ReceivingSourceAccounts
                .FirstOrDefaultAsync(a => a.Id == sourceAccountId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tài khoản nguồn.");

            if (sourceAccount.SourceId != source.Id)
            {
                throw new BusinessException("Tài khoản nguồn không thuộc nguồn thẻ đã chọn.");
            }

            if (!sourceAccount.IsActive && card.SourceAccountId != sourceAccount.Id)
            {
                throw new BusinessException("Tài khoản nguồn đang bị vô hiệu.");
            }

            card.SourceId = source.Id;
            card.SourceAccountId = sourceAccount.Id;
            card.Name = sourceAccount.Name.Trim();
            return;
        }

        if (sourceAccountId.HasValue)
        {
            throw new BusinessException($"Nguồn {source.Name} không dùng tài khoản con.");
        }

        card.SourceId = source.Id;
        card.SourceAccountId = null;
        card.Name = source.Name.Trim();
    }

    private async Task EnsureUsersExistAsync(IReadOnlyCollection<Guid> userIds, string label, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        var count = await _db.Users.CountAsync(u => userIds.Contains(u.Id), cancellationToken);
        if (count != userIds.Count)
        {
            throw new NotFoundException($"Một hoặc nhiều {label.ToLowerInvariant()} không tồn tại.");
        }
    }

    private async Task EnsureOrderExistsAsync(
        Guid orderId,
        CancellationToken cancellationToken,
        Guid? ownerUserId = null)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.OwnerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        if (ownerUserId.HasValue && order.OwnerId != ownerUserId.Value)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }
    }

    private static void EnsureTransactionOwnerScope(FulfilTransaction txn, Guid? ownerUserId)
    {
        if (!ownerUserId.HasValue)
        {
            return;
        }

        var inScope =
            (txn.Order != null && txn.Order.OwnerId == ownerUserId.Value) ||
            (txn.OrderId == null && txn.CreatedByUserId == ownerUserId.Value);
        if (!inScope)
        {
            throw new NotFoundException("Không tìm thấy giao dịch.");
        }
    }

    private static void EnsureCostUpdateMutateAllowed(FulfilTransaction txn, bool allowCostUpdateMutate)
    {
        if (!allowCostUpdateMutate && txn.Source == FulfilTransaction.SourceCostUpdate)
        {
            throw new BusinessException("Không thể sửa/xóa giao dịch Cập nhật phí.");
        }
    }

    private async Task EnsureUserExistsAsync(Guid? userId, string label, CancellationToken cancellationToken)
    {
        if (!userId.HasValue)
        {
            return;
        }

        if (!await _db.Users.AnyAsync(u => u.Id == userId.Value, cancellationToken))
        {
            throw new NotFoundException($"Không tìm thấy {label}.");
        }
    }

    /// <summary>
    /// Username and email must each be unique across fulfil accounts (case-insensitive; null/empty skipped).
    /// Same account may use the same value for both username and email.
    /// </summary>
    private async Task EnsureUniqueUsernameAndEmailAsync(
        string? username,
        string? email,
        Guid? excludeAccountId,
        CancellationToken cancellationToken)
    {
        var excludeId = excludeAccountId;

        if (username != null)
        {
            var usernameKey = username.ToLowerInvariant();
            var conflictName = await _db.FulfilAccounts
                .Where(a => a.Username != null
                            && a.Username.ToLower() == usernameKey
                            && (excludeId == null || a.Id != excludeId))
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflictName != null)
            {
                throw new BusinessException($"Username đã tồn tại trên tài khoản \"{conflictName}\".");
            }
        }

        if (email != null)
        {
            var emailKey = email.ToLowerInvariant();
            var conflictName = await _db.FulfilAccounts
                .Where(a => a.Email != null
                            && a.Email.ToLower() == emailKey
                            && (excludeId == null || a.Id != excludeId))
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflictName != null)
            {
                throw new BusinessException($"Email đã tồn tại trên tài khoản \"{conflictName}\".");
            }
        }
    }

    private async Task<FulfilAccount> LoadAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.FulfilAccounts
            .AsNoTracking()
            .Include(a => a.Assignees)
                .ThenInclude(x => x.User)
            .Include(a => a.ActiveCard!)
                .ThenInclude(c => c.Source)
            .Include(a => a.ActiveCard!)
                .ThenInclude(c => c.SourceAccount)
            .Include(a => a.CardLinks)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản fulfil.");
    }

    private async Task<FulfilCard> LoadCardAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.FulfilCards
            .AsNoTracking()
            .Include(c => c.Source)
            .Include(c => c.SourceAccount)
            .Include(c => c.AccountLinks)
                .ThenInclude(l => l.FulfilAccount)
            .Include(c => c.Assignees)
                .ThenInclude(a => a.User)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy thẻ thanh toán.");
    }

    private async Task<FulfilTransaction> LoadTransactionAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.FulfilTransactions
            .AsNoTracking()
            .Include(t => t.FulfilAccount)
            .Include(t => t.FulfilCard!)
                .ThenInclude(c => c.Source)
            .Include(t => t.FulfilCard!)
                .ThenInclude(c => c.SourceAccount)
            .Include(t => t.Order!)
                .ThenInclude(o => o.Owner)
            .Include(t => t.SupplierPurchase)
            .Include(t => t.PaidByUser)
            .Include(t => t.CreatedByUser)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy giao dịch.");
    }

    private static List<Guid> NormalizeIds(IEnumerable<Guid>? ids) =>
        (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();

    private static void SyncAccountLinks(FulfilCard card, IReadOnlyCollection<Guid> accountIds)
    {
        var next = accountIds.ToHashSet();
        foreach (var link in card.AccountLinks.Where(l => !next.Contains(l.FulfilAccountId)).ToList())
        {
            card.AccountLinks.Remove(link);
        }

        var existing = card.AccountLinks.Select(l => l.FulfilAccountId).ToHashSet();
        foreach (var accountId in next.Except(existing))
        {
            card.AccountLinks.Add(new FulfilAccountCard
            {
                FulfilAccountId = accountId,
                FulfilCardId = card.Id
            });
        }
    }

    private static void SyncAssigneeLinks(FulfilCard card, IReadOnlyCollection<Guid> userIds)
    {
        var next = userIds.ToHashSet();
        foreach (var link in card.Assignees.Where(l => !next.Contains(l.UserId)).ToList())
        {
            card.Assignees.Remove(link);
        }

        var existing = card.Assignees.Select(l => l.UserId).ToHashSet();
        foreach (var userId in next.Except(existing))
        {
            card.Assignees.Add(new FulfilCardAssignee
            {
                UserId = userId,
                FulfilCardId = card.Id
            });
        }
    }

    private static void SyncAccountAssigneeLinks(FulfilAccount account, IReadOnlyCollection<Guid> userIds)
    {
        var next = userIds.ToHashSet();
        foreach (var link in account.Assignees.Where(l => !next.Contains(l.UserId)).ToList())
        {
            account.Assignees.Remove(link);
        }

        var existing = account.Assignees.Select(l => l.UserId).ToHashSet();
        foreach (var userId in next.Except(existing))
        {
            account.Assignees.Add(new FulfilAccountAssignee
            {
                UserId = userId,
                FulfilAccountId = account.Id
            });
        }
    }

    private static FulfilAccountDto MapAccount(FulfilAccount a)
    {
        var assignees = (a.Assignees ?? [])
            .OrderBy(x => DisplayName(x.User))
            .ToList();

        var headers = ParseSheinHeaders(a.SheinHeadersJson);
        var missingHeaders = SheinSessionKeys.MissingRequiredHeaders(headers);
        var hasCookie = !string.IsNullOrWhiteSpace(a.SheinCookie);

        return new FulfilAccountDto
        {
            Id = a.Id,
            Name = a.Name,
            Username = a.Username,
            Password = a.Password,
            Email = a.Email,
            PassMail = a.PassMail,
            TwoFactorAuth = a.TwoFactorAuth,
            AssigneeUserIds = assignees.Select(x => x.UserId).ToList(),
            AssigneeUserNames = assignees.Select(x => DisplayName(x.User) ?? x.UserId.ToString()).ToList(),
            IsActive = a.IsActive,
            ActiveCardId = a.ActiveCardId,
            ActiveCardName = a.ActiveCard?.Name,
            ActiveCardNumberMasked = a.ActiveCard == null ? null : MaskCardNumber(a.ActiveCard.CardNumber),
            ActiveCardSourceName = a.ActiveCard?.Source?.Name,
            ActiveCardSourceAccountName = a.ActiveCard?.SourceAccount?.Name,
            ActiveCardCurrency = string.IsNullOrWhiteSpace(a.ActiveCard?.Currency) ? null : a.ActiveCard!.Currency,
            CardCount = a.CardLinks?.Count ?? 0,
            SheinProxy = a.SheinProxy,
            SheinOrigin = a.SheinOrigin,
            SheinHeaderCount = headers.Count,
            SheinHasCookie = hasCookie,
            SheinSessionReady = missingHeaders.Count == 0 && hasCookie,
            SheinMissingHeaders = missingHeaders,
            SheinSessionCapturedAt = a.SheinSessionCapturedAt,
            SheinSessionLastCheckedAt = a.SheinSessionLastCheckedAt,
            SheinSessionLastError = a.SheinSessionLastError,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt
        };
    }

    /// <summary>Reads the stored header map; a corrupt blob is treated as no session.</summary>
    private static Dictionary<string, string> ParseSheinHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? []
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static FulfilCardDto MapCard(FulfilCard c, int orderCount)
    {
        var accountLinks = (c.AccountLinks ?? [])
            .OrderBy(l => l.FulfilAccount?.Name)
            .ToList();
        var assignees = (c.Assignees ?? [])
            .OrderBy(a => DisplayName(a.User))
            .ToList();

        return new FulfilCardDto
        {
            Id = c.Id,
            Name = c.Name,
            CardNumber = c.CardNumber,
            CardNumberMasked = MaskCardNumber(c.CardNumber),
            CardBrand = c.CardBrand,
            ExpiryMonth = c.ExpiryMonth,
            ExpiryYear = c.ExpiryYear,
            HolderName = c.HolderName,
            Cvv = c.Cvv,
            Currency = string.IsNullOrWhiteSpace(c.Currency) ? "USD" : c.Currency,
            SourceId = c.SourceId,
            SourceName = c.Source?.Name ?? string.Empty,
            SourceCode = c.Source?.Code ?? string.Empty,
            SourceAccountId = c.SourceAccountId,
            SourceAccountName = c.SourceAccount?.Name,
            AccountIds = accountLinks.Select(l => l.FulfilAccountId).ToList(),
            AccountNames = accountLinks.Select(l => l.FulfilAccount?.Name ?? l.FulfilAccountId.ToString()).ToList(),
            AssigneeUserIds = assignees.Select(a => a.UserId).ToList(),
            AssigneeUserNames = assignees.Select(a => DisplayName(a.User) ?? a.UserId.ToString()).ToList(),
            IsActive = c.IsActive,
            OrderCount = orderCount,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };
    }

    private static FulfilTransactionDto MapTransaction(
        FulfilTransaction t,
        decimal? orderCost = null)
    {
        decimal? revenue = t.Order == null ? null : OrderRevenue.Resolve(t.Order);
        // Profit = Revenue − Cost (gross NCC + BuyTrack + ThankCard) — khớp totalCost trên đơn.
        decimal? profit = revenue.HasValue && orderCost.HasValue
            ? revenue.Value - orderCost.Value
            : null;

        var buyTrack = t.Order?.BuyTrackAmount ?? 0m;
        var thankCard = t.Order?.ThankCardAmount ?? 0m;
        var isCostUpdate = string.Equals(t.Source, FulfilTransaction.SourceCostUpdate, StringComparison.OrdinalIgnoreCase);
        decimal? purchaseCost = null;
        if (isCostUpdate)
        {
            var grossNcc = OrderSupplierCost.ResolveGrossPurchaseCost(
                t.SupplierPurchase,
                t.Amount,
                t.WalletAmount,
                t.CouponAmount,
                t.MembershipAmount,
                t.PointsAmount);
            if (grossNcc.HasValue)
            {
                purchaseCost = grossNcc.Value + buyTrack + thankCard;
            }
        }

        return new()
        {
            Id = t.Id,
            FulfilAccountId = t.FulfilAccountId,
            FulfilAccountName = t.FulfilAccount?.Name,
            FulfilCardId = t.FulfilCardId,
            FulfilCardName = t.FulfilCard?.Name,
            FulfilCardNumberMasked = t.FulfilCard == null ? null : MaskCardNumber(t.FulfilCard.CardNumber),
            FulfilCardSourceName = t.FulfilCard?.Source?.Name,
            FulfilCardSourceAccountName = t.FulfilCard?.SourceAccount?.Name,
            FulfilCardCurrency = string.IsNullOrWhiteSpace(t.FulfilCard?.Currency) ? null : t.FulfilCard!.Currency,
            OrderId = t.OrderId,
            PlatformOrderId = t.Order?.PlatformOrderId,
            FourSellerOrderId = t.Order?.FourSellerOrderId,
            OrderSupplierLinkId = t.SupplierPurchaseId,
            ManualLinkedAt = t.SupplierPurchase?.ManualLinkedAt,
            // Mỗi dòng chi luôn trỏ vào đúng lần mua nó trả tiền cho, kể cả đơn Joint Shipment.
            SupplierOrderNo = t.SupplierPurchase?.SupplierOrderNo,
            SupplierOrderUrl = t.SupplierPurchase?.SupplierOrderUrl,
            SupplierStatus = t.SupplierPurchase?.SupplierStatus,
            SupplierStatusRaw = t.SupplierPurchase?.SupplierStatusRaw,
            TrackingNo = t.SupplierPurchase?.TrackingNo,
            Carrier = ResolveCarrierName(t.SupplierPurchase?.Carrier, t.Order?.ShippingService),
            TrackingKind = t.SupplierPurchase?.TrackingKind,
            SellerId = t.Order?.OwnerId,
            SellerName = t.Order?.Owner == null ? null : DisplayName(t.Order.Owner),
            CouponAmount = t.CouponAmount,
            WalletAmount = t.WalletAmount,
            MembershipAmount = t.MembershipAmount,
            PointsAmount = t.PointsAmount,
            RevenueAmount = revenue,
            ProfitAmount = profit,
            PurchaseCostAmount = purchaseCost,
            // Chi thật dòng CostUpdate = thẻ + ví + club; Manual không có snapshot → null.
            TrueSpendAmount = isCostUpdate
                ? t.Amount + (t.WalletAmount ?? 0m) + (t.MembershipAmount ?? 0m)
                : null,
            BuyTrackAmount = buyTrack,
            ThankCardAmount = thankCard,
            SupplierShippingFee = isCostUpdate ? t.SupplierPurchase?.SupplierShippingFee : null,
            SupplierTax = isCostUpdate ? t.SupplierPurchase?.SupplierTax : null,
            Amount = t.Amount,
            Direction = string.IsNullOrWhiteSpace(t.Direction) ? FulfilTransaction.DirectionDebit : t.Direction,
            Currency = t.Currency,
            Note = t.Note,
            Source = t.Source,
            PaidByUserId = t.PaidByUserId,
            PaidByUserName = DisplayName(t.PaidByUser),
            CreatedByUserId = t.CreatedByUserId,
            CreatedByUserName = DisplayName(t.CreatedByUser),
            OccurredAt = t.OccurredAt,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
    }

    /// <summary>
    /// Tên carrier hiển thị: ưu tiên SHEIN <c>track_info.name</c>; fallback ShippingService
    /// đã Mark Ship nếu không phải nhãn phương thức giao ("Seller Shipping" / "TikTok Shipping").
    /// </summary>
    private static string? ResolveCarrierName(string? purchaseCarrier, string? orderShippingService)
    {
        if (!string.IsNullOrWhiteSpace(purchaseCarrier))
        {
            return purchaseCarrier.Trim();
        }

        var service = orderShippingService?.Trim();
        if (string.IsNullOrEmpty(service))
        {
            return null;
        }

        var lower = service.ToLowerInvariant();
        if (lower.Contains("seller shipping", StringComparison.Ordinal)
            || lower.Contains("tiktok shipping", StringComparison.Ordinal))
        {
            return null;
        }

        return service;
    }

    internal static string MaskCardNumber(string? number)
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

    private static string NormalizeCardNumber(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new BusinessException("Số thẻ không hợp lệ (12–19 chữ số).");
        }

        return digits;
    }

    private static string NormalizeDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)
            || direction.Equals(FulfilTransaction.DirectionDebit, StringComparison.OrdinalIgnoreCase))
        {
            return FulfilTransaction.DirectionDebit;
        }

        if (direction.Equals(FulfilTransaction.DirectionCredit, StringComparison.OrdinalIgnoreCase))
        {
            return FulfilTransaction.DirectionCredit;
        }

        throw new BusinessException("Loại giao dịch phải là Debit (trừ tiền) hoặc Credit (nạp tiền).");
    }

    private static string NormalizeBrand(string brand)
    {
        var value = brand.Trim();
        if (value.Equals("Visa", StringComparison.OrdinalIgnoreCase))
        {
            return "Visa";
        }

        if (value.Equals("Master", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Mastercard", StringComparison.OrdinalIgnoreCase)
            || value.Equals("MasterCard", StringComparison.OrdinalIgnoreCase))
        {
            return "Master";
        }

        throw new BusinessException("Loại thẻ chỉ hỗ trợ Visa hoặc Master.");
    }

    private static string NormalizeCurrency(string? currency)
    {
        var value = currency?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(value) ? "USD" : value;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = TrimOrNull(value);
            if (trimmed is not null) return trimmed;
        }
        return null;
    }

    private static string? DisplayName(ApplicationUser? user)
    {
        if (user is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName;
    }
}
