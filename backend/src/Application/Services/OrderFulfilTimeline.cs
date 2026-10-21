using System.Text.Json;
using Domain.Entities;

namespace Application.Services;

/// <summary>
/// Single writer for the fulfil timeline milestones on an <see cref="Order"/>
/// (<c>FulfilPurchasedAt</c>, <c>TrackingAddedAt</c>, <c>InTransitAt</c>,
/// <c>DeliveredAt</c>, <c>RefundedAt</c>, <c>CanceledAt</c>).
///
/// Rules:
/// <list type="bullet">
/// <item>Every milestone is stamped only from a real event clock — never the sync-run
/// <c>DateTime.UtcNow</c>. A <c>null</c> timestamp is a no-op (stage stays empty).</item>
/// <item>Source rank arbitrates writes: <c>Manual &gt; Shein &gt; Platform &gt; Legacy</c>.
/// A higher rank overwrites regardless of clock; equal rank keeps the earlier (first real)
/// event time; a lower rank is ignored.</item>
/// <item><see cref="Normalize"/> repairs ordering so the UI never renders an inverted
/// (<c>—</c>) or zero (<c>&lt;1m</c>) gap: it clears the soft middle milestones
/// (Track / Transit) when they violate their neighbours instead of forcing equal timestamps.</item>
/// </list>
/// Provenance per milestone is persisted in <see cref="Order.FulfilTimelineSources"/> (jsonb)
/// so a later re-sync can overwrite lower-ranked / legacy values.
/// </summary>
public static class OrderFulfilTimeline
{
    /// <summary>Fulfil timeline milestone columns.</summary>
    public enum Milestone
    {
        /// <summary><see cref="Order.FulfilPurchasedAt"/>.</summary>
        FulfilPurchased,

        /// <summary><see cref="Order.TrackingAddedAt"/>.</summary>
        TrackingAdded,

        /// <summary><see cref="Order.InTransitAt"/>.</summary>
        InTransit,

        /// <summary><see cref="Order.DeliveredAt"/>.</summary>
        Delivered,

        /// <summary><see cref="Order.RefundedAt"/>.</summary>
        Refunded,

        /// <summary><see cref="Order.CanceledAt"/>.</summary>
        Canceled
    }

    /// <summary>Provenance rank of a milestone timestamp.</summary>
    public enum Source
    {
        /// <summary>Pre-split backfill / unknown origin. Always overwritable.</summary>
        Legacy = 0,

        /// <summary>4Seller platform ship / cancel clock (only real times, never sync-time).</summary>
        Platform = 1,

        /// <summary>SHEIN fulfil sync (owns the fulfil lifecycle).</summary>
        Shein = 2,

        /// <summary>Human action (Mark Ship, manual milestone edit).</summary>
        Manual = 3
    }

    private static readonly JsonSerializerOptions SourcesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Stamps <paramref name="milestone"/> with a real event clock, honoring source rank.
    /// No-op when <paramref name="at"/> is null.
    /// </summary>
    public static void Stamp(Order order, Milestone milestone, DateTime? at, Source source)
    {
        if (at is null) return;

        var incoming = ToUtc(at.Value);
        var current = Get(order, milestone);
        var sources = ReadSources(order);
        var key = KeyOf(milestone);
        var currentRank = current is null
            ? int.MinValue
            : (int)(sources.TryGetValue(key, out var s) ? ParseSource(s) : Source.Legacy);

        var write = current is null
            || (int)source > currentRank
            || ((int)source == currentRank && incoming < current.Value);
        if (!write) return;

        Set(order, milestone, incoming);
        sources[key] = source.ToString().ToLowerInvariant();
        WriteSources(order, sources);
    }

    /// <summary>
    /// Provenance đang lưu của một mốc; <c>null</c> khi mốc trống. Dùng để không ghi đè giá trị
    /// người dùng nhập tay ở những chỗ dọn dẹp không đi qua <see cref="Stamp"/>.
    /// </summary>
    public static Source? SourceOf(Order order, Milestone milestone)
    {
        if (Get(order, milestone) is null) return null;
        var sources = ReadSources(order);
        return sources.TryGetValue(KeyOf(milestone), out var raw) ? ParseSource(raw) : Source.Legacy;
    }

    /// <summary>Clears a milestone and its provenance so a later real clock can re-stamp it.</summary>
    public static void Clear(Order order, Milestone milestone)
    {
        Set(order, milestone, null);
        var sources = ReadSources(order);
        if (sources.Remove(KeyOf(milestone)))
        {
            WriteSources(order, sources);
        }
    }

