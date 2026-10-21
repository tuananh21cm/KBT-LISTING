namespace Shared.Constants;

/// <summary>
/// Setting keys for the 4Seller after-sales (return / refund) sync — managed via admin Settings.
///
/// Two independent loops, because they are two different systems doing the work:
/// <c>trigger</c> asks 4Seller to pull fresh return cases from TikTok Shop into its own database,
/// <c>pull</c> reads those cases back into ours. Running them on separate clocks lets the pull stay
/// cheap while the trigger, which costs a TikTok round-trip on 4Seller's side, runs rarely.
///
/// This flow owns <c>Order.PlatformOrderStatus = REFUND</c>, <c>Order.RefundedAt</c> and
/// <c>Order.CancelReason</c>. Order sync only latches what this flow wrote.
/// </summary>
public static class AfterSalesSyncSettingKeys
{
    /// <summary>When true, the API server periodically asks 4Seller to pull returns from TikTok Shop.</summary>
    public const string TriggerEnabled = "fourseller.after_sales.trigger.enabled";

    /// <summary>Minutes between trigger calls. Clamped to <see cref="MinIntervalMinutes"/>–<see cref="MaxIntervalMinutes"/>.</summary>
    public const string TriggerIntervalMinutes = "fourseller.after_sales.trigger.interval_minutes";

    /// <summary>When true, the API server periodically pulls return cases from 4Seller into local orders.</summary>
    public const string PullEnabled = "fourseller.after_sales.pull.enabled";

    /// <summary>Minutes between pull runs. Clamped to <see cref="MinIntervalMinutes"/>–<see cref="MaxIntervalMinutes"/>.</summary>
    public const string PullIntervalMinutes = "fourseller.after_sales.pull.interval_minutes";

    /// <summary>When true, orders whose after-sales state changed this run are announced on Telegram.</summary>
    public const string NotifyEnabled = "fourseller.after_sales.notify_enabled";

    /// <summary>Telegram chat that receives after-sales handling messages.</summary>
    public const string TelegramChatId = "fourseller.after_sales.telegram_chat_id";

    /// <summary>
    /// Internal cursor, not meant for hand editing: JSON map <c>ownerId → last platformUpdateTime
    /// (UTC, round-trip format)</c> already processed. Per-owner so one seller failing (expired
    /// cookie, proxy cooldown) cannot skip its returns by riding a shared cursor other sellers moved.
    /// </summary>
    public const string Watermark = "fourseller.after_sales.watermark";

    /// <summary>
    /// One-shot marker (<c>done</c>) for the pass that re-stamps <c>Order.RefundedAt</c> from the
    /// return case's <c>platformCreateTime</c>. Earlier runs stamped <c>platformUpdateTime</c>,
    /// which moves every time the platform touches the case, so the refund clock drifted forward
    /// and the refund list sorted nothing like TikTok Shop / 4Seller.
    ///
    /// Not seeded as a user-facing switch: clear the value to run the pass again.
    /// </summary>
    public const string RefundedAtCreateTimeRepair = "maintenance.refunded_at_platform_create_time_v1";

    /// <summary>
    /// One-shot marker (<c>done</c>) for the pass that released orders from <c>REFUND</c> when their
    /// return case was still open. Superseded by <see cref="RefundCasePresenceReconcile"/>, which
    /// reverses that rule — kept only so the settled row is recognisable in Settings.
    /// </summary>
    public const string OrphanRefundReconcile = "maintenance.refunded_at_orphan_reconcile_v1";

    /// <summary>
    /// One-shot marker (<c>done</c>) for the full reconcile that makes local <c>REFUND</c> mirror
    /// 4Seller's returns list: an order with a case (complete or still running) belongs in the
    /// refund tab with its clock set to the case's create time, and an order no case mentions at all
    /// does not belong there.
    ///
    /// Not seeded as a user-facing switch: clear the value to run the pass again.
    /// </summary>
    public const string RefundCasePresenceReconcile = "maintenance.refund_case_presence_reconcile_v1";

    /// <summary>
    /// One-shot marker (<c>done</c>) for the pass that backfills <c>Order.ReturnStatus</c> from
    /// 4Seller <c>returnStatus</c> onto every refunded order. Pull incremental only re-reads cases
    /// that changed since the watermark, so without this pass OrderCards would keep a blank status
    /// line until each case next moves on 4Seller.
    ///
    /// Separate from <see cref="RefundCasePresenceReconcile"/> (already <c>done</c>) so that pass is
    /// not re-run; clear the value to run again.
    /// </summary>
    public const string ReturnStatusBackfill = "maintenance.order_return_status_v1";

    /// <summary>Default chat for after-sales messages (same ops group as cancel/refund).</summary>
    public const string DefaultTelegramChatId = "-1004462111161";

    /// <summary>Default minutes between trigger calls.</summary>
    public const int DefaultTriggerIntervalMinutes = 30;

    /// <summary>Default minutes between pull runs.</summary>
    public const int DefaultPullIntervalMinutes = 15;

    /// <summary>Minimum interval for both loops.</summary>
    public const int MinIntervalMinutes = 1;

    /// <summary>Maximum interval for both loops (24 hours).</summary>
    public const int MaxIntervalMinutes = 1440;

    /// <summary>Return rows fetched per page (4Seller caps this at 100).</summary>
    public const int PageSize = 100;

    /// <summary>
    /// Pages a single owner may read in one pull. A return case that stays open for months keeps
    /// bubbling up on <c>platformUpdateTime</c>, so an owner with a stale watermark must not be able
    /// to walk the entire history in one pass and stall every other owner behind the gate.
    /// </summary>
    public const int MaxPagesPerOwner = 10;

    /// <summary>
    /// Page cap for the one-shot full-reconcile passes (<see cref="ReturnStatusBackfill"/>,
    /// <see cref="RefundCasePresenceReconcile"/> and the settled
    /// <see cref="RefundedAtCreateTimeRepair"/> / <see cref="OrphanRefundReconcile"/>).
    /// They have no watermark to stop at — the whole point is reaching every case an incremental pull
    /// already walked past — so the cap is only a runaway guard, not a budget.
    /// </summary>
    public const int RepairMaxPagesPerOwner = 100;
}
