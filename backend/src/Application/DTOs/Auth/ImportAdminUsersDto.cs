namespace Application.DTOs.Auth;

/// <summary>
/// Admin bulk user import request (upsert theo tên đăng nhập hoặc email).
/// </summary>
public class ImportAdminUsersDto
{
    /// <summary>Danh sách tài khoản cần thêm mới / cập nhật.</summary>
    public List<ImportAdminUserItemDto> Items { get; set; } = [];
}

/// <summary>
/// Một dòng tài khoản trong file import.
/// </summary>
public class ImportAdminUserItemDto
{
    /// <summary>Tên đăng nhập — khóa khớp chính.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Email — khóa khớp phụ.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Họ tên hiển thị.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Mật khẩu — luôn được đặt lại khi tài khoản đã tồn tại.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Role names to assign (thay thế toàn bộ role hiện có khi cập nhật).</summary>
    public List<string> Roles { get; set; } = [];
}
