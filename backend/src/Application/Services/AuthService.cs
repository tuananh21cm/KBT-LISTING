using Application.DTOs.Auth;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Authentication service implementation.
/// </summary>
public class AuthService : IAuthService
{
    private const int PinFailMaxAttempts = 5;
    private static readonly TimeSpan PinFailWindow = TimeSpan.FromMinutes(15);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IFulfilPinHasher _pinHasher;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes the authentication service.
    /// </summary>
    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IFulfilPinHasher pinHasher,
        IMemoryCache cache)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _pinHasher = pinHasher;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            throw new UnauthorizedException(
                "Tài khoản tạm khóa do nhập sai mật khẩu nhiều lần. Thử lại sau 15 phút.");
        }
        if (!result.Succeeded)
        {
            throw new UnauthorizedException("Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        return await _tokenService.GenerateTokensAsync(user, ipAddress, cancellationToken, request.Persistent);
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> LoginWithPinAsync(
        PinLoginRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var failKey = PinFailCacheKey(ipAddress);
        if (_cache.TryGetValue(failKey, out int failCount) && failCount >= PinFailMaxAttempts)
        {
            throw new UnauthorizedException("Nhập sai mã PIN quá nhiều lần. Thử lại sau 15 phút.");
        }

        var hash = _pinHasher.Hash(request.Pin);
        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => u.FulfilPinHash == hash, cancellationToken);

        if (user is null || !user.IsActive)
        {
            RegisterPinFailure(failKey);
            throw new UnauthorizedException("Mã PIN không đúng.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(RoleNames.Fulfil) && !roles.Contains(RoleNames.Admin))
        {
            RegisterPinFailure(failKey);
            throw new UnauthorizedException("Mã PIN không đúng.");
        }

        _cache.Remove(failKey);
        return await _tokenService.GenerateTokensAsync(user, ipAddress, cancellationToken, request.Persistent);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(Guid userId, string refreshToken, CancellationToken cancellationToken = default)
    {
        await _tokenService.RevokeRefreshTokenAsync(userId, refreshToken, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        return await _tokenService.RefreshTokenAsync(
            request.RefreshToken,
            ipAddress,
            cancellationToken,
            request.Persistent);
    }

    private void RegisterPinFailure(string failKey)
    {
        var count = _cache.TryGetValue(failKey, out int existing) ? existing + 1 : 1;
        _cache.Set(failKey, count, PinFailWindow);
    }

    private static string PinFailCacheKey(string? ipAddress) =>
        $"fulfil-pin-fail:{(string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim())}";
}
