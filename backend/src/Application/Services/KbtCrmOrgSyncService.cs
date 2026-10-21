using Application.DTOs.KbtCrm;
using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <inheritdoc />
public class KbtCrmOrgSyncService : IKbtCrmOrgSyncService
{
    private const string TikTokPlatformCode = "TikTok";

    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IKbtCrmClient _client;
    private readonly ISettingProvider _settings;
    private readonly IShopService _shopService;
    private readonly ILogger<KbtCrmOrgSyncService> _logger;

    public KbtCrmOrgSyncService(
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IKbtCrmClient client,
        ISettingProvider settings,
        IShopService shopService,
        ILogger<KbtCrmOrgSyncService> logger)
    {
        _context = context;
        _userManager = userManager;
        _client = client;
        _settings = settings;
        _shopService = shopService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<KbtCrmSyncResultDto> RunAsync(string trigger, CancellationToken cancellationToken = default)
    {
        var values = await _settings.GetValuesAsync(
            [KbtCrmSyncSettingKeys.BaseUrl, KbtCrmSyncSettingKeys.AgentSecret], cancellationToken);
        var baseUrl = values.GetValueOrDefault(KbtCrmSyncSettingKeys.BaseUrl)?.Trim();
        var secret = values.GetValueOrDefault(KbtCrmSyncSettingKeys.AgentSecret)?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret))
        {
            throw new BusinessException(
                $"Chưa cấu hình {KbtCrmSyncSettingKeys.BaseUrl} / {KbtCrmSyncSettingKeys.AgentSecret} trong Cài đặt.");
        }

        var org = await _client.GetOrgAsync(baseUrl, secret, cancellationToken);
        var result = new KbtCrmSyncResultDto
        {
            CrmGroups = org.Groups.Count,
            CrmUsers = org.Groups.Sum(g => g.Members.Count) + org.UsersWithoutGroup.Count,
            CrmShops = org.Shops.Count,
        };

        var platform = await _context.Platforms
            .FirstOrDefaultAsync(p => p.Code == TikTokPlatformCode, cancellationToken)
            ?? throw new BusinessException("Chưa có Platform TikTok để tạo shop.");

