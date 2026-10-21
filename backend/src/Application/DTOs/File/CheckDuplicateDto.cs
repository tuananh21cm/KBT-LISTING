namespace Application.DTOs.File;

/// <summary>
/// Check file duplicate request DTO.
/// </summary>
public class CheckDuplicateDto
{
    /// <summary>SHA256 hash (hex).</summary>
    public string FileHash { get; set; } = string.Empty;
}
