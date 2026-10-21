using Application.DTOs.Auth;
using Application.DTOs.Role;
using Application.Interfaces;
using Application.Validators.Auth;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using static Shared.Constants.FourSellerCookieLoginStatuses;

namespace Application.Services;

/// <summary>
/// User profile service implementation.
/// </summary>
public class UserService : IUserService
{
    /// <summary>Kiểm tra từng dòng import tại chỗ để một dòng hỏng không chặn cả lô.</summary>
    private static readonly ImportAdminUserItemValidator ImportItemValidator = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ITokenService _tokenService;
    private readonly IFulfilPinHasher _pinHasher;
    private readonly IFourSellerSessionCache _sessionCache;
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes the user service.
    /// </summary>
    public UserService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ITokenService tokenService,
        IFulfilPinHasher pinHasher,
        IFourSellerSessionCache sessionCache,
        IApplicationDbContext db,
        IMapper mapper)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _tokenService = tokenService;
        _pinHasher = pinHasher;
        _sessionCache = sessionCache;
        _db = db;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("Tài khoản không tồn tại hoặc đã bị vô hiệu hóa.");
        }

        var dto = _mapper.Map<CurrentUserDto>(user);
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await _tokenService.GetUserPermissionsAsync(userId, cancellationToken);

        // A team member works on the team account's data, so the 4Seller cookie it sees is the team's.
        var teamOwner = user.TeamOwnerId is Guid teamOwnerId
            ? await _userManager.FindByIdAsync(teamOwnerId.ToString())
            : null;
        var cookieHolder = teamOwner ?? user;

        return new CurrentUserDto
        {
            Id = dto.Id,
            Username = dto.Username,
            Email = dto.Email,
            FullName = dto.FullName,
            Roles = roles.ToList(),
            Permissions = permissions,
            HasFourSellerCookie = !string.IsNullOrWhiteSpace(cookieHolder.FourSellerCookie),
            FourSellerCookieLoginStatus = ResolveLoginStatus(cookieHolder),
            Theme = UiThemeModes.Normalize(user.Theme),
            TeamOwnerUserName = teamOwner?.UserName,
        };
    }

    /// <inheritdoc />
    public async Task<string> UpdateThemeAsync(
        Guid userId,
        UpdateThemeDto dto,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("Tài khoản không tồn tại hoặc đã bị vô hiệu hóa.");
        }

        var theme = UiThemeModes.Normalize(dto.Theme);
        user.Theme = theme;
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            throw new BusinessException(errors.FirstOrDefault() ?? "Cập nhật theme thất bại.");
        }

        return theme;
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            throw new BusinessException(errors.FirstOrDefault() ?? "Đổi mật khẩu thất bại.");
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        await _tokenService.RevokeAllRefreshTokensAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateFourSellerCookieAsync(
        Guid userId,
        UpdateFourSellerCookieDto dto,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        user.FourSellerCookie = NormalizeCookie(dto.FourSellerCookie);
        user.FourSellerCookieLoginStatus = string.IsNullOrWhiteSpace(user.FourSellerCookie)
            ? None
            : Unchecked;
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            throw new BusinessException(errors.FirstOrDefault() ?? "Cập nhật cookie 4Seller thất bại.");
        }

        _sessionCache.Invalidate(userId);
        return !string.IsNullOrWhiteSpace(user.FourSellerCookie);
    }

    /// <inheritdoc />
    public async Task<bool> ClearFourSellerCookieAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        if (string.IsNullOrWhiteSpace(user.FourSellerCookie) &&
            user.FourSellerCookieLoginStatus == None)
        {
            return false;
        }

        var hadCookie = !string.IsNullOrWhiteSpace(user.FourSellerCookie);
        user.FourSellerCookie = null;
        user.FourSellerCookieLoginStatus = None;
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            throw new BusinessException(errors.FirstOrDefault() ?? "Xóa cookie 4Seller thất bại.");
        }

        _sessionCache.Invalidate(userId);
        return hadCookie;
    }

    /// <inheritdoc />
    public async Task<FourSellerCookieDto> GetFourSellerCookieAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        if (string.IsNullOrWhiteSpace(user.FourSellerCookie))
        {
            throw new BusinessException(
                "Bạn chưa lưu cookie 4Seller. Vào Hồ sơ cá nhân để cập nhật cookie trước khi dùng extension.");
        }

        return new FourSellerCookieDto
        {
            FourSellerCookie = user.FourSellerCookie,
            FourSellerCookieLoginStatus = ResolveLoginStatus(user)
        };
    }

    /// <inheritdoc />
    public async Task SetFourSellerCookieLoginStatusAsync(
        Guid userId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        var normalized = NormalizeLoginStatus(status);
        if (user.FourSellerCookieLoginStatus == normalized)
        {
            return;
        }

        user.FourSellerCookieLoginStatus = normalized;
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            throw new BusinessException(errors.FirstOrDefault() ?? "Cập nhật trạng thái cookie 4Seller thất bại.");
        }

        if (!string.Equals(normalized, LoggedIn, StringComparison.Ordinal))
        {
            _sessionCache.Invalidate(userId);
        }
    }

    /// <inheritdoc />
    public async Task<List<UserSummaryDto>> GetSellersAsync(CancellationToken cancellationToken = default)
    {
        var sellers = await _userManager.GetUsersInRoleAsync(RoleNames.Seller);

        return sellers
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.UserName)
            .Select(u => new UserSummaryDto
            {
                Id = u.Id,
                UserName = u.UserName ?? string.Empty,
                FullName = u.FullName,
                Email = u.Email,
                IsActive = u.IsActive
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<UserSummaryDto>> GetUserOptionsAsync(
        IReadOnlyCollection<string>? roles = null,
        CancellationToken cancellationToken = default)
    {
        var wantedRoles = roles?
            .Select(r => r?.Trim())
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wantedRoles is { Count: > 0 })
        {
            // Một query / role (bảng user nhỏ); role không tồn tại → store trả rỗng, không throw.
            var byId = new Dictionary<Guid, ApplicationUser>();
            foreach (var role in wantedRoles)
            {
                foreach (var user in await _userManager.GetUsersInRoleAsync(role!))
                {
                    byId[user.Id] = user;
                }
            }

            return byId.Values
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .ThenBy(u => u.UserName)
                .Select(u => new UserSummaryDto
                {
                    Id = u.Id,
                    UserName = u.UserName ?? string.Empty,
                    FullName = u.FullName,
                    IsActive = u.IsActive
                })
                .ToList();
        }

        return await _userManager.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.UserName)
            .Select(u => new UserSummaryDto
            {
                Id = u.Id,
                UserName = u.UserName ?? string.Empty,
                FullName = u.FullName,
                IsActive = u.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<AdminUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _userManager.Users
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        var userNameById = users.ToDictionary(u => u.Id, u => u.UserName);
        var result = new List<AdminUserDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var dto = MapToAdminUserDto(user, roles);
            dto.TeamOwnerUserName = user.TeamOwnerId is Guid teamOwnerId ? userNameById.GetValueOrDefault(teamOwnerId) : null;
            result.Add(dto);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<AdminUserDto> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(userId);
        var roles = await _userManager.GetRolesAsync(user);
        return MapToAdminUserDto(user, roles);
    }

    /// <inheritdoc />
    public async Task<AdminUserDto> CreateUserAsync(CreateAdminUserDto dto, CancellationToken cancellationToken = default)
    {
        await ValidateRolesAsync(dto.Roles);

        var cookie = NormalizeCookie(dto.FourSellerCookie);
        var pinHash = await ResolvePinHashForWriteAsync(dto.FulfilPin, excludeUserId: null, cancellationToken);
        var user = new ApplicationUser
        {
            UserName = dto.UserName,
            NormalizedUserName = dto.UserName.ToUpperInvariant(),
            Email = dto.Email,
            NormalizedEmail = dto.Email.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = dto.FullName,
            IsActive = true,
            FourSellerCookie = cookie,
            FourSellerCookieLoginStatus = cookie is null ? None : Unchecked,
            FourSellerProxy = NormalizeProxy(dto.FourSellerProxy),
            FulfilPinHash = pinHash
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            throw new BusinessException(result.Errors.FirstOrDefault()?.Description ?? "Tạo người dùng thất bại.");
        }

        await _userManager.AddToRolesAsync(user, dto.Roles);
        return MapToAdminUserDto(user, dto.Roles);
    }

    /// <inheritdoc />
    public async Task<AdminUserDto> UpdateUserAsync(Guid userId, UpdateAdminUserDto dto, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(userId);
        await ValidateRolesAsync(dto.Roles);

        if (userId == currentUserId && !dto.IsActive)
        {
            throw new BusinessException("Không thể vô hiệu hóa tài khoản của chính mình.");
        }

        var wasActive = user.IsActive;
        var previousPinHash = user.FulfilPinHash;
        user.Email = dto.Email;
        user.NormalizedEmail = dto.Email.ToUpperInvariant();
        user.FullName = dto.FullName;
        user.IsActive = dto.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        if (dto.FourSellerCookie is not null)
        {
            user.FourSellerCookie = NormalizeCookie(dto.FourSellerCookie);
            user.FourSellerCookieLoginStatus = string.IsNullOrWhiteSpace(user.FourSellerCookie)
                ? None
                : Unchecked;
        }

        if (dto.FourSellerProxy is not null)
        {
            user.FourSellerProxy = NormalizeProxy(dto.FourSellerProxy);
        }

        var pinChanged = false;
        if (dto.FulfilPin is not null)
        {
            var nextPinHash = await ResolvePinHashForWriteAsync(dto.FulfilPin, userId, cancellationToken);
            if (!string.Equals(previousPinHash, nextPinHash, StringComparison.Ordinal))
            {
                user.FulfilPinHash = nextPinHash;
                pinChanged = true;
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw new BusinessException(updateResult.Errors.FirstOrDefault()?.Description ?? "Cập nhật người dùng thất bại.");
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removeResult.Succeeded)
        {
            throw new BusinessException("Cập nhật vai trò thất bại.");
        }

        await _userManager.AddToRolesAsync(user, dto.Roles);

        if ((wasActive && !user.IsActive) || pinChanged)
        {
            if (wasActive && !user.IsActive)
            {
                await _userManager.UpdateSecurityStampAsync(user);
            }

            await _tokenService.RevokeAllRefreshTokensAsync(userId, cancellationToken);
        }

        return MapToAdminUserDto(user, dto.Roles);
    }

    /// <inheritdoc />
    public async Task<ImportAdminUsersResultDto> ImportUsersAsync(
        ImportAdminUsersDto dto,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportAdminUsersResultDto();

        var roleNames = await _roleManager.Roles
            .Where(r => r.Name != null)
            .Select(r => r.Name!)
            .ToListAsync(cancellationToken);
        var rolesByName = roleNames.ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);

        var seenUserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < dto.Items.Count; index++)
        {
            var item = dto.Items[index];
            var userName = item.UserName?.Trim() ?? string.Empty;
            var email = item.Email?.Trim() ?? string.Empty;
            var fullName = item.FullName?.Trim() ?? string.Empty;
            var password = item.Password ?? string.Empty;

            void Fail(string error) => result.Failed.Add(new ImportAdminUserErrorDto
            {
                Index = index,
                UserName = userName,
                Error = error
            });

            var normalized = new ImportAdminUserItemDto
            {
                UserName = userName,
                Email = email,
                FullName = fullName,
                Password = password,
                Roles = item.Roles
            };

            var validation = ImportItemValidator.Validate(normalized);
            if (!validation.IsValid)
            {
                Fail(validation.Errors[0].ErrorMessage);
                continue;
            }

            if (!seenUserNames.Add(userName))
            {
                Fail("Tên đăng nhập bị lặp trong file.");
                continue;
            }

            if (!seenEmails.Add(email))
            {
                Fail("Email bị lặp trong file.");
                continue;
            }

            var roles = new List<string>();
            var unknownRole = (string?)null;
            foreach (var role in normalized.Roles
                         .Select(r => r?.Trim() ?? string.Empty)
                         .Where(r => r.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!rolesByName.TryGetValue(role, out var canonical))
                {
                    unknownRole = role;
                    break;
                }

                roles.Add(canonical);
            }

            if (unknownRole is not null)
            {
                Fail($"Vai trò '{unknownRole}' không tồn tại.");
                continue;
            }

            if (roles.Count == 0)
            {
                Fail("Phải chọn ít nhất một vai trò.");
                continue;
            }

            var byUserName = await _userManager.FindByNameAsync(userName);
            var byEmail = await _userManager.FindByEmailAsync(email);
            if (byUserName is not null && byEmail is not null && byUserName.Id != byEmail.Id)
            {
                Fail("Tên đăng nhập và email đang thuộc hai tài khoản khác nhau.");
                continue;
            }

            var existing = byUserName ?? byEmail;

            // Đổi mật khẩu sẽ revoke token của chính người đang import → chặn để không tự đăng xuất giữa chừng.
            if (existing is not null && existing.Id == currentUserId)
            {
                Fail("Không thể import chính tài khoản đang đăng nhập.");
                continue;
            }

            var rowError = existing is null
                ? await ImportCreateUserAsync(userName, email, fullName, password, roles)
                : await ImportUpdateUserAsync(existing, email, fullName, password, roles, cancellationToken);

            if (rowError is not null)
            {
                Fail(rowError);
                continue;
            }

            if (existing is null)
            {
                result.Created++;
            }
            else
            {
                result.Updated++;
            }
        }

        return result;
    }

    /// <summary>Tạo user từ một dòng import. Trả về thông báo lỗi khi thất bại, <c>null</c> khi thành công.</summary>
    private async Task<string?> ImportCreateUserAsync(
        string userName,
        string email,
        string fullName,
        string password,
        IReadOnlyList<string> roles)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true,
            FourSellerCookieLoginStatus = None
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return createResult.Errors.FirstOrDefault()?.Description ?? "Tạo người dùng thất bại.";
        }

        var roleResult = await _userManager.AddToRolesAsync(user, roles);
        return roleResult.Succeeded
            ? null
            : roleResult.Errors.FirstOrDefault()?.Description ?? "Gán vai trò thất bại.";
    }

    /// <summary>
    /// Cập nhật user sẵn có từ một dòng import: email, họ tên, vai trò và mật khẩu.
    /// Giữ nguyên <c>IsActive</c>, cookie 4Seller, proxy và mã PIN.
    /// </summary>
    private async Task<string?> ImportUpdateUserAsync(
        ApplicationUser user,
        string email,
        string fullName,
        string password,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        user.Email = email;
        user.NormalizedEmail = email.ToUpperInvariant();
        user.FullName = fullName;
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return updateResult.Errors.FirstOrDefault()?.Description ?? "Cập nhật người dùng thất bại.";
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Except(roles, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = roles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();

        if (toRemove.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removeResult.Succeeded)
            {
                return "Cập nhật vai trò thất bại.";
            }
        }

        if (toAdd.Count > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(user, toAdd);
            if (!addResult.Succeeded)
            {
                return addResult.Errors.FirstOrDefault()?.Description ?? "Gán vai trò thất bại.";
            }
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var passwordResult = await _userManager.ResetPasswordAsync(user, token, password);
        if (!passwordResult.Succeeded)
        {
            return passwordResult.Errors.FirstOrDefault()?.Description ?? "Đặt lại mật khẩu thất bại.";
        }

        await _tokenService.RevokeAllRefreshTokensAsync(user.Id, cancellationToken);
        return null;
    }

    /// <inheritdoc />
    public async Task DeleteUserAsync(Guid userId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        if (userId == currentUserId)
        {
            throw new BusinessException("Không thể xóa tài khoản của chính mình.");
        }

        var user = await FindUserAsync(userId);
        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(RoleNames.Admin))
        {
            var otherAdmins = (await _userManager.GetUsersInRoleAsync(RoleNames.Admin))
                .Count(u => u.Id != userId);
            if (otherAdmins == 0)
            {
                throw new BusinessException("Không thể xóa Admin cuối cùng trong hệ thống.");
            }
        }

        await EnsureUserCanBeHardDeletedAsync(userId, cancellationToken);

        // OwnerId không có FK tới AspNetUsers — dọn tay để tránh orphan.
        await _db.ListingSyncWatermarks
            .Where(w => w.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await _db.TikTokShopSyncLogs
            .Where(x => x.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await _db.ShopTaskCompletions
            .Where(x => x.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await _tokenService.RevokeAllRefreshTokensAsync(userId, cancellationToken);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new BusinessException(
                result.Errors.FirstOrDefault()?.Description ?? "Xóa người dùng thất bại.");
        }
    }

    /// <summary>
    /// Chặn xóa cứng khi còn dữ liệu nghiệp vụ gắn Restrict FK — tránh cascade/wipe shop·đơn
    /// hoặc lỗi DB khó hiểu. Admin vẫn có thể tắt <c>IsActive</c> trên form Sửa.
    /// </summary>
    private async Task EnsureUserCanBeHardDeletedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();

        if (await _db.Shops.AnyAsync(s => s.OwnerId == userId, cancellationToken))
        {
            blockers.Add("còn shop");
        }

        if (await _db.Orders.AnyAsync(o => o.OwnerId == userId, cancellationToken))
        {
            blockers.Add("còn đơn hàng");
        }

        if (await _db.Listings.AnyAsync(l => l.OwnerId == userId, cancellationToken))
        {
            blockers.Add("còn listing");
        }

        if (await _db.ListingAddOrigins.AnyAsync(o => o.OwnerId == userId, cancellationToken))
        {
            blockers.Add("còn lịch sử thêm listing");
        }

        if (await _db.CompanyLedgerEntries.AnyAsync(e => e.CreatedByUserId == userId, cancellationToken))
        {
            blockers.Add("còn dòng sổ cái do user tạo");
        }

        if (await _db.FulfilTransactions.AnyAsync(t => t.CreatedByUserId == userId, cancellationToken))
        {
            blockers.Add("còn giao dịch fulfil do user tạo");
        }

        if (await _db.ReceivingBankTransactions.AnyAsync(t => t.CreatedByUserId == userId, cancellationToken))
        {
            blockers.Add("còn giao dịch tiền về do user tạo");
        }

        if (await _db.OrderFulfilAttempts.AnyAsync(a => a.UserId == userId, cancellationToken))
        {
            blockers.Add("còn lịch sử fulfil attempt");
        }

        if (await _db.ShippingTemplates.AnyAsync(t => t.CreatedById == userId, cancellationToken))
        {
            blockers.Add("còn shipping template");
        }

        if (await _db.PricingTemplates.AnyAsync(t => t.CreatedById == userId, cancellationToken))
        {
            blockers.Add("còn pricing template");
        }

        if (await _db.ScrapeBatches.AnyAsync(b => b.CreatedById == userId, cancellationToken))
        {
            blockers.Add("còn scrape batch");
        }

        if (await _db.ScrapeDrafts.AnyAsync(d => d.CreatedById == userId, cancellationToken))
        {
            blockers.Add("còn scrape draft");
        }

        if (await _db.UspsScanBatches.AnyAsync(b => b.CreatedByUserId == userId, cancellationToken))
        {
            blockers.Add("còn lô quét USPS");
        }

        if (blockers.Count == 0)
        {
            return;
        }

        throw new BusinessException(
            $"Không thể xóa người dùng vì {string.Join(", ", blockers)}. " +
            "Hãy chuyển quyền/xóa dữ liệu liên quan trước, hoặc tắt Hoạt động (IsActive) trên form Sửa.");
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(Guid userId, ResetPasswordDto dto, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(userId);
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);

        if (!result.Succeeded)
        {
            throw new BusinessException(result.Errors.FirstOrDefault()?.Description ?? "Đặt lại mật khẩu thất bại.");
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        await _tokenService.RevokeAllRefreshTokensAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<RoleOptionDto>> GetRoleOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _roleManager.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleOptionDto
            {
                Id = r.Id,
                Name = r.Name ?? string.Empty
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<ApplicationUser> FindUserAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        return user;
    }

    private async Task ValidateRolesAsync(IReadOnlyList<string> roles)
    {
        foreach (var role in roles.Distinct())
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                throw new BusinessException($"Vai trò '{role}' không tồn tại.");
            }
        }
    }

    private static AdminUserDto MapToAdminUserDto(ApplicationUser user, IEnumerable<string> roles) => new()
    {
        Id = user.Id,
        UserName = user.UserName ?? string.Empty,
        FullName = user.FullName,
        Email = user.Email,
        IsActive = user.IsActive,
        Roles = roles.ToList(),
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
        HasFourSellerCookie = !string.IsNullOrWhiteSpace(user.FourSellerCookie),
        FourSellerCookieLoginStatus = ResolveLoginStatus(user),
        FourSellerProxy = user.FourSellerProxy,
        HasFulfilPin = !string.IsNullOrWhiteSpace(user.FulfilPinHash)
    };

    /// <summary>
    /// Null/omit → no change when updating (caller must skip); empty → clear; 4 digits → hash.
    /// Create path: null/empty → no PIN.
    /// </summary>
    private async Task<string?> ResolvePinHashForWriteAsync(
        string? fulfilPin,
        Guid? excludeUserId,
        CancellationToken cancellationToken)
    {
        if (fulfilPin is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(fulfilPin))
        {
            return null;
        }

        var normalized = Shared.Helpers.FulfilPin.Normalize(fulfilPin);
        if (!Shared.Helpers.FulfilPin.IsValid(normalized))
        {
            throw new BusinessException("Mã PIN phải gồm đúng 4 chữ số.");
        }

        var hash = _pinHasher.Hash(normalized);
        var taken = await _userManager.Users
            .AnyAsync(
                u => u.FulfilPinHash == hash && (excludeUserId == null || u.Id != excludeUserId),
                cancellationToken);
        if (taken)
        {
            throw new BusinessException("Mã PIN này đã được dùng bởi tài khoản khác.");
        }

        return hash;
    }

    private static string? NormalizeCookie(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        return cookie.Trim();
    }

    private static string? NormalizeProxy(string? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy))
        {
            return null;
        }

        return proxy.Trim();
    }

    private static string ResolveLoginStatus(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.FourSellerCookie))
        {
            return None;
        }

        return NormalizeLoginStatus(user.FourSellerCookieLoginStatus);
    }

    private static string NormalizeLoginStatus(string? status) =>
        status switch
        {
            LoggedIn => LoggedIn,
            Expired => Expired,
            Unchecked => Unchecked,
            None => None,
            _ => Unchecked
        };
}
