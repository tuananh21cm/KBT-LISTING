namespace Application.DTOs.Auth;

/// <summary>
/// Admin reset user password request DTO.
/// </summary>
public class ResetPasswordDto
{
    /// <summary>New password.</summary>
    public string NewPassword { get; set; } = string.Empty;
}