        // CRM user id → local owner (team account for members, self for team accounts / ungrouped users).
        var ownerByCrmUserId = new Dictionary<long, Guid>();
        var ownerByGroupName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in org.Groups)
        {
            var teamAccount = group.Members.FirstOrDefault(m => m.TaiKhoanTo)
                ?? group.Members.FirstOrDefault(m => IsSeller(m) && m.ConLam);
            if (teamAccount is null)
            {
                result.Skipped.Add($"Tổ '{group.Name}': không có tài khoản tổ.");
                continue;
            }

            var owner = await UpsertUserAsync(teamAccount, teamOwnerId: null, result, cancellationToken);
            if (owner is null)
            {
                result.Skipped.Add($"Tổ '{group.Name}': tài khoản tổ '{teamAccount.Ten}' không có username.");
                continue;
            }

            ownerByGroupName[group.Name] = owner.Id;
            ownerByCrmUserId[teamAccount.UserId] = owner.Id;

            foreach (var member in group.Members.Where(m => m.UserId != teamAccount.UserId))
            {
                if (!IsSeller(member))
                {
                    result.Skipped.Add($"User '{member.Ten}' ({member.VaiTro}): chỉ đồng bộ vai trò seller.");
                    continue;
                }

                var local = await UpsertUserAsync(member, owner.Id, result, cancellationToken);
                if (local is not null)
                {
                    ownerByCrmUserId[member.UserId] = owner.Id;
                }
            }
        }

        foreach (var lone in org.UsersWithoutGroup.Where(u => u.Shops.Count > 0))
        {
            if (!IsSeller(lone))
            {
                continue;
            }

            var local = await UpsertUserAsync(lone, teamOwnerId: null, result, cancellationToken);
            if (local is not null)
            {
                ownerByCrmUserId[lone.UserId] = local.Id;
            }
        }

        foreach (var crmShop in org.Shops)
        {
            var code = crmShop.ShopCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(crmShop.Platform)
                && !crmShop.Platform.Equals("tiktok", StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add($"Shop {code}: platform '{crmShop.Platform}' không hỗ trợ.");
                continue;
            }

            Guid? ownerId = null;
            if (!string.IsNullOrWhiteSpace(crmShop.Assignee)
                && ownerByGroupName.TryGetValue(crmShop.Assignee.Trim(), out var byGroup))
            {
                ownerId = byGroup;
            }
            else
            {
                ownerId = crmShop.QuanLy
                    .Select(id => ownerByCrmUserId.TryGetValue(id, out var o) ? o : (Guid?)null)
                    .FirstOrDefault(o => o.HasValue);
            }

            if (ownerId is null)
            {
                result.Skipped.Add($"Shop {code}: không xác định được tổ (assignee '{crmShop.Assignee}').");
                continue;
            }

            try
            {
                await UpsertShopAsync(crmShop, code, ownerId.Value, platform.Id, result, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "CRM sync: shop {Code} failed", code);
                result.Skipped.Add($"Shop {code}: {ex.Message}");
            }
        }

        result.FinishedAt = DateTime.UtcNow;
        _logger.LogInformation(
            "CRM org sync ({Trigger}) done: users +{UC}/~{UU}/-{UD}, shops +{SC}/~{SU}, owner transfers {OT}, skipped {Sk}",
            trigger, result.UsersCreated, result.UsersUpdated, result.UsersDeactivated,
            result.ShopsCreated, result.ShopsUpdated, result.OwnersTransferred, result.Skipped.Count);
        return result;
    }

    private static bool IsSeller(KbtCrmMemberDto m) =>
        string.Equals(m.VaiTro, "seller", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates or updates the local account for a CRM member. Returns null when it cannot log in (no username).</summary>
    private async Task<ApplicationUser?> UpsertUserAsync(
        KbtCrmMemberDto member,
        Guid? teamOwnerId,
        KbtCrmSyncResultDto result,
        CancellationToken cancellationToken)
    {
        var username = member.Username?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(username))
        {
            result.Skipped.Add($"User '{member.Ten}': CRM không trả username.");
            return null;
        }

        var fullName = string.IsNullOrWhiteSpace(member.Ten) ? username : member.Ten.Trim();
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = username,
                NormalizedUserName = username.ToUpperInvariant(),
                Email = await ResolveEmailAsync(username, member.Email, cancellationToken),
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = member.ConLam,
                TeamOwnerId = teamOwnerId,
            };
            user.NormalizedEmail = user.Email!.ToUpperInvariant();

            var created = await _userManager.CreateAsync(user, KbtCrmSyncSettingKeys.DefaultPasswordFor(username));
            if (!created.Succeeded)
            {
                result.Skipped.Add($"User '{username}': {created.Errors.FirstOrDefault()?.Description}");
                return null;
            }

            await _userManager.AddToRoleAsync(user, RoleNames.Seller);
            result.UsersCreated++;
            return user;
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(RoleNames.Admin))
        {
            // Never let CRM demote or re-scope an admin.
            return user;
        }

        var changed = false;
        var scopeChanged = false;
        if (user.FullName != fullName)
        {
            user.FullName = fullName;
            changed = true;
        }

        if (user.IsActive != member.ConLam)
        {
            user.IsActive = member.ConLam;
            changed = true;
            scopeChanged = true;
            if (!member.ConLam)
            {
                result.UsersDeactivated++;
            }
        }

        if (user.TeamOwnerId != teamOwnerId)
        {
            user.TeamOwnerId = teamOwnerId;
            changed = true;
            scopeChanged = true;
        }

        if (!roles.Contains(RoleNames.Seller))
        {
            await _userManager.AddToRoleAsync(user, RoleNames.Seller);
            changed = true;
        }

        if (changed)
        {
            user.UpdatedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            if (scopeChanged)
            {
                // The `ss` claim check turns this into immediate token revocation, so a stale `owner` claim cannot linger.
                await _userManager.UpdateSecurityStampAsync(user);
            }

            result.UsersUpdated++;
        }

        return user;
    }

    /// <summary>Identity requires a unique email; CRM rarely has one, so fall back to a synthetic local address.</summary>
    private async Task<string> ResolveEmailAsync(string username, string? crmEmail, CancellationToken cancellationToken)
    {
        var email = crmEmail?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var taken = await _context.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
            if (!taken)
            {
                return email;
            }
        }

        return $"{username}@kbtcrm.local";
    }

    private async Task UpsertShopAsync(
        KbtCrmShopDto crmShop,
        string code,
        Guid ownerId,
        Guid platformId,
        KbtCrmSyncResultDto result,
        CancellationToken cancellationToken)
    {
        var profileName = crmShop.Ten?.Trim();
        var displayName = string.IsNullOrWhiteSpace(crmShop.ShopName) ? profileName ?? code : crmShop.ShopName.Trim();

        var shop = await _context.Shops.FirstOrDefaultAsync(s => s.ShopCode == code, cancellationToken);
        if (shop is null)
        {
            var created = await _shopService.CreateForOwnerAsync(new CreateAdminShopDto
            {
                OwnerId = ownerId,
                Name = displayName,
                PlatformId = platformId,
                Region = "US",
                Status = ShopStatuses.Active,
                ProfileName = profileName,
            }, cancellationToken);

            // ShopCode is not part of the create contract (admin UI never sets it); stamp it after creation.
            var entity = await _context.Shops.FirstAsync(s => s.Id == created.Id, cancellationToken);
            entity.ShopCode = code;
            await _context.SaveChangesAsync(cancellationToken);
            result.ShopsCreated++;
            return;
        }

        var changed = false;
        if (!string.IsNullOrWhiteSpace(crmShop.ShopName) && shop.Name != displayName)
        {
            shop.Name = displayName;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(profileName) && shop.ProfileName != profileName)
        {
            shop.ProfileName = profileName;
            changed = true;
        }

        if (changed)
        {
            shop.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            result.ShopsUpdated++;
        }

        if (shop.OwnerId != ownerId)
        {
            await _shopService.TransferOwnerAsync(shop.Id, ownerId, cancellationToken);
            result.OwnersTransferred++;
        }
    }
}