    /// <summary>
    /// Repairs ordering of the ship chain. Mỗi mốc mềm (Track, Transit) phải nằm trong khoảng
    /// <c>max(OrderCreateTime, FulfilPurchasedAt) ≤ mốc &lt; DeliveredAt</c>; vi phạm thì xoá chứ
    /// không ép bằng nhau.
    ///
    /// Cố tình **không** ràng buộc <c>Track ≤ Transit</c>. Hai mốc này đo hai sự kiện độc lập ở hai
    /// hệ thống khác nhau: Track là lúc mã vận đơn lên sàn (4Seller), Transit là lúc hàng rời kho
    /// SHEIN. Đo trên 154 đơn có đủ hai mốc, 47 đơn (31%) có hàng rời kho **trước** khi mã lên sàn —
    /// hoàn toàn bình thường. Ràng buộc cũ khiến 48 mốc Transit hợp lệ bị xoá và UI hiện "Transit?".
    ///
    /// Cận trên là <c>&gt; DeliveredAt</c>, không phải <c>≥</c>: đơn một gói thường lấy cả mốc rời
    /// kho và mốc ký nhận từ cùng một dòng <c>goods_operation_list</c> nên hai clock trùng nhau, và
    /// xoá mốc trùng chỉ tạo ra một vòng lặp — lượt sync sau stamp lại (mốc đang null), Normalize
    /// xoá tiếp, mãi không hội tụ.
    /// </summary>
    public static IReadOnlyList<Cleared> Normalize(Order order)
    {
        var lower = Max(order.OrderCreateTime, order.FulfilPurchasedAt);
        var delivered = order.DeliveredAt;
        var cleared = new List<Cleared>(2);

        // Tracking cannot precede the order/purchase, nor land at/after delivery.
        if (order.TrackingAddedAt is { } track)
        {
            if (lower is { } lo && track < lo)
            {
                cleared.Add(new Cleared(Milestone.TrackingAdded, track, $"sớm hơn mốc dưới {lo:O}"));
            }
            else if (delivered is { } d && track > d)
            {
                cleared.Add(new Cleared(Milestone.TrackingAdded, track, $"muộn hơn mốc Giao {d:O}"));
            }
        }

        if (order.InTransitAt is { } transit)
        {
            if (lower is { } lo2 && transit < lo2)
            {
                cleared.Add(new Cleared(Milestone.InTransit, transit, $"sớm hơn mốc dưới {lo2:O}"));
            }
            else if (delivered is { } d2 && transit > d2)
            {
                cleared.Add(new Cleared(Milestone.InTransit, transit, $"muộn hơn mốc Giao {d2:O}"));
            }
        }

        foreach (var item in cleared)
        {
            Clear(order, item.Milestone);
        }

        return cleared;
    }

    /// <summary>
    /// Một mốc bị <see cref="Normalize"/> xoá, kèm lý do. Trả về cho chỗ gọi vì xoá một clock sự
    /// kiện thật là mất dữ liệu không hồi lại được (chỉ dựng lại được từ payload gốc), nên không
    /// được phép diễn ra âm thầm.
    /// </summary>
    public sealed record Cleared(Milestone Milestone, DateTime Value, string Reason);

    private static DateTime? Max(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Value >= b.Value ? a : b;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? Get(Order o, Milestone m) => m switch
    {
        Milestone.FulfilPurchased => o.FulfilPurchasedAt,
        Milestone.TrackingAdded => o.TrackingAddedAt,
        Milestone.InTransit => o.InTransitAt,
        Milestone.Delivered => o.DeliveredAt,
        Milestone.Refunded => o.RefundedAt,
        Milestone.Canceled => o.CanceledAt,
        _ => null
    };

    private static void Set(Order o, Milestone m, DateTime? v)
    {
        switch (m)
        {
            case Milestone.FulfilPurchased: o.FulfilPurchasedAt = v; break;
            case Milestone.TrackingAdded: o.TrackingAddedAt = v; break;
            case Milestone.InTransit: o.InTransitAt = v; break;
            case Milestone.Delivered: o.DeliveredAt = v; break;
            case Milestone.Refunded: o.RefundedAt = v; break;
            case Milestone.Canceled: o.CanceledAt = v; break;
        }
    }

    private static string KeyOf(Milestone m) => m switch
    {
        Milestone.FulfilPurchased => "fulfilPurchasedAt",
        Milestone.TrackingAdded => "trackingAddedAt",
        Milestone.InTransit => "inTransitAt",
        Milestone.Delivered => "deliveredAt",
        Milestone.Refunded => "refundedAt",
        Milestone.Canceled => "canceledAt",
        _ => m.ToString()
    };

    private static Source ParseSource(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "manual" => Source.Manual,
        "shein" => Source.Shein,
        "platform" => Source.Platform,
        _ => Source.Legacy
    };

    private static Dictionary<string, string> ReadSources(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.FulfilTimelineSources))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(
                       order.FulfilTimelineSources, SourcesJson)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WriteSources(Order order, Dictionary<string, string> sources)
    {
        order.FulfilTimelineSources = sources.Count == 0
            ? null
            : JsonSerializer.Serialize(sources, SourcesJson);
    }
}
