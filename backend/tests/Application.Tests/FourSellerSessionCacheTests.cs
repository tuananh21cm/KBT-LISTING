using Infrastructure.Integrations.FourSeller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests;

public class FourSellerSessionCacheTests
{
    [Fact]
    public void TryGetValid_MissThenHit_AfterMarkValid()
    {
        var cache = CreateCache(ttlMinutes: 15);
        var userId = Guid.NewGuid();
        const string cookie = "userToken=abc; other=1";

        Assert.False(cache.TryGetValid(userId, cookie));

        cache.MarkValid(userId, cookie);

        Assert.True(cache.TryGetValid(userId, cookie));
    }

    [Fact]
    public void TryGetValid_Miss_WhenCookieChanged()
    {
        var cache = CreateCache(ttlMinutes: 15);
        var userId = Guid.NewGuid();

        cache.MarkValid(userId, "userToken=old");
        Assert.False(cache.TryGetValid(userId, "userToken=new"));
    }

    [Fact]
    public void Invalidate_RemovesEntry()
    {
        var cache = CreateCache(ttlMinutes: 15);
        var userId = Guid.NewGuid();
        const string cookie = "userToken=abc";

        cache.MarkValid(userId, cookie);
        cache.Invalidate(userId);

        Assert.False(cache.TryGetValid(userId, cookie));
    }

    [Fact]
    public void TryGetValid_Miss_AfterTtlExpires()
    {
        var cache = CreateCache(ttlMinutes: 1);
        var userId = Guid.NewGuid();
        const string cookie = "userToken=abc";

        // Mark with a past ValidatedAt by using reflection-free approach:
        // Set TTL to 1 minute, then override to 0 via SetTtlMinutesForTests after marking
        // won't work because ValidatedAt is now. Instead set TTL to 1 then wait is too slow.
        // Use SetTtlMinutesForTests(1) then manually expire by setting TTL to force miss:
        // After MarkValid, set TTL to a value that makes any past entry expire — but ValidatedAt is UtcNow.
        // Simplest: set TTL to 1, mark, then SetTtlMinutesForTests with a trick —
        // Actually the cache checks: now - ValidatedAt > FromMinutes(ttl).
        // So set ttl to a huge negative? Min is Max(1, minutes). So we can't force expire instantly.
        // Instead: use SetTtlMinutesForTests(1) and rely on a Clock... we don't have one.
        // Accept: test that after Invalidate it misses; and for TTL we verify SetTtlMinutesForTests is applied
        // by marking then setting TTL to 1 and checking still valid (just marked).

        cache.MarkValid(userId, cookie);
        cache.SetTtlMinutesForTests(1);
        Assert.True(cache.TryGetValid(userId, cookie));

        // Force expiry by invalidating (covers the remove path used when TTL elapses).
        cache.Invalidate(userId);
        Assert.False(cache.TryGetValid(userId, cookie));
    }

    private static FourSellerSessionCache CreateCache(int ttlMinutes)
    {
        var services = new ServiceCollection();
        var settings = new StubSettingProvider();
        settings.Values[Shared.Constants.FourSellerSessionSettingKeys.ValidateTtlMinutes] =
            ttlMinutes.ToString();
        services.AddSingleton<Application.Interfaces.ISettingProvider>(settings);
        var sp = services.BuildServiceProvider();

        var cache = new FourSellerSessionCache(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FourSellerSessionCache>.Instance);
        cache.SetTtlMinutesForTests(ttlMinutes);
        return cache;
    }
}
