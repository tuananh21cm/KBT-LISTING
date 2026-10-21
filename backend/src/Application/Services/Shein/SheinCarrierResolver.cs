using System.Text.RegularExpressions;

namespace Application.Services.Shein;

/// <summary>
/// Suy carrier SHEIN khi <c>track_info.name</c> trống: ưu tiên
/// <c>shipping_method_real</c>, rồi prefix <c>shipping_no</c>. Kết quả là tên logic
/// (GOFO / SpeedX / USPS…) để exact-match 4Seller qua <see cref="OrderAutoMarkShipPolicy.NormalizeCarrier"/>.
/// </summary>
public static partial class SheinCarrierResolver
{
    /// <summary>Aliases nhúng trong <c>shipping_method_real</c> (dài hơn trước để tránh khớp ngắn). </summary>
    private static readonly string[] MethodRealAliases =
    [
        "YunExpress",
        "SpeedX",
        "Speedx",
        "FedEx",
        "FEDEX",
        "GOFO",
        "USPS",
        "OnTrac",
        "UPS",
        "DHL"
    ];

    /// <summary>
    /// Cascade: track_info.name → shipping_method_real → tracking prefix.
    /// Trả null khi không suy được an toàn (warehouse label / mã kho / mơ hồ).
    /// </summary>
    public static string? Infer(
        string? trackInfoName,
        string? shippingMethodReal,
        string? trackingNo)
    {
        var fromName = CleanTrackInfoName(trackInfoName);
        if (fromName is not null) return fromName;

        var fromMethod = InferFromShippingMethodReal(shippingMethodReal);
        if (fromMethod is not null) return fromMethod;

        return InferFromTrackingPrefix(trackingNo);
    }

    /// <summary>
    /// Chọn đúng một chuỗi carrier trong catalog 4Seller khớp <paramref name="logicalCarrier"/>
    /// (sau normalize). Ưu tiên khớp chữ thường nguyên văn, rồi tên dài nhất.
    /// </summary>
    public static string? MatchCatalog(string? logicalCarrier, IEnumerable<string?> catalogCarriers)
    {
        var needle = OrderAutoMarkShipPolicy.NormalizeCarrier(logicalCarrier);
        if (needle.Length == 0) return null;

        string? best = null;
        var bestNormLen = -1;
        var bestExactIgnoreCase = false;

        foreach (var raw in catalogCarriers)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var candidate = raw.Trim();
            if (!OrderAutoMarkShipPolicy.IsExactCarrierMatch(logicalCarrier, candidate)) continue;

            var normLen = OrderAutoMarkShipPolicy.NormalizeCarrier(candidate).Length;
            var exact = string.Equals(candidate, logicalCarrier, StringComparison.OrdinalIgnoreCase);
            if (best is null
                || exact && !bestExactIgnoreCase
                || exact == bestExactIgnoreCase && normLen > bestNormLen)
            {
                best = candidate;
                bestNormLen = normLen;
                bestExactIgnoreCase = exact;
            }
        }

        return best;
    }

    private static string? CleanTrackInfoName(string? trackInfoName)
    {
        if (string.IsNullOrWhiteSpace(trackInfoName)) return null;
        var name = trackInfoName.Trim();
        if (name.Contains("warehouse", StringComparison.OrdinalIgnoreCase)) return null;
        return name;
    }

    /// <summary>
    /// Ví dụ <c>ASnGOFOxCNO-DID-PB-BN-Na</c> → GOFO (alias dài nhất xuất hiện trong chuỗi).
    /// </summary>
    public static string? InferFromShippingMethodReal(string? shippingMethodReal)
    {
        if (string.IsNullOrWhiteSpace(shippingMethodReal)) return null;
        var hay = shippingMethodReal.Trim();
        var hayNorm = OrderAutoMarkShipPolicy.NormalizeCarrier(hay);
        if (hayNorm.Length == 0) return null;

        string? best = null;
        var bestLen = 0;
        foreach (var alias in MethodRealAliases)
        {
            var aliasNorm = OrderAutoMarkShipPolicy.NormalizeCarrier(alias);
            if (aliasNorm.Length == 0 || aliasNorm.Length < bestLen) continue;
            if (hayNorm.Contains(aliasNorm, StringComparison.Ordinal)
                || hay.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                // Canonical display for Mark Ship / UI.
                best = CanonicalAlias(alias);
                bestLen = aliasNorm.Length;
            }
        }

        return best;
    }

    /// <summary>
    /// Prefix quan sát từ Mark Ship thành công: GFUS→GOFO, SPX→SpeedX, 92/93/94→USPS, YT→YunExpress.
    /// </summary>
    public static string? InferFromTrackingPrefix(string? trackingNo)
    {
        if (string.IsNullOrWhiteSpace(trackingNo)) return null;
        var t = trackingNo.Trim();
        if (t.Length < 4) return null;

        // Warehouse C…/N… — không suy last-mile.
        if (WarehouseTrackingRegex().IsMatch(t)) return null;

        if (t.StartsWith("GFUS", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("GFU", StringComparison.OrdinalIgnoreCase))
        {
            return "GOFO";
        }

        if (t.StartsWith("SPX", StringComparison.OrdinalIgnoreCase))
        {
            return "SpeedX";
        }

        if (t.StartsWith("YT", StringComparison.OrdinalIgnoreCase))
        {
            return "YunExpress";
        }

        // USPS IMpb. Danh sách này từng dựng từ mẫu Mark Ship thành công và thiếu mất `94` —
        // prefix USPS Tracking / Ground Advantage, loại phổ biến nhất với hàng e-commerce. Thiếu
        // nó thì link SHEIN có mã `94…` mà không kèm tên carrier bị Auto Mark Ship bỏ qua im lặng
        // (`OrderAutoMarkShipPolicy.ShouldAttempt` trả false), đơn nằm mãi không lên mã sàn.
        // `91/95/96` cũng nằm trong dải IMpb nhưng chưa có bằng chứng trong dữ liệu nên không
        // thêm: đoán sai ở đây là publish nhầm carrier lên sàn.
        if (UspsImPbPrefixRegex().IsMatch(t))
        {
            return "USPS";
        }

        return null;
    }

    private static string CanonicalAlias(string alias)
    {
        if (alias.Equals("SpeedX", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("Speedx", StringComparison.OrdinalIgnoreCase))
        {
            return "SpeedX";
        }

        if (alias.Equals("YunExpress", StringComparison.OrdinalIgnoreCase)) return "YunExpress";
        if (alias.Equals("FedEx", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("FEDEX", StringComparison.OrdinalIgnoreCase))
        {
            return "FedEx";
        }

        if (alias.Equals("OnTrac", StringComparison.OrdinalIgnoreCase)) return "OnTrac";
        if (alias.Equals("GOFO", StringComparison.OrdinalIgnoreCase)) return "GOFO";
        if (alias.Equals("USPS", StringComparison.OrdinalIgnoreCase)) return "USPS";
        if (alias.Equals("UPS", StringComparison.OrdinalIgnoreCase)) return "UPS";
        if (alias.Equals("DHL", StringComparison.OrdinalIgnoreCase)) return "DHL";
        return alias;
    }

    [GeneratedRegex(@"^[CN]\d{10,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WarehouseTrackingRegex();

    [GeneratedRegex(@"^(92|93|94)\d{18,}$", RegexOptions.CultureInvariant)]
    private static partial Regex UspsImPbPrefixRegex();
}
