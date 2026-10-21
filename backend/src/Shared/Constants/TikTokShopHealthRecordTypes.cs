namespace Shared.Constants;

/// <summary>Record type values for <see cref="Domain.Entities.TikTokShopHealthRecord"/>.</summary>
public static class TikTokShopHealthRecordTypes
{
    /// <summary>POST growth_center/shop/violation/records/list.</summary>
    public const string Violation = "violation";

    /// <summary>POST growth_center/shop/warning/records/list.</summary>
    public const string Warning = "warning";
}

/// <summary>TikTok <c>state.record_status</c> values we treat as active for UI counts.</summary>
public static class TikTokShopHealthRecordStatuses
{
    /// <summary>Record đang hiển thị trên Health Center.</summary>
    public const short Active = 1;
}
