namespace Shared.Constants;

/// <summary>
/// Trạng thái khớp của một dòng <c>SupplierPurchases</c>.
///
/// Ghi nhận tách hẳn khỏi khớp: mọi lần mua vào bảng <b>trước</b> khi chạy khớp, nên một lần khớp
/// sai hay pool hụt cũng không làm mất dấu vết lần mua nào.
/// </summary>
public static class SupplierPurchaseMatchStates
{
    /// <summary>Đã gắn vào một đơn sàn — <c>OrderId</c> có giá trị.</summary>
    public const string Matched = "matched";

    /// <summary>Chưa gắn được, còn nằm trong hàng đợi xử lý tay.</summary>
    public const string Unmatched = "unmatched";

    /// <summary>Người xử lý đã xem và quyết định bỏ qua (Club/Saver, đơn cá nhân, mua thử…).</summary>
    public const string Ignored = "ignored";
}

/// <summary>Nguồn của một dòng <c>SupplierPurchases</c>.</summary>
public static class SupplierPurchaseSources
{
    /// <summary>Sync SHEIN ghi nhận.</summary>
    public const string Shein = "shein";

    /// <summary>Người đi đơn nhập tay qua nút "Cập nhật phí".</summary>
    public const string Manual = "manual";
}
