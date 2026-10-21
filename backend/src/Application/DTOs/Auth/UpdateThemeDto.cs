namespace Application.DTOs.Auth;

/// <summary>
/// Request to update the current user's UI theme preference.
/// </summary>
public class UpdateThemeDto
{
    /// <summary>
    /// Appearance mode: <c>light</c> or <c>dark</c>.
    /// </summary>
    public string Theme { get; set; } = "light";
}
