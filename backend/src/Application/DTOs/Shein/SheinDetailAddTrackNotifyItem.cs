using Domain.Entities;

namespace Application.DTOs.Shein;

/// <summary>
/// One order the backend detail sweep just gave real carrier tracking to, queued for the
/// Telegram "Add track" message. Built during matching but sent only after Auto Mark Ship ran,
/// so the message can report whether the code actually reached the platform.
/// </summary>
/// <param name="Order">Matched platform order (items loaded when available).</param>
/// <param name="Link">Supplier purchase carrying the carrier tracking and Mark Ship outcome.</param>
/// <param name="Billno">SHEIN billno that produced the tracking.</param>
/// <param name="DetectedAtUtc">When this sweep first saw the carrier code.</param>
public sealed record SheinDetailAddTrackNotifyItem(
    Domain.Entities.Order Order,
    SupplierPurchase Link,
    string Billno,
    DateTime DetectedAtUtc);
