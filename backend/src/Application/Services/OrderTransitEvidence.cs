using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Phân biệt mốc Transit có clock rời kho thật với mốc chỉ là bản sao của mốc Track.
///
/// Mốc rời kho chỉ SHEIN biết (<c>delivery_time</c> trong payload gốc), nên luồng sync hiện tại chỉ
/// stamp Transit với nguồn <see cref="OrderFulfilTimeline.Source.Shein"/>. Một phiên bản trước từng
/// stamp Transit bằng <c>OrderShippedTime</c> của 4Seller — đúng giá trị đã dùng cho mốc Track — nên
/// hai mốc trùng khít và dải timeline không còn khoảng nào để hiện bộ đếm thời gian.
/// </summary>
public static class OrderTransitEvidence
{
    /// <summary>
    /// Mốc Transit bịa: nguồn <c>platform</c> **và** trùng khít mốc Track (hoặc chính
    /// <c>OrderShippedTime</c>). Đòi đủ hai điều kiện để một mốc rời kho thật tình cờ cùng giây với
    /// mốc Track không bị xoá oan.
    /// </summary>
    public static bool IsFabricatedFromPlatformClock(Order order)
    {
        if (order.InTransitAt is not { } transit) return false;
        if (OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.InTransit)
            != OrderFulfilTimeline.Source.Platform)
        {
            return false;
        }

        return order.TrackingAddedAt == transit || order.OrderShippedTime == transit;
    }

    /// <summary>
    /// Xoá mốc Transit bịa để chặng hiện "Transit?" trung thực thay vì một khoảng bằng 0. Trả
    /// <c>true</c> khi có xoá. Mốc thật sẽ được dựng lại từ payload SHEIN gốc.
    /// </summary>
    public static bool ClearFabricatedTransit(Order order)
    {
        if (!IsFabricatedFromPlatformClock(order)) return false;

        OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.InTransit);
        return true;
    }
}
