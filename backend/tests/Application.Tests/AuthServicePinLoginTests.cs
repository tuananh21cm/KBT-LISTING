using Application.DTOs.Auth;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Shared.Helpers;
using Xunit;

namespace Application.Tests;

/// <summary>
/// PIN-only login: role gate, inactive, wrong PIN, IP throttle.
/// </summary>
public class AuthServicePinLoginTests : IDisposable
{
    private const string Pepper = "unit-test-pepper";
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuthService _sut;
    private readonly IMemoryCache _cache;
    private readonly StubTokenService _tokens = new();

    public AuthServicePinLoginTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _userManager = NewUserManager(_db);
        _cache = new MemoryCache(new MemoryCacheOptions());
        var pinHasher = new TestPinHasher(Pepper);
        // SignInManager chỉ dùng cho login username/password — PIN path không gọi.
        _sut = new AuthService(_userManager, signInManager: null!, _tokens, pinHasher, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task LoginWithPin_Succeeds_ForActiveFulfilUser()
    {
        await SeedUserAsync("fulfil_ok", RoleNames.Fulfil, "4829", isActive: true);

        var result = await _sut.LoginWithPinAsync(
            new PinLoginRequestDto { Pin = "4829" },
            "10.0.0.1");

        Assert.Equal("access", result.AccessToken);
        Assert.Equal(1, _tokens.GenerateCalls);
    }

    [Fact]
    public async Task LoginWithPin_Succeeds_ForAdmin()
    {
        await SeedUserAsync("admin_ok", RoleNames.Admin, "1111", isActive: true);

        var result = await _sut.LoginWithPinAsync(
            new PinLoginRequestDto { Pin = "1111" },
            "10.0.0.2");

        Assert.Equal("access", result.AccessToken);
    }

    [Fact]
    public async Task LoginWithPin_Rejects_WrongPin()
    {
        await SeedUserAsync("fulfil_a", RoleNames.Fulfil, "2222", isActive: true);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.LoginWithPinAsync(new PinLoginRequestDto { Pin = "9999" }, "10.0.0.3"));

        Assert.Equal("Mã PIN không đúng.", ex.Message);
    }

    [Fact]
    public async Task LoginWithPin_Rejects_InactiveUser()
    {
        await SeedUserAsync("fulfil_off", RoleNames.Fulfil, "3333", isActive: false);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.LoginWithPinAsync(new PinLoginRequestDto { Pin = "3333" }, "10.0.0.4"));

        Assert.Equal("Mã PIN không đúng.", ex.Message);
    }

    [Fact]
    public async Task LoginWithPin_Rejects_SellerRole()
    {
        await SeedUserAsync("seller_x", RoleNames.Seller, "4444", isActive: true);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.LoginWithPinAsync(new PinLoginRequestDto { Pin = "4444" }, "10.0.0.5"));

        Assert.Equal("Mã PIN không đúng.", ex.Message);
    }

    [Fact]
    public async Task LoginWithPin_Throttles_AfterFiveFailures()
    {
        await SeedUserAsync("fulfil_t", RoleNames.Fulfil, "5555", isActive: true);
        const string ip = "10.0.0.9";

        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                _sut.LoginWithPinAsync(new PinLoginRequestDto { Pin = "0000" }, ip));
        }

        var locked = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _sut.LoginWithPinAsync(new PinLoginRequestDto { Pin = "5555" }, ip));

        Assert.Contains("quá nhiều lần", locked.Message);
    }

    private async Task SeedUserAsync(string name, string roleName, string pin, bool isActive)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role is null)
        {
            role = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant()
            };
            _db.Roles.Add(role);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            FullName = name,
            IsActive = isActive,
            Email = $"{name}@test.local",
            NormalizedEmail = $"{name}@test.local".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FulfilPinHash = FulfilPin.ComputeHash(pin, Pepper)
        };
        _db.Users.Add(user);
        _db.Set<IdentityUserRole<Guid>>().Add(new IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = role.Id
        });
        await _db.SaveChangesAsync();
    }

    private static UserManager<ApplicationUser> NewUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db);
        return new UserManager<ApplicationUser>(
            store,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private sealed class TestPinHasher(string pepper) : IFulfilPinHasher
    {
        public string Hash(string pin) => FulfilPin.ComputeHash(pin, pepper);
    }

    private sealed class StubTokenService : ITokenService
    {
        public int GenerateCalls { get; private set; }

        public Task<AuthResponseDto> GenerateTokensAsync(
            ApplicationUser user,
            string? ipAddress,
            CancellationToken cancellationToken = default,
            bool persistent = false)
        {
            GenerateCalls++;
            return Task.FromResult(new AuthResponseDto
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        }

        public Task<AuthResponseDto> RefreshTokenAsync(
            string refreshToken,
            string? ipAddress,
            CancellationToken cancellationToken = default,
            bool persistent = false) =>
            throw new NotImplementedException();

        public Task RevokeRefreshTokenAsync(Guid userId, string refreshToken, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
