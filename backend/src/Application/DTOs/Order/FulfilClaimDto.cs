namespace Application.DTOs.Order;

/// <summary>Body for <c>POST /admin/orders/{id}/fulfil-claim</c>.</summary>
public class ClaimOrderRequest
{
    /// <summary>
    /// Other order ids in the same customer group (phone+zip) that the client wants claimed together.
    /// Server only accepts siblings whose phone+zip match the primary order.
    /// </summary>
    public List<Guid>? SiblingOrderIds { get; set; }

    /// <summary>Optional short label for the Chrome profile (shown in history).</summary>
    public string? SessionLabel { get; set; }
}

/// <summary>One order's claim / lease snapshot.</summary>
public class FulfilClaimItemDto
{
    /// <summary>Order id.</summary>
    public Guid OrderId { get; set; }

    /// <summary><c>held</c> | <c>released</c> | <c>purchased</c>.</summary>
    public string State { get; set; } = "held";

    /// <summary>User holding the order (null when free).</summary>
    public Guid? ClaimedByUserId { get; set; }

    /// <summary>Display name of the holder.</summary>
    public string? ClaimedByName { get; set; }

    /// <summary>When the first session of this user claimed (UTC).</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>Lease expiry = MAX of open session expires (UTC).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Number of Chrome-profile sessions currently holding.</summary>
    public int SessionCount { get; set; }

    /// <summary>Open session ids (for client badge: this session / my other / other user).</summary>
    public List<Guid> SessionIds { get; set; } = [];

    /// <summary>
    /// StartedAt of the calling session's open attempt when present; otherwise earliest open attempt.
    /// </summary>
    public DateTime? StartedAt { get; set; }
}

/// <summary>Result of claim / renew / release.</summary>
public class FulfilClaimResultDto
{
    /// <summary>Primary order claim state after the operation.</summary>
    public FulfilClaimItemDto Claim { get; set; } = null!;

    /// <summary>Sibling claims that succeeded in the same batch (empty when none).</summary>
    public List<FulfilClaimItemDto> SiblingClaims { get; set; } = [];

    /// <summary>
    /// Sibling order ids that could not be claimed (already held by someone else).
    /// Does not fail the primary claim.
    /// </summary>
    public List<FulfilClaimConflictDto> Conflicts { get; set; } = [];

    /// <summary>
    /// Orders this session previously held that were auto-released because a session
    /// may only focus one click-group at a time (primary + siblings in the same request).
    /// </summary>
    public List<FulfilClaimItemDto> ReleasedClaims { get; set; } = [];

    /// <summary>Server UTC clock for client countdown skew correction.</summary>
    public DateTime ServerTime { get; set; }
}

/// <summary>A sibling that failed to claim.</summary>
public class FulfilClaimConflictDto
{
    /// <summary>Sibling order id.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Who currently holds it.</summary>
    public Guid? ClaimedByUserId { get; set; }

    /// <summary>Holder display name.</summary>
    public string? ClaimedByName { get; set; }

    /// <summary>When their lease expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Human-readable reason.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Active claims list (lightweight poll / panel sync).</summary>
public class FulfilActiveClaimsDto
{
    /// <summary>Orders currently held (lease not expired, not purchased).</summary>
    public List<FulfilClaimItemDto> Items { get; set; } = [];

    /// <summary>Server UTC clock.</summary>
    public DateTime ServerTime { get; set; }
}

/// <summary>Query for fulfil attempt history / KPI.</summary>
public class FulfilAttemptListQuery
{
    /// <summary>1-based page.</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size (max 200).</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Filter by order.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Filter by fulfil user.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Filter by Chrome-profile session.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Inclusive lower bound on StartedAt (UTC).</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>Inclusive upper bound on StartedAt (UTC).</summary>
    public DateTime? DateTo { get; set; }
}

/// <summary>One row of attempt history.</summary>
public class FulfilAttemptDto
{
    /// <summary>Attempt id.</summary>
    public Guid Id { get; set; }

    /// <summary>Order id.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Platform order id for display.</summary>
    public string? PlatformOrderId { get; set; }

    /// <summary>Fulfil user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Fulfil display name.</summary>
    public string? UserName { get; set; }

    /// <summary>Chrome-profile session id.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Optional session label.</summary>
    public string? SessionLabel { get; set; }

    /// <summary>Batch id (customer-group claim).</summary>
    public Guid BatchId { get; set; }

    /// <summary><c>phone|zip</c> key when known.</summary>
    public string? CustomerKey { get; set; }

    /// <summary>When this session clicked the order.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Last heartbeat.</summary>
    public DateTime LastRenewedAt { get; set; }

    /// <summary>Session lease expiry at last renew.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When closed (null = still open).</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>Outcome when closed.</summary>
    public string? Outcome { get; set; }

    /// <summary>Purchase stamp when Outcome = Purchased.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>ClosedAt − StartedAt in seconds; null when not measurable.</summary>
    public int? DurationSeconds { get; set; }
}

/// <summary>Paged attempt history.</summary>
public class FulfilAttemptListPageDto
{
    /// <summary>Rows.</summary>
    public List<FulfilAttemptDto> Items { get; set; } = [];

    /// <summary>Total matching rows.</summary>
    public int Total { get; set; }

    /// <summary>1-based page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }
}

/// <summary>Known claim event states for SignalR <c>orderClaimChanged</c>.</summary>
public static class FulfilClaimStates
{
    /// <summary>Order is held by a fulfil user.</summary>
    public const string Held = "held";

    /// <summary>Order returned to the free pool.</summary>
    public const string Released = "released";

    /// <summary>Order was stamped purchased.</summary>
    public const string Purchased = "purchased";
}
