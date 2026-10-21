namespace Application.Interfaces;

/// <summary>
/// Hashes Fulfil extension PINs with the configured JWT pepper (write-only storage).
/// </summary>
public interface IFulfilPinHasher
{
    /// <summary>
    /// HMAC-SHA256 hex of a normalized 4-digit PIN.
    /// </summary>
    string Hash(string pin);
}
