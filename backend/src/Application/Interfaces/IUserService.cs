using Application.DTOs.Auth;
using Application.DTOs.Role;

namespace Application.Interfaces;

/// <summary>
/// User profile service contract.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Gets the current user profile with roles and permissions.
    /// </summary>
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the user password.
    /// </summary>
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the current user's UI theme preference (<c>light</c> / <c>dark</c>).
    /// Returns the canonical theme value stored.
    /// </summary>
    Task<string> UpdateThemeAsync(
        Guid userId,
        UpdateThemeDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or clears the current user's 4Seller session cookie. Returns whether a cookie is stored.
    /// New cookie → status <c>unchecked</c>; clear → <c>none</c>.
    /// </summary>
    Task<bool> UpdateFourSellerCookieAsync(
        Guid userId,
        UpdateFourSellerCookieDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the stored 4Seller cookie for a user (no-op when already empty).
    /// Returns <c>true</c> when a non-empty cookie was removed. Status → <c>none</c>.
    /// </summary>
    Task<bool> ClearFourSellerCookieAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw stored 4Seller cookie for the owner (used by the browser extension).
    /// Throws <see cref="Domain.Exceptions.BusinessException"/> when no cookie is stored.
    /// </summary>
    Task<FourSellerCookieDto> GetFourSellerCookieAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists 4Seller cookie login status after an is-login check (or outbound probe failure).
    /// </summary>
    Task SetFourSellerCookieLoginStatusAsync(
        Guid userId,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists active seller accounts for admin assignment.
    /// </summary>
    Task<List<UserSummaryDto>> GetSellersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Active users as id/name options for admin filter dropdowns (no roles in payload).
    /// <paramref name="roles"/> (tên role, OR) giới hạn danh sách — vd. <c>Admin</c> + <c>Fulfil</c>
    /// cho dropdown người fulfil; bỏ trống = mọi user đang hoạt động.
    /// </summary>
    Task<List<UserSummaryDto>> GetUserOptionsAsync(
        IReadOnlyCollection<string>? roles = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all users for admin management.
    /// </summary>
    Task<List<AdminUserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by ID for admin management.
    /// </summary>
    Task<AdminUserDto> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user.
    /// </summary>
    Task<AdminUserDto> CreateUserAsync(CreateAdminUserDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user.
    /// </summary>
    Task<AdminUserDto> UpdateUserAsync(Guid userId, UpdateAdminUserDto dto, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Import hàng loạt: khớp theo tên đăng nhập hoặc email — đã có thì cập nhật
    /// (họ tên, email, vai trò và mật khẩu), chưa có thì tạo mới. Một dòng lỗi
    /// không chặn các dòng còn lại; lỗi được trả về trong kết quả.
    /// </summary>
    Task<ImportAdminUsersResultDto> ImportUsersAsync(
        ImportAdminUsersDto dto,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a user (hard delete). Blocked when related business data
    /// still references the user (Restrict FKs). Use <c>IsActive=false</c> on update to deactivate instead.
    /// </summary>
    Task DeleteUserAsync(Guid userId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets a user password by admin.
    /// </summary>
    Task ResetPasswordAsync(Guid userId, ResetPasswordDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists roles for user assignment dropdowns.
    /// </summary>
    Task<List<RoleOptionDto>> GetRoleOptionsAsync(CancellationToken cancellationToken = default);
}
