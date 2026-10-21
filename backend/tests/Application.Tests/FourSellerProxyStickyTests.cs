using Application.Interfaces;
using Infrastructure.Integrations.FourSeller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class FourSellerProxyStickyTests
{
    [Fact]
    public async Task ResolveAsync_PersonalProxy_AlwaysReturned_EvenWhenCoolingDown()
    {
        var (manager, _) = CreateManager(poolEnabled: true, pool: ["10.0.0.1:8000:u:p"]);
        var userId = Guid.NewGuid();
        const string sticky = "103.77.241.191:1948:kbiproxy:abc123";

        manager.ReportBlocked(sticky, "connect");
        Assert.True(manager.IsInCooldownForTests(sticky));

        var resolved = await manager.ResolveAsync(userId, sticky);

        Assert.Equal(sticky, resolved);
    }

    [Fact]
    public async Task ResolveAsync_NoPersonalProxy_UsesPoolWhenEnabled()
    {
        const string poolProxy = "10.0.0.1:8000:u:p";
        var (manager, _) = CreateManager(poolEnabled: true, pool: [poolProxy]);
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var resolved = await manager.ResolveAsync(userId, userProxy: null);

        Assert.Equal(poolProxy, resolved);
    }

    [Fact]
    public async Task ResolveAsync_NoPersonalProxy_PoolDisabled_ReturnsNull()
    {
        var (manager, _) = CreateManager(poolEnabled: false, pool: ["10.0.0.1:8000:u:p"]);

        var resolved = await manager.ResolveAsync(Guid.NewGuid(), userProxy: null);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_PersonalProxyCoolingDown_DoesNotFallThroughToPool()
    {
        const string sticky = "103.77.241.191:1948:kbiproxy:abc123";
        const string poolProxy = "10.0.0.1:8000:u:p";
        var (manager, _) = CreateManager(poolEnabled: true, pool: [poolProxy]);
        var userId = Guid.NewGuid();

        manager.ReportBlocked(sticky, "567");

        var resolved = await manager.ResolveAsync(userId, sticky);

        Assert.Equal(sticky, resolved);
        Assert.NotEqual(poolProxy, resolved);
    }

    private static (FourSellerProxyManager Manager, StubSettingProvider Settings) CreateManager(
        bool poolEnabled,
        IReadOnlyList<string> pool)
    {
        var settings = new StubSettingProvider();
        settings.Values[FourSellerProxySettingKeys.Enabled] = poolEnabled ? "true" : "false";
        settings.Values[FourSellerProxySettingKeys.Pool] = string.Join('\n', pool);
        settings.Values[FourSellerProxySettingKeys.CooldownMinutes] = "10";

        var services = new ServiceCollection();
        services.AddSingleton<ISettingProvider>(settings);
        var sp = services.BuildServiceProvider();

        var manager = new FourSellerProxyManager(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FourSellerProxyManager>.Instance);

        return (manager, settings);
    }
}
