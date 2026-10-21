namespace Application.DTOs.Fulfil;

/// <summary>Fulfil purchase account list/detail DTO.</summary>
public class FulfilAccountDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Username.</summary>
    public string? Username { get; set; }

    /// <summary>Password.</summary>
    public string? Password { get; set; }

    /// <summary>Email.</summary>
    public string? Email { get; set; }

    /// <summary>Email password.</summary>
    public string? PassMail { get; set; }

    /// <summary>2FA secret.</summary>
    public string? TwoFactorAuth { get; set; }

    /// <summary>Assignee user ids (cấp cho nhiều người).</summary>
    public List<Guid> AssigneeUserIds { get; set; } = [];

    /// <summary>Assignee display names.</summary>
    public List<string> AssigneeUserNames { get; set; } = [];

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }

    /// <summary>Active card id.</summary>
    public Guid? ActiveCardId { get; set; }

    /// <summary>Active card display name.</summary>
    public string? ActiveCardName { get; set; }

    /// <summary>Masked active card number.</summary>
    public string? ActiveCardNumberMasked { get; set; }

    /// <summary>Active card provider source name.</summary>
    public string? ActiveCardSourceName { get; set; }

    /// <summary>Active card provider source account name.</summary>
    public string? ActiveCardSourceAccountName { get; set; }

    /// <summary>Active card billing currency.</summary>
    public string? ActiveCardCurrency { get; set; }

    /// <summary>Linked card count.</summary>
    public int CardCount { get; set; }

    /// <summary>Egress proxy used when the backend calls SHEIN for this account.</summary>
    public string? SheinProxy { get; set; }

    /// <summary>Storefront origin the stored session belongs to.</summary>
    public string? SheinOrigin { get; set; }

    /// <summary>How many session headers are stored.</summary>
    public int SheinHeaderCount { get; set; }

    /// <summary>Whether a cookie header is stored.</summary>
    public bool SheinHasCookie { get; set; }

    /// <summary>True when every required header plus a cookie is present and no failure is recorded.</summary>
    public bool SheinSessionReady { get; set; }

    /// <summary>Required headers still missing from the stored session.</summary>
    public List<string> SheinMissingHeaders { get; set; } = [];

    /// <summary>When the extension captured the stored session.</summary>
    public DateTime? SheinSessionCapturedAt { get; set; }

    /// <summary>Last time the backend used the session against SHEIN.</summary>
    public DateTime? SheinSessionLastCheckedAt { get; set; }

    /// <summary>Last failure seen when using the session.</summary>
    public string? SheinSessionLastError { get; set; }

    /// <summary>Created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create fulfil account.</summary>
public class CreateFulfilAccountDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Username.</summary>
    public string? Username { get; set; }

    /// <summary>Password.</summary>
    public string? Password { get; set; }

    /// <summary>Email.</summary>
    public string? Email { get; set; }

    /// <summary>Email password.</summary>
    public string? PassMail { get; set; }

    /// <summary>2FA secret.</summary>
    public string? TwoFactorAuth { get; set; }

    /// <summary>Assignee user ids.</summary>
    public List<Guid> AssigneeUserIds { get; set; } = [];

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Egress proxy used when the backend calls SHEIN for this account.</summary>
    public string? SheinProxy { get; set; }
}

/// <summary>Update fulfil account.</summary>
public class UpdateFulfilAccountDto
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Username.</summary>
    public string? Username { get; set; }

    /// <summary>Password.</summary>
    public string? Password { get; set; }

    /// <summary>Email.</summary>
    public string? Email { get; set; }

    /// <summary>Email password.</summary>
    public string? PassMail { get; set; }

    /// <summary>2FA secret.</summary>
    public string? TwoFactorAuth { get; set; }

    /// <summary>Assignee user ids.</summary>
    public List<Guid> AssigneeUserIds { get; set; } = [];

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Egress proxy used when the backend calls SHEIN for this account.</summary>
    public string? SheinProxy { get; set; }
}

/// <summary>Set active card on an account.</summary>
public class SetActiveFulfilCardDto
{
    /// <summary>Card id (null clears active card).</summary>
    public Guid? CardId { get; set; }
}

/// <summary>
/// SHEIN storefront session pushed by the fulfil extension once its capture is complete.
/// The account is resolved by <see cref="AccountEmail"/> (the SHEIN billing email).
/// </summary>
public class SaveSheinSessionDto
{
    /// <summary>SHEIN account email, matched against <c>FulfilAccounts.Email</c>.</summary>
    public string? AccountEmail { get; set; }

