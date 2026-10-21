using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Quy tắc duy nhất quyết định các mốc fulfil suy từ SHEIN còn bằng chứng hay không.
///
/// <see cref="OrderSupplierCost"/> tính lại tiền sau khi gỡ một link, nhưng mốc thì không: mốc đã
/// nằm sẵn trên cột <c>Order</c> nên gỡ link xong đơn vẫn báo "Đã giao" bằng clock của billno vừa
/// bị gỡ. Không thể chờ lượt sync sau tự sửa vì <see cref="OrderFulfilTimeline.Stamp"/> chỉ ghi đè
/// khi mốc mới **sớm hơn** ở cùng rank, mà mốc đúng của billno còn lại thường muộn hơn.
///
/// Cố tình chỉ xét mốc nguồn <c>shein</c> và cố tình hẹp: mốc do người dùng nhập tay, do Mark Ship
/// hay do sàn stamp đều không phải việc của hàm này.
/// </summary>
public static class OrderSheinMilestoneEvidence
{
    /// <summary>
    /// Xoá mốc SHEIN mà các link còn lại của đơn không chứng minh được.
    ///
    /// Bằng chứng theo từng chặng đúng như <c>ApplySheinStatusMilestones</c> đã stamp: mốc Giao cần
    /// một billno <c>delivered</c>, mốc Transit cần billno <c>shipped</c> hoặc <c>delivered</c>, và
    /// mốc Mua cần khớp đúng clock đặt/trả tiền của một billno còn lại — điều kiện cuối là mấu chốt
    /// để mốc đến từ nguồn khác không bị xoá oan.
    /// </summary>
    /// <returns><c>true</c> khi có mốc bị xoá.</returns>
    public static bool ClearMilestonesWithoutLinkEvidence(Order order)
    {
        var links = order.SupplierPurchases ?? [];
        var cleared = false;

        var hasDelivered = links.Any(l =>
            string.Equals(l.SupplierStatus, SupplierStatusGroups.Delivered, StringComparison.OrdinalIgnoreCase));
        var hasShipped = hasDelivered || links.Any(l =>
            string.Equals(l.SupplierStatus, SupplierStatusGroups.Shipped, StringComparison.OrdinalIgnoreCase));

        if (!hasDelivered && ClearIfShein(order, OrderFulfilTimeline.Milestone.Delivered)) cleared = true;
        if (!hasShipped && ClearIfShein(order, OrderFulfilTimeline.Milestone.InTransit)) cleared = true;

        if (order.FulfilPurchasedAt is { } purchased
            && !links.Any(l => SameInstant(l.OrderedAt, purchased) || SameInstant(l.SupplierPaidAt, purchased))
            && ClearIfShein(order, OrderFulfilTimeline.Milestone.FulfilPurchased))
        {
            cleared = true;
        }

        if (cleared) OrderFulfilTimeline.Normalize(order);
        return cleared;
    }

    /// <summary>Xoá mốc chỉ khi nó do sync SHEIN stamp; nguồn khác giữ nguyên.</summary>
    private static bool ClearIfShein(Order order, OrderFulfilTimeline.Milestone milestone)
    {
        if (OrderFulfilTimeline.SourceOf(order, milestone) != OrderFulfilTimeline.Source.Shein)
        {
            return false;
        }

        OrderFulfilTimeline.Clear(order, milestone);
        return true;
    }

    /// <summary>
    /// So mốc bỏ qua <see cref="DateTime.Kind"/>: cột DB đọc ra là UTC còn giá trị vừa stamp trong
    /// bộ nhớ có thể là Unspecified, so trực tiếp sẽ trượt.
    /// </summary>
    private static bool SameInstant(DateTime? candidate, DateTime stamped) =>
        candidate is { } value
        && DateTime.SpecifyKind(value, DateTimeKind.Utc)
            == DateTime.SpecifyKind(stamped, DateTimeKind.Utc);
}
