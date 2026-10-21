namespace Shared.Constants;

/// <summary>
/// Setting keys for 4Seller session validation (is-login probe cache).
/// </summary>
public static class FourSellerSessionSettingKeys
{
    /// <summary>
    /// Minutes a successful is-login result is reused before probing again.
    /// Default 15; minimum 1. Reduces burst traffic from parallel DI scopes.
    /// </summary>
    public const string ValidateTtlMinutes = "fourseller.session.validate_ttl_minutes";

    /// <summary>Default TTL when the setting is missing or invalid.</summary>
    public const int DefaultValidateTtlMinutes = 15;
}
