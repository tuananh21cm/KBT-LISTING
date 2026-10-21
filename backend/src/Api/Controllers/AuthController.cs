using Application.DTOs.Auth;
using Application.DTOs.FourSeller;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Constants;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Authentication endpoints.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;
    private readonly IFourSellerApiService _fourSellerApiService;
    private readonly IValidator<LoginRequestDto> _loginValidator;
    private readonly IValidator<PinLoginRequestDto> _pinLoginValidator;
    private readonly IValidator<RefreshTokenRequestDto> _refreshValidator;
    private readonly IValidator<ChangePasswordRequestDto> _changePasswordValidator;
    private readonly IValidator<UpdateFourSellerCookieDto> _fourSellerCookieValidator;
    private readonly IValidator<UpdateThemeDto> _themeValidator;

    /// <summary>
    /// Initializes the auth controller.
    /// </summary>
    public AuthController(
        IAuthService authService,
        IUserService userService,
        IFourSellerApiService fourSellerApiService,
        IValidator<LoginRequestDto> loginValidator,
        IValidator<PinLoginRequestDto> pinLoginValidator,
        IValidator<RefreshTokenRequestDto> refreshValidator,
        IValidator<ChangePasswordRequestDto> changePasswordValidator,
        IValidator<UpdateFourSellerCookieDto> fourSellerCookieValidator,
        IValidator<UpdateThemeDto> themeValidator)
    {
        _authService = authService;
        _userService = userService;
        _fourSellerApiService = fourSellerApiService;
        _loginValidator = loginValidator;
        _pinLoginValidator = pinLoginValidator;
        _refreshValidator = refreshValidator;
        _changePasswordValidator = changePasswordValidator;
        _fourSellerCookieValidator = fourSellerCookieValidator;
        _themeValidator = themeValidator;
    }

    /// <summary>
    /// Authenticates a user and returns tokens.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        await _loginValidator.ValidateAndThrowAsync(request, cancellationToken);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.LoginAsync(request, ipAddress, cancellationToken);
        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Đăng nhập thành công."));
    }

    /// <summary>
    /// Authenticates via 4-digit Fulfil extension PIN (Fulfil/Admin only).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login-pin")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> LoginWithPin(
        [FromBody] PinLoginRequestDto request,
        CancellationToken cancellationToken)
    {
        await _pinLoginValidator.ValidateAndThrowAsync(request, cancellationToken);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.LoginWithPinAsync(request, ipAddress, cancellationToken);
        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Đăng nhập thành công."));
    }

    /// <summary>
    /// Revokes the current refresh token.
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Logout(
        [FromBody] RefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        await _refreshValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = GetCurrentUserId();
        await _authService.LogoutAsync(userId, request.RefreshToken, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đăng xuất thành công."));
    }

    /// <summary>
    /// Refreshes the access token.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Refresh(
        [FromBody] RefreshTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        await _refreshValidator.ValidateAndThrowAsync(request, cancellationToken);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RefreshTokenAsync(request, ipAddress, cancellationToken);
        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Làm mới token thành công."));
    }

    /// <summary>
    /// Gets the current authenticated user.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CurrentUserDto>>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _userService.GetCurrentUserAsync(userId, cancellationToken);
        return Ok(ApiResponse<CurrentUserDto>.Ok(result));
    }

    /// <summary>
    /// Changes the current user's password.
    /// </summary>
    [Authorize]
    [HttpPut("change-password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(
        [FromBody] ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        await _changePasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = GetCurrentUserId();
        await _userService.ChangePasswordAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Đổi mật khẩu thành công."));
    }

    /// <summary>
    /// Updates the current user's UI theme preference (<c>light</c> / <c>dark</c>).
    /// </summary>
    [Authorize]
    [HttpPut("theme")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> UpdateTheme(
        [FromBody] UpdateThemeDto request,
        CancellationToken cancellationToken)
    {
        await _themeValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = GetCurrentUserId();
        var theme = await _userService.UpdateThemeAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { theme }, "Đã cập nhật giao diện."));
    }

    /// <summary>
    /// Sets or clears the current user's 4Seller session cookie (write-only; never returned).
    /// Empty string clears the cookie.
    /// </summary>
    [Authorize]
    [HttpPut("four-seller-cookie")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> UpdateFourSellerCookie(
        [FromBody] UpdateFourSellerCookieDto request,
        CancellationToken cancellationToken)
    {
        await _fourSellerCookieValidator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = GetCurrentUserId();
        var hasCookie = await _userService.UpdateFourSellerCookieAsync(userId, request, cancellationToken);
        var loginStatus = hasCookie
            ? FourSellerCookieLoginStatuses.Unchecked
            : FourSellerCookieLoginStatuses.None;
        var message = hasCookie
            ? "Đã lưu cookie 4Seller."
            : "Đã xóa cookie 4Seller.";
        return Ok(ApiResponse<object>.Ok(
            new { hasFourSellerCookie = hasCookie, fourSellerCookieLoginStatus = loginStatus },
            message));
    }

    /// <summary>
    /// Validates 4Seller login via is-login. Optional body cookie checks a draft without saving;
    /// omit/empty uses the stored cookie. Invalid/expired stored cookie → status <c>expired</c> (cookie kept).
    /// </summary>
    [Authorize]
    [HttpPost("four-seller-cookie/check-login")]
    [ProducesResponseType(typeof(ApiResponse<FourSellerLoginCheckResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FourSellerLoginCheckResponse>>> CheckFourSellerLogin(
        [FromBody] FourSellerLoginCheckRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var checkingDraft = !string.IsNullOrWhiteSpace(request?.FourSellerCookie);
        var result = await _fourSellerApiService.CheckLoginAsync(
            userId,
            request?.FourSellerCookie,
            cancellationToken);

        await ApplyStoredCookieLoginStatusAsync(userId, checkingDraft, result, cancellationToken);

        return Ok(ApiResponse<FourSellerLoginCheckResponse>.Ok(result, result.Message));
    }

    private async Task ApplyStoredCookieLoginStatusAsync(
        Guid userId,
        bool checkingDraft,
        FourSellerLoginCheckResponse result,
        CancellationToken cancellationToken)
    {
        var current = await _userService.GetCurrentUserAsync(userId, cancellationToken);
        result.HasFourSellerCookie = current.HasFourSellerCookie;
        result.CookieCleared = false;

        if (checkingDraft)
        {
            result.FourSellerCookieLoginStatus = current.FourSellerCookieLoginStatus;
            return;
        }

        if (!current.HasFourSellerCookie)
        {
            result.FourSellerCookieLoginStatus = FourSellerCookieLoginStatuses.None;
            return;
        }

        // EdgeOne 567 / transient network — session unverifiable; keep current status.
        if (result.Blocked || result.Unverified)
        {
            result.FourSellerCookieLoginStatus = current.FourSellerCookieLoginStatus;
            if (string.IsNullOrWhiteSpace(result.Message))
            {
                result.Message = "Không xác minh được phiên 4Seller — giữ nguyên trạng thái cookie.";
            }
            else if (!result.Message.Contains("không đổi", StringComparison.OrdinalIgnoreCase) &&
                     !result.Message.Contains("KHÔNG phải", StringComparison.OrdinalIgnoreCase))
            {
                result.Message = $"{result.Message.TrimEnd('.')} — không đổi trạng thái cookie.";
            }

            return;
        }

        var status = result.IsLoggedIn
            ? FourSellerCookieLoginStatuses.LoggedIn
            : FourSellerCookieLoginStatuses.Expired;
        await _userService.SetFourSellerCookieLoginStatusAsync(userId, status, cancellationToken);
        result.FourSellerCookieLoginStatus = status;
        if (!result.IsLoggedIn)
        {
            result.Message = string.IsNullOrWhiteSpace(result.Message)
                ? "Cookie 4Seller hết hạn — đã cập nhật trạng thái."
                : $"{result.Message.TrimEnd('.')} — đã cập nhật trạng thái hết hạn.";
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimConstants.UserId)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Không xác định được người dùng.");
        }

        return userId;
    }
}
