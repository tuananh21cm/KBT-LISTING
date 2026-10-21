namespace Application.DTOs.Auth;

/// <summary>
/// PIN-only login request for the KINHBAC FULFIL extension.
/// </summary>
public class PinLoginRequestDto
{
    /// <summary>Exactly 4 decimal digits.</summary>
    public string Pin { get; set; } = string.Empty;

    /// <summary>
    /// When true, issues a long-lived refresh token (extension stay-signed-in).
    /// </summary>
    public bool Persistent { get; set; }
}
