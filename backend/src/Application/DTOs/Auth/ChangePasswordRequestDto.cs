namespace Application.DTOs.Auth;

/// <summary>
/// Change password request payload.
/// </summary>
public class ChangePasswordRequestDto
{
    /// <summary>
    /// Current password.
    /// </summary>
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>
    /// New password.
    /// </summary>
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>
    /// New password confirmation.
    /// </summary>
    public string ConfirmPassword { get; set; } = string.Empty;
}
