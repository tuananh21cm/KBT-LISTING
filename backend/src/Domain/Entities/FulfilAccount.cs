using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Purchase / fulfil shopping account (credentials used to buy supplier stock).
/// </summary>
public class FulfilAccount : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Login username.</summary>
    public string? Username { get; set; }

    /// <summary>Login password.</summary>
    public string? Password { get; set; }

    /// <summary>Account email.</summary>
    public string? Email { get; set; }

    /// <summary>Email password (passmail).</summary>
    public string? PassMail { get; set; }

    /// <summary>2FA / TOTP secret.</summary>
    public string? TwoFactorAuth { get; set; }

    /// <summary>Whether the account is usable.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Currently preferred payment card for this account (must be linked via AccountLinks).</summary>
    public Guid? ActiveCardId { get; set; }

    /// <summary>
    /// Egress proxy used when the backend calls SHEIN for this account, so every account
    /// leaves from a distinct IP. Formats: <c>host:port</c>, <c>host:port:user:pass</c>
    /// or <c>[scheme://][user:pass@]host:port</c>.
    /// </summary>
    public string? SheinProxy { get; set; }

    /// <summary>Captured SHEIN session headers (armortoken, smdeviceid, x-csrf-token, x-gw-auth…) as a JSON object.</summary>
    public string? SheinHeadersJson { get; set; }

    /// <summary>Captured SHEIN cookie header (<c>a=1; b=2</c>), including HttpOnly cookies.</summary>
    public string? SheinCookie { get; set; }

    /// <summary>Storefront origin the session belongs to, e.g. <c>https://us.shein.com</c>.</summary>
    public string? SheinOrigin { get; set; }

    /// <summary>When the extension captured the session currently stored.</summary>
    public DateTime? SheinSessionCapturedAt { get; set; }

    /// <summary>Last time the backend used this session against SHEIN.</summary>
    public DateTime? SheinSessionLastCheckedAt { get; set; }

    /// <summary>Last failure seen when using the session (auth/captcha/proxy); null when healthy.</summary>
    public string? SheinSessionLastError { get; set; }

    /// <summary>Active card navigation.</summary>
    public FulfilCard? ActiveCard { get; set; }

    /// <summary>Users this account is assigned to.</summary>
    public ICollection<FulfilAccountAssignee> Assignees { get; set; } = new List<FulfilAccountAssignee>();

    /// <summary>Card links (many-to-many).</summary>
    public ICollection<FulfilAccountCard> CardLinks { get; set; } = new List<FulfilAccountCard>();

    /// <summary>Transactions on this account.</summary>
    public ICollection<FulfilTransaction> Transactions { get; set; } = new List<FulfilTransaction>();
}
