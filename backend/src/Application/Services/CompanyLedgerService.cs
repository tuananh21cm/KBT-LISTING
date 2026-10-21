using Application.CompanyLedger;
using Application.DTOs.CompanyLedger;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Company operating ledger. Shop and seller are optional and independent.
/// </summary>
public class CompanyLedgerService : ICompanyLedgerService
{
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>Initializes the service.</summary>
    public CompanyLedgerService(IApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<CompanyLedgerCategoryDto> GetCategories() => CompanyLedgerCatalog.All;

    /// <inheritdoc />
    public async Task<CompanyLedgerListDto> ListAsync(
        CompanyLedgerQuery query,
        CancellationToken cancellationToken = default)
    {
        var rows = ApplyFilter(_context.CompanyLedgerEntries.AsNoTracking(), query);
        var items = await Project(rows)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return new CompanyLedgerListDto
        {
            Items = items,
            Summary = Summarize(items)
        };
    }

    /// <inheritdoc />
    public async Task<CompanyLedgerEntryDto> CreateAsync(
        SaveCompanyLedgerEntryDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        Normalize(dto);
        await ValidateReferencesAsync(dto.ShopId, dto.OwnerUserId, cancellationToken);

        if (IsShopAcquisition(dto))
        {
            var existing = await FindAcquisitionAsync(dto.ShopId!.Value, cancellationToken);
            if (existing is not null)
            {
                ApplyForm(existing, dto, keepOwnerWhenOmitted: false);
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return await LoadDtoAsync(existing.Id, cancellationToken);
            }
        }

        var entry = new CompanyLedgerEntry
        {
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        };
        ApplyForm(entry, dto, keepOwnerWhenOmitted: false);
        _context.CompanyLedgerEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
        return await LoadDtoAsync(entry.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompanyLedgerEntryDto> UpdateAsync(
        Guid id,
        SaveCompanyLedgerEntryDto dto,
        CancellationToken cancellationToken = default)
    {
        Normalize(dto);
        var entry = await _context.CompanyLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new BusinessException("Không tìm thấy khoản thu/chi.");

        await ValidateReferencesAsync(dto.ShopId, dto.OwnerUserId, cancellationToken);

        if (IsShopAcquisition(dto))
        {
            var other = await FindAcquisitionAsync(dto.ShopId!.Value, cancellationToken);
            if (other is not null && other.Id != entry.Id)
            {
                throw new BusinessException("Shop này đã có dòng giá mua. Sửa dòng đó thay vì tạo thêm.");
            }
        }

        ApplyForm(entry, dto, keepOwnerWhenOmitted: false);
        entry.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return await LoadDtoAsync(entry.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _context.CompanyLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new BusinessException("Không tìm thấy khoản thu/chi.");
        _context.CompanyLedgerEntries.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompanyLedgerEntryDto?> UpsertShopAcquisitionAsync(
        Guid shopId,
        UpsertShopAcquisitionCostDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var shop = await _context.Shops.AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => new { s.Id, s.Name, s.OwnerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessException("Không tìm thấy shop.");

        var existing = await FindAcquisitionAsync(shopId, cancellationToken);
        if (dto.Amount is null || dto.Amount <= 0)
        {
            if (existing is null)
            {
                return null;
            }

            _context.CompanyLedgerEntries.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (existing is null)
        {
            existing = new CompanyLedgerEntry
            {
                ShopId = shop.Id,
                OwnerUserId = shop.OwnerId,
                Source = CompanyLedgerEntry.SourceShopAcquisition,
                Direction = CompanyLedgerEntry.DirectionDebit,
                CategoryCode = CompanyLedgerEntry.CategoryShopAcquisition,
                Title = TrimTitle($"Giá mua {shop.Name}"),
                Currency = "USD",
                CreatedByUserId = actorUserId,
                CreatedAt = DateTime.UtcNow,
                OccurredAt = dto.OccurredAt ?? DateTime.UtcNow,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                Amount = dto.Amount.Value
            };
            _context.CompanyLedgerEntries.Add(existing);
        }
        else
        {
            existing.Amount = dto.Amount.Value;
            existing.Currency = "USD";
            existing.Direction = CompanyLedgerEntry.DirectionDebit;
            existing.CategoryCode = CompanyLedgerEntry.CategoryShopAcquisition;
            existing.Source = CompanyLedgerEntry.SourceShopAcquisition;
            existing.ShopId = shop.Id;
            if (dto.OccurredAt.HasValue)
            {
                existing.OccurredAt = dto.OccurredAt.Value;
            }

            if (dto.Note is not null)
            {
                existing.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            }

            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return await LoadDtoAsync(existing.Id, cancellationToken);
    }

    private IQueryable<CompanyLedgerEntry> ApplyFilter(IQueryable<CompanyLedgerEntry> query, CompanyLedgerQuery filter)
    {
        if (filter.SellerScopeUserId is Guid sellerId)
        {
            query = query.Where(e =>
                e.OwnerUserId == sellerId
                || _context.Shops.Any(s => s.Id == e.ShopId && s.OwnerId == sellerId));
        }

        if (filter.From.HasValue)
        {
            query = query.Where(e => e.OccurredAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(e => e.OccurredAt <= filter.To.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Direction))
        {
            var direction = filter.Direction.Trim();
            query = query.Where(e => e.Direction == direction);
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryCode))
        {
            var category = filter.CategoryCode.Trim();
            query = query.Where(e => e.CategoryCode == category);
        }

        if (filter.OwnerUnassigned)
        {
            query = query.Where(e => e.OwnerUserId == null);
        }
        else if (filter.OwnerUserId.HasValue)
        {
            query = query.Where(e => e.OwnerUserId == filter.OwnerUserId.Value);
        }

        if (filter.ShopUnassigned)
        {
            query = query.Where(e => e.ShopId == null);
        }
        else if (filter.ShopId.HasValue)
        {
            query = query.Where(e => e.ShopId == filter.ShopId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(e =>
                e.Title.ToLower().Contains(term) ||
                (e.Note != null && e.Note.ToLower().Contains(term)));
        }

        return query;
    }

    private static IQueryable<CompanyLedgerEntryDto> Project(IQueryable<CompanyLedgerEntry> query) =>
        query.Select(e => new CompanyLedgerEntryDto
        {
            Id = e.Id,
            Amount = e.Amount,
            Currency = e.Currency,
            Direction = e.Direction,
            CategoryCode = e.CategoryCode,
            Title = e.Title,
            Note = e.Note,
            OccurredAt = e.OccurredAt,
            ShopId = e.ShopId,
            ShopName = e.Shop != null ? e.Shop.Name : null,
            OwnerUserId = e.OwnerUserId,
            OwnerFullName = e.OwnerUser != null ? e.OwnerUser.FullName : null,
            OwnerUserName = e.OwnerUser != null ? e.OwnerUser.UserName : null,
            Source = e.Source,
            CreatedByUserId = e.CreatedByUserId,
            CreatedAt = e.CreatedAt
        });

    private async Task<CompanyLedgerEntryDto> LoadDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = await Project(_context.CompanyLedgerEntries.AsNoTracking().Where(e => e.Id == id))
            .FirstOrDefaultAsync(cancellationToken);
        return dto ?? throw new BusinessException("Không tìm thấy khoản thu/chi.");
    }

    private Task<CompanyLedgerEntry?> FindAcquisitionAsync(Guid shopId, CancellationToken cancellationToken) =>
        _context.CompanyLedgerEntries.FirstOrDefaultAsync(
            e => e.ShopId == shopId && e.Source == CompanyLedgerEntry.SourceShopAcquisition,
            cancellationToken);

    private static bool IsShopAcquisition(SaveCompanyLedgerEntryDto dto) =>
        dto.CategoryCode == CompanyLedgerEntry.CategoryShopAcquisition && dto.ShopId.HasValue;

    private static void Normalize(SaveCompanyLedgerEntryDto dto)
    {
        dto.Direction = (dto.Direction ?? string.Empty).Trim();
        dto.CategoryCode = (dto.CategoryCode ?? string.Empty).Trim();
        dto.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant();
        dto.Title = (dto.Title ?? string.Empty).Trim();
        dto.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

        if (!CompanyLedgerCatalog.IsKnown(dto.CategoryCode))
        {
            throw new BusinessException("Loại khoản thu/chi không hợp lệ.");
        }

        if (!CompanyLedgerCatalog.IsDirection(dto.Direction))
        {
            throw new BusinessException("Chiều thu/chi không hợp lệ.");
        }

        if (!CompanyLedgerCatalog.IsCurrency(dto.Currency))
        {
            throw new BusinessException("Tiền tệ chỉ nhận USD hoặc VND.");
        }

        if (dto.Amount <= 0)
        {
            throw new BusinessException("Số tiền phải lớn hơn 0.");
        }

        if (dto.Title.Length > 200)
        {
            throw new BusinessException("Nội dung tối đa 200 ký tự.");
        }

        if (dto.OccurredAt == default)
        {
            throw new BusinessException("Chọn ngày phát sinh.");
        }

        if (dto.OccurredAt.Kind == DateTimeKind.Unspecified)
        {
            dto.OccurredAt = DateTime.SpecifyKind(dto.OccurredAt, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Writes form fields. Shop acquisition with a shop locks direction, category and source.
    /// Clearing the shop releases the unique acquisition source.
    /// </summary>
    private static void ApplyForm(CompanyLedgerEntry entry, SaveCompanyLedgerEntryDto dto, bool keepOwnerWhenOmitted)
    {
        entry.Amount = dto.Amount;
        entry.Currency = dto.Currency;
        entry.Title = dto.Title;
        entry.Note = dto.Note;
        entry.OccurredAt = dto.OccurredAt;
        entry.ShopId = dto.ShopId;
        if (!keepOwnerWhenOmitted)
        {
            entry.OwnerUserId = dto.OwnerUserId;
        }

        if (IsShopAcquisition(dto))
        {
            entry.Direction = CompanyLedgerEntry.DirectionDebit;
            entry.CategoryCode = CompanyLedgerEntry.CategoryShopAcquisition;
            entry.Source = CompanyLedgerEntry.SourceShopAcquisition;
            entry.Currency = "USD";
            return;
        }

        entry.Direction = dto.Direction;
        entry.CategoryCode = dto.CategoryCode;
        entry.Source = CompanyLedgerEntry.SourceManual;
    }

    private async Task ValidateReferencesAsync(
        Guid? shopId,
        Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (shopId.HasValue)
        {
            var shopExists = await _context.Shops.AsNoTracking()
                .AnyAsync(s => s.Id == shopId.Value, cancellationToken);
            if (!shopExists)
            {
                throw new BusinessException("Shop không tồn tại.");
            }
        }

        if (!ownerUserId.HasValue)
        {
            return;
        }

        var user = await _userManager.FindByIdAsync(ownerUserId.Value.ToString());
        if (user is null || !user.IsActive)
        {
            throw new BusinessException("Seller không hợp lệ hoặc đã bị vô hiệu hóa.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(RoleNames.Seller))
        {
            throw new BusinessException("Người được gán phải có vai trò Seller.");
        }
    }

    private static CompanyLedgerSummaryDto Summarize(IReadOnlyList<CompanyLedgerEntryDto> items) =>
        new()
        {
            Count = items.Count,
            ByCurrency = items
                .GroupBy(i => i.Currency)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var credit = g.Where(i => i.Direction == CompanyLedgerEntry.DirectionCredit).Sum(i => i.Amount);
                    var debit = g.Where(i => i.Direction == CompanyLedgerEntry.DirectionDebit).Sum(i => i.Amount);
                    return new CompanyLedgerCurrencyTotalDto
                    {
                        Currency = g.Key,
                        CreditTotal = credit,
                        DebitTotal = debit,
                        Net = credit - debit
                    };
                })
                .ToList()
        };

    private static string TrimTitle(string title) =>
        title.Length <= 200 ? title : title[..200];
}