    /// <summary>Explicit account id; wins over <see cref="AccountEmail"/> when provided.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Storefront origin, e.g. <c>https://us.shein.com</c>.</summary>
    public string? Origin { get; set; }

    /// <summary>Captured request headers keyed by lowercase header name.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>Cookie header string (<c>a=1; b=2</c>).</summary>
    public string? Cookie { get; set; }

    /// <summary>When the extension captured the session.</summary>
    public DateTime? CapturedAt { get; set; }
}

/// <summary>Result of storing a SHEIN session.</summary>
public class SheinSessionSavedDto
{
    /// <summary>Account the session was stored on.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Account display name.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Account email.</summary>
    public string? AccountEmail { get; set; }

    /// <summary>How many headers were stored.</summary>
    public int HeaderCount { get; set; }

    /// <summary>Whether a cookie was stored.</summary>
    public bool HasCookie { get; set; }

    /// <summary>Whether the account has an egress proxy configured.</summary>
    public bool HasProxy { get; set; }

    /// <summary>True when the stored session is complete enough for the backend to call SHEIN.</summary>
    public bool Ready { get; set; }

    /// <summary>Required headers still missing.</summary>
    public List<string> MissingHeaders { get; set; } = [];

    /// <summary>Capture timestamp stored.</summary>
    public DateTime CapturedAt { get; set; }
}

/// <summary>
/// Full stored SHEIN session for one account, including the raw cookie and headers.
///
/// Served by its own endpoint rather than folded into <c>GET /accounts</c>: the cookie alone runs
/// to ~16 KB, and the accounts list loads on every visit to the page.
/// </summary>
public class SheinSessionDetailDto
{
    /// <summary>Account the session belongs to.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Account display name.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Storefront origin the session belongs to.</summary>
    public string? Origin { get; set; }

    /// <summary>Raw cookie header (<c>a=1; b=2</c>), null when nothing is stored.</summary>
    public string? Cookie { get; set; }

    /// <summary>Stored request headers keyed by lowercase name.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>
    /// User-Agent from <see cref="Headers"/>, surfaced separately because it must match the
    /// browser that earned <c>cf_clearance</c> — Cloudflare binds that cookie to IP + UA.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>Egress proxy the backend leaves from for this account.</summary>
    public string? Proxy { get; set; }

    /// <summary>When the session was captured.</summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>Last time the backend used the session against SHEIN.</summary>
    public DateTime? LastCheckedAt { get; set; }

    /// <summary>Last failure seen when using the session.</summary>
    public string? LastError { get; set; }

    /// <summary>Required headers still missing.</summary>
    public List<string> MissingHeaders { get; set; } = [];

    /// <summary>True when the stored session is complete enough for the backend to call SHEIN.</summary>
    public bool Ready { get; set; }
}

/// <summary>
/// Manual session overwrite from the admin UI. Headers may arrive as a dictionary or as a pasted
/// <c>name: value</c> block; sending both empty clears the session.
/// </summary>
public class SaveSheinSessionManualDto
{
    /// <summary>Storefront origin; blank falls back to the default storefront.</summary>
    public string? Origin { get; set; }

    /// <summary>Cookie header string.</summary>
    public string? Cookie { get; set; }

    /// <summary>Headers keyed by name. Merged with <see cref="HeadersText"/>, dictionary wins.</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>Headers pasted as <c>name: value</c> lines (DevTools "Copy request headers").</summary>
    public string? HeadersText { get; set; }

    /// <summary>User-Agent to replay; stored as the <c>user-agent</c> header.</summary>
    public string? UserAgent { get; set; }
}

/// <summary>Outcome of probing a stored SHEIN session against the storefront.</summary>
public class TestSheinSessionResultDto
{
    /// <summary>True when SHEIN answered with a usable payload.</summary>
    public bool Ok { get; set; }

    /// <summary>Failure reason in Vietnamese when <see cref="Ok"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>True when the failure means the session itself is dead (re-capture needed).</summary>
    public bool SessionInvalid { get; set; }

    /// <summary>True when the failure looks like the proxy being blocked or unreachable.</summary>
    public bool ProxyFailed { get; set; }

    /// <summary>Billno probed, or null when the account had none and the list page was used.</summary>
    public string? Billno { get; set; }

    /// <summary>Round-trip time of the probe.</summary>
    public long ElapsedMs { get; set; }
}

/// <summary>Dropdown option for fulfil accounts.</summary>
public class FulfilAccountOptionDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; }

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Login / SHEIN email (để extension map tài khoản đang sync).</summary>
    public string? Email { get; set; }

    /// <summary>Active card id if any.</summary>
    public Guid? ActiveCardId { get; set; }
}
