using System.Text.Json.Serialization;

namespace Application.DTOs.KbtCrm;

/// <summary>Payload of KBT CRM <c>GET /agent/org</c>: teams → members → shop codes. Field names are the CRM's (Vietnamese).</summary>
public class KbtCrmOrgDto
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("cap_nhat_luc")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("groups")] public List<KbtCrmGroupDto> Groups { get; set; } = [];
    [JsonPropertyName("users_khong_to")] public List<KbtCrmMemberDto> UsersWithoutGroup { get; set; } = [];
    [JsonPropertyName("shops")] public List<KbtCrmShopDto> Shops { get; set; } = [];
}

public class KbtCrmGroupDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("members")] public List<KbtCrmMemberDto> Members { get; set; } = [];
}

public class KbtCrmMemberDto
{
    [JsonPropertyName("user_id")] public long UserId { get; set; }
    /// <summary>Display name (CRM <c>users.display_name</c>).</summary>
    [JsonPropertyName("ten")] public string Ten { get; set; } = string.Empty;
    /// <summary>Login name (CRM <c>users.username</c>). Required to create an account here.</summary>
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    /// <summary>admin | sub-admin | fulfill | seller | designer.</summary>
    [JsonPropertyName("vai_tro")] public string? VaiTro { get; set; }
    /// <summary>Still employed.</summary>
    [JsonPropertyName("con_lam")] public bool ConLam { get; set; } = true;
    [JsonPropertyName("truong_to")] public bool TruongTo { get; set; }
    /// <summary>The team's shared account (owns the team's shops).</summary>
    [JsonPropertyName("tai_khoan_to")] public bool TaiKhoanTo { get; set; }
    [JsonPropertyName("shops")] public List<string> Shops { get; set; } = [];
}

public class KbtCrmShopDto
{
    /// <summary>TikTok shop code, e.g. USLC8METK2 — the match key.</summary>
    [JsonPropertyName("shop_code")] public string ShopCode { get; set; } = string.Empty;
    /// <summary>Browser profile name (CRM <c>suggest_name</c>), e.g. KBT-Homie-227-The Maxage Lounge.</summary>
    [JsonPropertyName("ten")] public string Ten { get; set; } = string.Empty;
    /// <summary>Team name in charge (matches <see cref="KbtCrmGroupDto.Name"/>).</summary>
    [JsonPropertyName("assignee")] public string? Assignee { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    /// <summary>CRM user ids managing the shop.</summary>
    [JsonPropertyName("quan_ly")] public List<long> QuanLy { get; set; } = [];
    [JsonPropertyName("shop_name")] public string? ShopName { get; set; }
    [JsonPropertyName("is_active")] public bool? IsActive { get; set; }
    [JsonPropertyName("shop_type")] public string? ShopType { get; set; }
    [JsonPropertyName("shop_status")] public string? ShopStatus { get; set; }
    [JsonPropertyName("tenant_group_id")] public long? TenantGroupId { get; set; }
}

/// <summary>Outcome of one CRM → local sync pass.</summary>
public class KbtCrmSyncResultDto
{
    public int CrmGroups { get; set; }
    public int CrmUsers { get; set; }
    public int CrmShops { get; set; }
    public int UsersCreated { get; set; }
    public int UsersUpdated { get; set; }
    public int UsersDeactivated { get; set; }
    public int ShopsCreated { get; set; }
    public int ShopsUpdated { get; set; }
    public int OwnersTransferred { get; set; }
    /// <summary>Items left untouched and why (no username, no team, unknown platform…).</summary>
    public List<string> Skipped { get; set; } = [];
    public DateTime FinishedAt { get; set; }
}
