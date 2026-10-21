namespace Application.DTOs.Auth;

/// <summary>
/// Kết quả import tài khoản hàng loạt — một dòng lỗi không chặn các dòng còn lại.
/// </summary>
public class ImportAdminUsersResultDto
{
    /// <summary>Số tài khoản tạo mới.</summary>
    public int Created { get; set; }

    /// <summary>Số tài khoản được cập nhật (kể cả mật khẩu).</summary>
    public int Updated { get; set; }

    /// <summary>Các dòng bị bỏ qua kèm lý do.</summary>
    public List<ImportAdminUserErrorDto> Failed { get; set; } = [];
}

/// <summary>
/// Một dòng import thất bại.
/// </summary>
public class ImportAdminUserErrorDto
{
    /// <summary>Vị trí dòng trong mảng <c>items</c> gửi lên (bắt đầu từ 0).</summary>
    public int Index { get; set; }

    /// <summary>Tên đăng nhập của dòng lỗi (có thể rỗng khi file thiếu dữ liệu).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Lý do bỏ qua.</summary>
    public string Error { get; set; } = string.Empty;
}
