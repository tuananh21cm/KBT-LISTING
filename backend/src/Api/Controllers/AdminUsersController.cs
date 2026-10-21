using Application.DTOs.Auth;
using Application.DTOs.FourSeller;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Constants;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin user management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IFourSellerApiService _fourSellerApiService;
    private readonly IValidator<CreateAdminUserDto> _createValidator;
    private readonly IValidator<UpdateAdminUserDto> _updateValidator;
    private readonly IValidator<ImportAdminUsersDto> _importValidator;
    private readonly IValidator<ResetPasswordDto> _resetPasswordValidator;

    /// <summary>
    /// Initializes the admin users controller.
    /// </summary>
    public AdminUsersController(
        IUserService userService,
        IFourSellerApiService fourSellerApiService,
        IValidator<CreateAdminUserDto> createValidator,
        IValidator<UpdateAdminUserDto> updateValidator,
        IValidator<ImportAdminUsersDto> importValidator,
        IValidator<ResetPasswordDto> resetPasswordValidator)
    {
        _userService = userService;
        _fourSellerApiService = fourSellerApiService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _importValidator = importValidator;
        _resetPasswordValidator = resetPasswordValidator;
    }

    /// <summary>
    /// Lists all users.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<AdminUserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AdminUserDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _userService.GetAllUsersAsync(cancellationToken);
        return Ok(ApiResponse<List<AdminUserDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a user by ID.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _userService.GetUserByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<AdminUserDto>.Ok(result));
    }

    /// <summary>
    /// Creates a new user.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> Create(
        [FromBody] CreateAdminUserDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _userService.CreateUserAsync(request, cancellationToken);
        return Ok(ApiResponse<AdminUserDto>.Ok(result, "Tạo người dùng thành công."));
    }

    /// <summary>
    /// Updates a user.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> Update(
        Guid id,
        [FromBody] UpdateAdminUserDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _userService.UpdateUserAsync(id, request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<AdminUserDto>.Ok(result, "Cập nhật người dùng thành công."));
    }

    /// <summary>
    /// Imports users in bulk: khớp theo tên đăng nhập hoặc email — đã có thì cập nhật
    /// (họ tên, email, vai trò, mật khẩu), chưa có thì tạo mới.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.manage")]
    [HttpPost("import")]
    [ProducesResponseType(typeof(ApiResponse<ImportAdminUsersResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ImportAdminUsersResultDto>>> Import(
        [FromBody] ImportAdminUsersDto request,
        CancellationToken cancellationToken)
    {
        await _importValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _userService.ImportUsersAsync(request, User.GetUserId(), cancellationToken);
        var message = $"Import xong: thêm mới {result.Created}, cập nhật {result.Updated}, lỗi {result.Failed.Count}.";
        return Ok(ApiResponse<ImportAdminUsersResultDto>.Ok(result, message));
    }

    /// <summary>
    /// Permanently deletes a user (hard delete). Fails when related business data still references the user.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _userService.DeleteUserAsync(id, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa người dùng thành công."));
    }

    /// <summary>
    /// Resets a user password.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.manage")]
    [HttpPut("{id:guid}/reset-password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> ResetPassword(
        Guid id,
        [FromBody] ResetPasswordDto request,
        CancellationToken cancellationToken)
    {
        await _resetPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        await _userService.ResetPasswordAsync(id, request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đặt lại mật khẩu thành công."));
    }

    /// <summary>
    /// Lists active seller accounts for assignment.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.manage")]
    [HttpGet("sellers")]
    [ProducesResponseType(typeof(ApiResponse<List<UserSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<UserSummaryDto>>>> GetSellers(
        CancellationToken cancellationToken)
    {
        var result = await _userService.GetSellersAsync(cancellationToken);
        return Ok(ApiResponse<List<UserSummaryDto>>.Ok(result));
    }

    /// <summary>
    /// Active users as id/name options for filter dropdowns (no roles / secrets).
    /// </summary>
    /// <param name="roles">Tên role cách nhau bằng dấu phẩy (OR), vd. <c>Admin,Fulfil</c>. Bỏ trống = mọi user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Authorize(Policy = "Permission:admin.users.view")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<UserSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<UserSummaryDto>>>> GetUserOptions(
        [FromQuery] string? roles,
        CancellationToken cancellationToken)
    {
        var roleNames = roles?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = await _userService.GetUserOptionsAsync(roleNames, cancellationToken);
        return Ok(ApiResponse<List<UserSummaryDto>>.Ok(result));
    }

    /// <summary>
    /// Lists roles for user assignment dropdowns.
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.manage")]
    [HttpGet("role-options")]
    [ProducesResponseType(typeof(ApiResponse<List<Application.DTOs.Role.RoleOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<Application.DTOs.Role.RoleOptionDto>>>> GetRoleOptions(
        CancellationToken cancellationToken)
    {
        var result = await _userService.GetRoleOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<Application.DTOs.Role.RoleOptionDto>>.Ok(result));
    }

    /// <summary>
    /// Checks 4Seller login for one user via <c>GET /api/user/is-login</c>.
    /// Optional body cookie checks a draft without saving; omit/empty uses the stored cookie.
    /// Invalid/expired stored cookie → status <c>expired</c> (cookie kept; outbound calls skip until re-check).
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.view")]
    [HttpPost("{id:guid}/four-seller-cookie/check-login")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerLoginCheckResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerLoginCheckResponse>>> CheckFourSellerLogin(
        Guid id,
        [FromBody] FourSellerLoginCheckRequest? request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.GetUserByIdAsync(id, cancellationToken);
        var checkingDraft = !string.IsNullOrWhiteSpace(request?.FourSellerCookie);
        var result = await _fourSellerApiService.CheckLoginAsync(
            id,
            request?.FourSellerCookie,
            cancellationToken);

        result.HasFourSellerCookie = user.HasFourSellerCookie;
        result.CookieCleared = false;

        if (checkingDraft)
        {
            result.FourSellerCookieLoginStatus = user.FourSellerCookieLoginStatus;
        }
        else if (result.Blocked || result.Unverified)
        {
            // EdgeOne 567 / transient — session unverifiable; keep the current status (do not expire).
            result.FourSellerCookieLoginStatus = user.FourSellerCookieLoginStatus;
            if (string.IsNullOrWhiteSpace(result.Message))
            {
                result.Message = "Không xác minh được phiên 4Seller — giữ nguyên trạng thái cookie.";
            }
            else if (!result.Message.Contains("không đổi", StringComparison.OrdinalIgnoreCase) &&
                     !result.Message.Contains("KHÔNG phải", StringComparison.OrdinalIgnoreCase))
            {
                result.Message = $"{result.Message.TrimEnd('.')} — không đổi trạng thái cookie.";
            }
        }
        else if (!user.HasFourSellerCookie)
        {
            result.FourSellerCookieLoginStatus = FourSellerCookieLoginStatuses.None;
        }
        else
        {
            var status = result.IsLoggedIn
                ? FourSellerCookieLoginStatuses.LoggedIn
                : FourSellerCookieLoginStatuses.Expired;
            await _userService.SetFourSellerCookieLoginStatusAsync(id, status, cancellationToken);
            result.FourSellerCookieLoginStatus = status;
            if (!result.IsLoggedIn)
            {
                result.Message = string.IsNullOrWhiteSpace(result.Message)
                    ? "Cookie 4Seller hết hạn — đã cập nhật trạng thái."
                    : $"{result.Message.TrimEnd('.')} — đã cập nhật trạng thái hết hạn.";
            }
        }

        return Ok(ApiResponse<FourSellerLoginCheckResponse>.Ok(result, result.Message));
    }

    /// <summary>
    /// Bulk-checks 4Seller login for users with a stored cookie (or a given <c>userIds</c> filter).
    /// Invalid/expired stored cookies → status <c>expired</c> (cookie kept).
    /// </summary>
    [Authorize(Policy = "Permission:admin.users.view")]
    [HttpPost("four-seller-cookie/check-login")]
    [ProducesResponseType(typeof(ApiResponse<List<AdminFourSellerLoginCheckItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AdminFourSellerLoginCheckItemDto>>>> CheckFourSellerLogins(
        [FromBody] AdminFourSellerLoginCheckRequest? request,
        CancellationToken cancellationToken)
    {
        var users = await _userService.GetAllUsersAsync(cancellationToken);

        if (request?.UserIds is { Count: > 0 } filterIds)
        {
            var idSet = filterIds.ToHashSet();
            users = users.Where(u => idSet.Contains(u.Id)).ToList();
        }
        else
        {
            users = users.Where(u => u.HasFourSellerCookie).ToList();
        }

        var results = new List<AdminFourSellerLoginCheckItemDto>(users.Count);
        var expiredCount = 0;
        var blockedCount = 0;
        var unverifiedCount = 0;
        foreach (var user in users)
        {
            if (!user.HasFourSellerCookie)
            {
                results.Add(new AdminFourSellerLoginCheckItemDto
                {
                    UserId = user.Id,
                    UserName = user.UserName,
                    FullName = user.FullName,
                    HasCookie = false,
                    IsLoggedIn = false,
                    FourSellerCookieLoginStatus = FourSellerCookieLoginStatuses.None,
                    CookieCleared = false,
                    Message = "Chưa có cookie 4Seller."
                });
                continue;
            }

            var check = await _fourSellerApiService.CheckLoginAsync(user.Id, null, cancellationToken);

            if (check.Blocked || check.Unverified)
            {
                // EdgeOne 567 / transient — keep the current status; count separately, do not expire.
                if (check.Blocked)
                {
                    blockedCount++;
                }
                else
                {
                    unverifiedCount++;
                }

                results.Add(new AdminFourSellerLoginCheckItemDto
                {
                    UserId = user.Id,
                    UserName = user.UserName,
                    FullName = user.FullName,
                    HasCookie = true,
                    IsLoggedIn = false,
                    FourSellerCookieLoginStatus = user.FourSellerCookieLoginStatus,
                    CookieCleared = false,
                    Blocked = check.Blocked,
                    Unverified = check.Unverified,
                    Message = check.Message
                });
                continue;
            }

            var status = check.IsLoggedIn
                ? FourSellerCookieLoginStatuses.LoggedIn
                : FourSellerCookieLoginStatuses.Expired;
            await _userService.SetFourSellerCookieLoginStatusAsync(user.Id, status, cancellationToken);

            var message = check.Message;
            if (!check.IsLoggedIn)
            {
                expiredCount++;
                message = string.IsNullOrWhiteSpace(message)
                    ? "Cookie 4Seller hết hạn — đã cập nhật trạng thái."
                    : $"{message.TrimEnd('.')} — đã cập nhật trạng thái hết hạn.";
            }

            results.Add(new AdminFourSellerLoginCheckItemDto
            {
                UserId = user.Id,
                UserName = user.UserName,
                FullName = user.FullName,
                HasCookie = true,
                IsLoggedIn = check.IsLoggedIn,
                FourSellerCookieLoginStatus = status,
                CookieCleared = false,
                Message = message
            });
        }

        var loggedIn = results.Count(r => r.IsLoggedIn);
        var blockedSuffix = blockedCount > 0 ? $", {blockedCount} bị chặn IP (567)" : string.Empty;
        var unverifiedSuffix = unverifiedCount > 0
            ? $", {unverifiedCount} chưa xác minh (mạng/proxy)"
            : string.Empty;
        var messageSummary = results.Count == 0
            ? "Không có người dùng nào có cookie 4Seller để kiểm tra."
            : expiredCount > 0
                ? $"Đã kiểm tra {results.Count} người dùng — {loggedIn} đang login, {expiredCount} hết hạn{blockedSuffix}{unverifiedSuffix}."
                : $"Đã kiểm tra {results.Count} người dùng — {loggedIn} đang login{blockedSuffix}{unverifiedSuffix}.";

        return Ok(ApiResponse<List<AdminFourSellerLoginCheckItemDto>>.Ok(results, messageSummary));
    }
}
