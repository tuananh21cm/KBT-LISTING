using Application.Interfaces;
using Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Shared.Helpers;

namespace Infrastructure.Identity;

/// <summary>
/// Fulfil PIN hasher using <see cref="JwtSettings.PinPepper"/> (falls back to <see cref="JwtSettings.Secret"/>).
/// </summary>
public sealed class FulfilPinHasher : IFulfilPinHasher
{
    private readonly string _pepper;

    /// <summary>
    /// Initializes the hasher from JWT settings.
    /// </summary>
    public FulfilPinHasher(IOptions<JwtSettings> jwtSettings)
    {
        var jwt = jwtSettings.Value;
        _pepper = string.IsNullOrWhiteSpace(jwt.PinPepper) ? jwt.Secret : jwt.PinPepper;
    }

    /// <inheritdoc />
    public string Hash(string pin) => FulfilPin.ComputeHash(pin, _pepper);
}
